using System;

namespace readboard
{
    internal interface ISyncSessionCoordinator
    {
        void AttachHost(IProtocolCommandHost host);
        void AttachRuntime(SyncSessionRuntimeDependencies runtimeDependencies);
        void BindSessionState(SessionState state);
        void Start();
        void Stop();
        bool StartedSync { get; }
        bool KeepSync { get; }
        bool IsContinuousSyncing { get; }
        bool SyncBoth { get; }
        void SetSyncBoth(bool enabled);
        void SetCapturedFoxMoveNumber(int? foxMoveNumber);
        void BeginContinuousSync();
        void EndContinuousSync();
        void BeginKeepSync();
        void EndKeepSync();
        bool WaitForContinuousSyncStop(int millisecondsTimeout);
        bool WaitForSyncIdle(int millisecondsTimeout);
        bool TryQueuePendingMove(MoveRequest request, int boardPixelWidth, int boardWidth);
        bool TryTakePendingMove(out MoveRequest request);
        void HandlePendingMovePlacementResult(bool success);
        bool WaitForPendingMoveResult();
        void ResolvePendingMove(BoardSnapshot snapshot, int boardWidth);
        void CancelPendingMove();
        void ResetSyncCaches();
        void SendClear();
        void SendOverlayLine(string protocolLine);
        void SendBoardSnapshot(BoardSnapshot snapshot);
        void NotifyReady(bool playPonderEnabled);
        void SendPonderStatus(bool playPonderEnabled);
        void SendVersion(string version);
        void SendSync();
        void SendStopSync();
        void SendBothSync(bool enabled);
        void SendEndSync();
        void SendForegroundFoxInBoard(bool enabled);
        void SendStart(int boardWidth, int boardHeight, IntPtr windowHandle, bool includeWindowHandle);
        void SendPlay(string color, string time, string playouts, string firstPolicy);
        void SendNoInBoard();
        void SendNotInBoard();
        void SendPlacementResult(bool success);
        void SendTimeChanged(string numericValue);
        void SendPlayoutsChanged(string numericValue);
        void SendFirstPolicyChanged(string numericValue);
        void SendNoPonder();
        void SendStopAutoPlay();
        void SendPass();
        void SendShutdownProtocol();
        void SendLine(string line);
        void SendError(string message);
        bool TryRunOneTimeSync();
        bool TryStartKeepSync();
        bool TryStartContinuousSync();
        void StopSyncSession();
        PlaceRequestExecutionResult HandlePlaceRequest(MoveRequest request);
    }
}
