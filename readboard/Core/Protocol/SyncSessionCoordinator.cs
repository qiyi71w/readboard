using System;
using System.Collections.Generic;
using System.Threading;

namespace readboard
{
    internal sealed partial class SyncSessionCoordinator : ISyncSessionCoordinator
    {
        private const int MoveVerifyMaxAttempts = 10;
        private const int PendingMoveWaitTimeoutMs = 250;

        private readonly IReadBoardTransport transport;
        private readonly IReadBoardProtocolAdapter protocolAdapter;
        private readonly object stateLock = new object();
        private readonly object outboundProtocolSyncRoot = new object();
        private readonly AutoResetEvent pendingMoveEvent = new AutoResetEvent(false);
        private readonly ManualResetEventSlim pendingMoveAvailableEvent = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim continuousSyncStoppedEvent = new ManualResetEventSlim(true);
        private readonly ManualResetEventSlim syncIdleEvent = new ManualResetEventSlim(true);
        private volatile bool acceptingInboundProtocolMessages;
        private volatile bool outboundProtocolClosed;
        private int? lastCapturedFoxMoveNumber;
        private int? lastSentBoardFoxMoveNumber;
        private SessionState sessionState;
        private IProtocolCommandHost host;

        public SyncSessionCoordinator(IReadBoardTransport transport, IReadBoardProtocolAdapter protocolAdapter)
        {
            if (transport == null)
                throw new ArgumentNullException("transport");
            if (protocolAdapter == null)
                throw new ArgumentNullException("protocolAdapter");

            this.transport = transport;
            this.protocolAdapter = protocolAdapter;
            sessionState = new SessionState();
        }

        public bool StartedSync
        {
            get
            {
                lock (stateLock)
                    return sessionState.StartedSync;
            }
        }

        public bool KeepSync
        {
            get
            {
                lock (stateLock)
                    return sessionState.KeepSync;
            }
        }

        public bool IsContinuousSyncing
        {
            get
            {
                lock (stateLock)
                    return sessionState.IsContinuousSyncing;
            }
        }

        public bool SyncBoth
        {
            get
            {
                lock (stateLock)
                    return sessionState.SyncBoth;
            }
        }

        public void AttachHost(IProtocolCommandHost host)
        {
            if (host == null)
                throw new ArgumentNullException("host");
            this.host = host;
        }

        public void BindSessionState(SessionState state)
        {
            if (state == null)
                throw new ArgumentNullException("state");

            lock (stateLock)
            {
                sessionState = state;
            }
            UpdateContinuousSyncStoppedEvent();
            UpdateSyncIdleEvent();
        }

        public void SetCapturedFoxMoveNumber(int? foxMoveNumber)
        {
            lock (stateLock)
            {
                lastCapturedFoxMoveNumber = foxMoveNumber;
            }
        }

        public void Start()
        {
            outboundProtocolClosed = false;
            acceptingInboundProtocolMessages = true;
            transport.MessageReceived += OnMessageReceived;
            transport.Start();
        }

        public void Stop()
        {
            CloseOutboundProtocol();
            acceptingInboundProtocolMessages = false;
            StopSyncSessionCore(false);
            transport.MessageReceived -= OnMessageReceived;
            CancelPendingMove();
            continuousSyncStoppedEvent.Set();
            syncIdleEvent.Set();
            transport.Stop();
        }

        public void SetSyncBoth(bool enabled)
        {
            lock (stateLock)
            {
                sessionState.SyncBoth = enabled;
            }
        }

        public void BeginContinuousSync()
        {
            lock (stateLock)
            {
                sessionState.IsContinuousSyncing = true;
            }
            UpdateContinuousSyncStoppedEvent();
        }

        public void EndContinuousSync()
        {
            lock (stateLock)
            {
                sessionState.IsContinuousSyncing = false;
            }
            UpdateContinuousSyncStoppedEvent();
        }

        public void BeginKeepSync()
        {
            lock (stateLock)
            {
                sessionState.StartedSync = true;
                sessionState.KeepSync = true;
            }
            UpdateSyncIdleEvent();
        }

        public void EndKeepSync()
        {
            bool shouldSignal = false;
            lock (stateLock)
            {
                shouldSignal = TryFailPendingMoveOnKeepSyncStop();
                sessionState.StartedSync = false;
                sessionState.KeepSync = false;
                ResetSyncCachesCore();
            }
            if (shouldSignal)
                pendingMoveEvent.Set();
            UpdateSyncIdleEvent();
        }

        public bool WaitForContinuousSyncStop(int millisecondsTimeout)
        {
            return continuousSyncStoppedEvent.Wait(millisecondsTimeout);
        }

        public bool WaitForSyncIdle(int millisecondsTimeout)
        {
            return syncIdleEvent.Wait(millisecondsTimeout);
        }

        public bool TryQueuePendingMove(MoveRequest request, int boardPixelWidth, int boardWidth)
        {
            if (request == null)
                return false;

            lock (stateLock)
            {
                if (!sessionState.KeepSync || !sessionState.SyncBoth || boardPixelWidth < boardWidth)
                    return false;

                PendingMoveState pendingMove = sessionState.PendingMove;
                if (pendingMove.Active || pendingMove.Completed)
                    return false;

                pendingMove.Reset();
                pendingMove.X = request.X;
                pendingMove.Y = request.Y;
                pendingMove.AttemptsRemaining = request.VerifyMove ? MoveVerifyMaxAttempts : 1;
                pendingMove.VerifyMove = request.VerifyMove;
                pendingMove.Active = true;
                UpdatePendingMoveAvailableEventUnsafe();
                return true;
            }
        }

        public bool TryTakePendingMove(out MoveRequest request)
        {
            request = null;
            bool shouldSignal = false;

            lock (stateLock)
            {
                PendingMoveState pendingMove = sessionState.PendingMove;
                if (pendingMove == null || !pendingMove.Active || pendingMove.Completed)
                    return false;
                if (pendingMove.PlacementInProgress)
                    return false;

                if (pendingMove.AttemptsRemaining <= 0)
                {
                    shouldSignal = TryCompletePendingMove(false);
                }
                else
                {
                    pendingMove.AttemptsRemaining--;
                    pendingMove.PlacementInProgress = true;
                    request = new MoveRequest
                    {
                        X = pendingMove.X,
                        Y = pendingMove.Y,
                        VerifyMove = pendingMove.VerifyMove
                    };
                }

                UpdatePendingMoveAvailableEventUnsafe();
            }

            if (shouldSignal)
                pendingMoveEvent.Set();
            return request != null;
        }

        public void HandlePendingMovePlacementResult(bool success)
        {
            bool shouldSignal = false;

            lock (stateLock)
            {
                PendingMoveState pendingMove = sessionState.PendingMove;
                if (pendingMove == null || !pendingMove.Active || pendingMove.Completed)
                    return;

                pendingMove.PlacementInProgress = false;
                if (pendingMove.VerifyMove && success && sessionState.KeepSync)
                {
                    UpdatePendingMoveAvailableEventUnsafe();
                    return;
                }

                shouldSignal = TryCompletePendingMove(success);
                UpdatePendingMoveAvailableEventUnsafe();
            }

            if (shouldSignal)
                pendingMoveEvent.Set();
        }

        public bool WaitForPendingMoveResult()
        {
            while (true)
            {
                lock (stateLock)
                {
                    PendingMoveState pendingMove = sessionState.PendingMove;
                    if (pendingMove != null && pendingMove.Completed)
                    {
                        bool result = pendingMove.Succeeded;
                        pendingMove.Reset();
                        UpdatePendingMoveAvailableEventUnsafe();
                        return result;
                    }
                    if (!sessionState.KeepSync && !IsPendingMoveAwaitingPlacementResult(pendingMove))
                    {
                        if (pendingMove != null)
                            pendingMove.Reset();
                        UpdatePendingMoveAvailableEventUnsafe();
                        return false;
                    }
                }

                pendingMoveEvent.WaitOne(PendingMoveWaitTimeoutMs);
            }
        }

        public void ResolvePendingMove(BoardSnapshot snapshot, int boardWidth)
        {
            bool shouldSignal = false;
            int effectiveBoardWidth = boardWidth > 0
                ? boardWidth
                : snapshot == null ? 0 : snapshot.Width;

            lock (stateLock)
            {
                PendingMoveState pendingMove = sessionState.PendingMove;
                if (pendingMove == null || !pendingMove.Active || pendingMove.Completed || !pendingMove.VerifyMove)
                    return;

                if (IsPendingMoveVisible(snapshot, effectiveBoardWidth, pendingMove))
                {
                    shouldSignal = TryCompletePendingMove(true);
                }
                else if (pendingMove.AttemptsRemaining <= 0)
                {
                    shouldSignal = TryCompletePendingMove(false);
                }

                UpdatePendingMoveAvailableEventUnsafe();
            }

            if (shouldSignal)
                pendingMoveEvent.Set();
        }

        public void CancelPendingMove()
        {
            bool shouldSignal = false;

            lock (stateLock)
            {
                shouldSignal = TryCancelPendingMove();
            }

            if (shouldSignal)
                pendingMoveEvent.Set();
        }

        public void ResetSyncCaches()
        {
            lock (stateLock)
            {
                ResetSyncCachesCore();
            }
        }

        public void SendClear()
        {
            ResetSyncCaches();
            SendProtocolMessage(protocolAdapter.CreateClearMessage());
        }

        public void SendOverlayLine(string protocolLine)
        {
            if (string.IsNullOrWhiteSpace(protocolLine))
                return;

            lock (stateLock)
            {
                if (string.Equals(sessionState.LastOverlayProtocolLine, protocolLine, StringComparison.Ordinal))
                    return;
                sessionState.LastOverlayProtocolLine = protocolLine;
            }

            SendProtocolLine(protocolLine);
        }

        public void SendBoardSnapshot(BoardSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.Payload))
                return;

            IList<string> protocolLines = snapshot.ProtocolLines;
            if (protocolLines == null || protocolLines.Count == 0)
                return;

            int? effectiveFoxMoveNumber = ResolveEffectiveFoxMoveNumber(snapshot.FoxMoveNumber);
            lock (stateLock)
            {
                if (string.Equals(sessionState.LastBoardPayload, snapshot.Payload, StringComparison.Ordinal)
                    && lastSentBoardFoxMoveNumber == effectiveFoxMoveNumber)
                    return;
                sessionState.LastBoardPayload = snapshot.Payload;
                lastSentBoardFoxMoveNumber = effectiveFoxMoveNumber;
            }

            SendFoxMoveNumber(effectiveFoxMoveNumber);
            for (int i = 0; i < protocolLines.Count; i++)
                SendProtocolLine(protocolLines[i]);
            SendProtocolMessage(protocolAdapter.CreateBoardEndMessage());
        }

        public void NotifyReady(bool playPonderEnabled)
        {
            SendProtocolMessage(protocolAdapter.CreateReadyMessage());
            SendPonderStatus(playPonderEnabled);
        }

        public void SendPonderStatus(bool playPonderEnabled)
        {
            SendProtocolMessage(protocolAdapter.CreatePonderStatusMessage(playPonderEnabled));
        }

        public void SendVersion(string version)
        {
            SendProtocolMessage(protocolAdapter.CreateVersionMessage(version));
        }

        public void SendSync()
        {
            SendProtocolMessage(protocolAdapter.CreateSyncMessage());
        }

        public void SendStopSync()
        {
            SendProtocolMessage(protocolAdapter.CreateStopSyncMessage());
        }

        public void SendBothSync(bool enabled)
        {
            SendProtocolMessage(protocolAdapter.CreateBothSyncMessage(enabled));
        }

        public void SendEndSync()
        {
            SendProtocolMessage(protocolAdapter.CreateEndSyncMessage());
        }

        public void SendForegroundFoxInBoard(bool enabled)
        {
            SendProtocolMessage(protocolAdapter.CreateForegroundFoxInBoardMessage(enabled));
        }

        public void SendStart(int boardWidth, int boardHeight, IntPtr windowHandle, bool includeWindowHandle)
        {
            SendProtocolMessage(protocolAdapter.CreateStartMessage(boardWidth, boardHeight, windowHandle, includeWindowHandle));
        }

        public void SendPlay(string color, string time, string playouts, string firstPolicy)
        {
            SendProtocolMessage(protocolAdapter.CreatePlayMessage(color, time, playouts, firstPolicy));
        }

        public void SendNoInBoard()
        {
            SendProtocolMessage(protocolAdapter.CreateNoInBoardMessage());
        }

        public void SendNotInBoard()
        {
            SendProtocolMessage(protocolAdapter.CreateNotInBoardMessage());
        }

        public void SendPlacementResult(bool success)
        {
            SendProtocolMessage(protocolAdapter.CreatePlacementResultMessage(success));
        }

        public void SendTimeChanged(string numericValue)
        {
            SendProtocolMessage(protocolAdapter.CreateTimeChangedMessage(numericValue));
        }

        public void SendPlayoutsChanged(string numericValue)
        {
            SendProtocolMessage(protocolAdapter.CreatePlayoutsChangedMessage(numericValue));
        }

        public void SendFirstPolicyChanged(string numericValue)
        {
            SendProtocolMessage(protocolAdapter.CreateFirstPolicyChangedMessage(numericValue));
        }

        public void SendNoPonder()
        {
            SendProtocolMessage(protocolAdapter.CreateNoPonderMessage());
        }

        public void SendStopAutoPlay()
        {
            SendProtocolMessage(protocolAdapter.CreateStopAutoPlayMessage());
        }

        public void SendPass()
        {
            SendProtocolMessage(protocolAdapter.CreatePassMessage());
        }

        public void SendShutdownProtocol()
        {
            lock (outboundProtocolSyncRoot)
            {
                if (outboundProtocolClosed)
                    return;

                SendProtocolMessageCore(protocolAdapter.CreateStopSyncMessage());
                SendProtocolMessageCore(protocolAdapter.CreateBothSyncMessage(false));
                SendProtocolMessageCore(protocolAdapter.CreateEndSyncMessage());
                outboundProtocolClosed = true;
            }
        }

        public void SendLine(string line)
        {
            SendProtocolLine(line);
        }

        public void SendError(string message)
        {
            lock (outboundProtocolSyncRoot)
            {
                if (outboundProtocolClosed)
                    return;

                transport.SendError(message);
            }
        }

        private void OnMessageReceived(object sender, string rawLine)
        {
            if (!acceptingInboundProtocolMessages)
                return;
            ProtocolMessage message = protocolAdapter.ParseInbound(rawLine);
            IProtocolCommandHost currentHost = host;
            Action command = CreateDispatchCommand(currentHost, message);
            if (command == null || currentHost == null)
                return;
            currentHost.DispatchProtocolCommand(delegate
            {
                if (!acceptingInboundProtocolMessages)
                    return;
                command();
            });
        }

        private Action CreateDispatchCommand(IProtocolCommandHost currentHost, ProtocolMessage message)
        {
            if (currentHost == null || message == null)
                return null;

            switch (message.Kind)
            {
                case ProtocolMessageKind.PlaceMove:
                    return () => currentHost.HandlePlaceRequest(message.MoveRequest);
                case ProtocolMessageKind.LossFocus:
                    return currentHost.HandleLossFocus;
                case ProtocolMessageKind.StopInBoard:
                    return currentHost.HandleStopInBoardRequest;
                case ProtocolMessageKind.VersionRequest:
                    return currentHost.HandleVersionRequest;
                case ProtocolMessageKind.Quit:
                    return currentHost.HandleQuitRequest;
                default:
                    return null;
            }
        }

        private bool IsPendingMoveVisible(BoardSnapshot snapshot, int boardWidth, PendingMoveState pendingMove)
        {
            if (snapshot == null || !snapshot.IsValid || snapshot.BoardState == null)
                return false;
            if (pendingMove == null || pendingMove.X < 0 || pendingMove.Y < 0 || boardWidth <= 0)
                return false;

            int index = (pendingMove.Y * boardWidth) + pendingMove.X;
            return index >= 0
                && index < snapshot.BoardState.Length
                && snapshot.BoardState[index] != BoardCellState.Empty;
        }

        private int? ResolveEffectiveFoxMoveNumber(int? foxMoveNumber)
        {
            if (foxMoveNumber.HasValue)
                return foxMoveNumber;

            lock (stateLock)
            {
                return lastCapturedFoxMoveNumber;
            }
        }

        private void SendFoxMoveNumber(int? foxMoveNumber)
        {
            if (!foxMoveNumber.HasValue)
                return;

            SendProtocolMessage(protocolAdapter.CreateFoxMoveNumberMessage(foxMoveNumber.Value));
        }

        private void ResetSyncCachesCore()
        {
            sessionState.LastBoardPayload = null;
            sessionState.LastOverlayProtocolLine = null;
            lastSentBoardFoxMoveNumber = null;
        }

        private bool TryCompletePendingMove(bool success)
        {
            PendingMoveState pendingMove = sessionState.PendingMove;
            if (pendingMove == null || pendingMove.Completed || !pendingMove.Active)
                return false;

            pendingMove.PlacementInProgress = false;
            pendingMove.Active = false;
            pendingMove.Completed = true;
            pendingMove.Succeeded = success;
            UpdatePendingMoveAvailableEventUnsafe();
            return true;
        }

        private bool TryFailPendingMoveOnKeepSyncStop()
        {
            PendingMoveState pendingMove = sessionState.PendingMove;
            if (pendingMove == null || pendingMove.PlacementInProgress)
                return false;

            return TryCompletePendingMove(false);
        }

        private bool TryCancelPendingMove()
        {
            PendingMoveState pendingMove = sessionState.PendingMove;
            if (pendingMove == null || pendingMove.PlacementInProgress || pendingMove.Completed)
                return false;

            if (TryCompletePendingMove(false))
                return true;

            pendingMove.Reset();
            UpdatePendingMoveAvailableEventUnsafe();
            return false;
        }

        private void UpdatePendingMoveAvailableEventUnsafe()
        {
            PendingMoveState pendingMove = sessionState.PendingMove;
            if (pendingMove != null
                && pendingMove.Active
                && !pendingMove.Completed
                && !pendingMove.PlacementInProgress)
            {
                pendingMoveAvailableEvent.Set();
                return;
            }

            pendingMoveAvailableEvent.Reset();
        }

        private static bool IsPendingMoveAwaitingPlacementResult(PendingMoveState pendingMove)
        {
            return pendingMove != null
                && pendingMove.Active
                && pendingMove.PlacementInProgress;
        }

        private void UpdateSyncIdleEvent()
        {
            bool isIdle;
            lock (stateLock)
            {
                isIdle = !sessionState.StartedSync && !sessionState.KeepSync;
            }

            if (isIdle)
            {
                syncIdleEvent.Set();
                return;
            }

            syncIdleEvent.Reset();
        }

        private void UpdateContinuousSyncStoppedEvent()
        {
            bool isStopped;
            lock (stateLock)
            {
                isStopped = !sessionState.IsContinuousSyncing;
            }

            if (isStopped)
            {
                continuousSyncStoppedEvent.Set();
                return;
            }

            continuousSyncStoppedEvent.Reset();
        }

        private void SendProtocolLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            SendProtocolMessage(ProtocolMessage.CreateLegacyLine(line));
        }

        private void SendProtocolMessage(ProtocolMessage message)
        {
            lock (outboundProtocolSyncRoot)
            {
                if (outboundProtocolClosed)
                    return;

                SendProtocolMessageCore(message);
            }
        }

        private void CloseOutboundProtocol()
        {
            lock (outboundProtocolSyncRoot)
                outboundProtocolClosed = true;
        }

        private void SendProtocolMessageCore(ProtocolMessage message)
        {
            string line = protocolAdapter.Serialize(message);
            if (string.IsNullOrWhiteSpace(line))
                return;

            transport.Send(line);
        }
    }
}
