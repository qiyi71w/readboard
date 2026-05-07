using System;
using System.Collections.Generic;
using System.Threading;

namespace readboard
{
    internal sealed partial class SyncSessionCoordinator
    {
        private const int ContinuousSyncPollIntervalMs = 100;

        private readonly object workerLock = new object();
        private readonly ManualResetEventSlim keepSyncStopRequestedEvent = new ManualResetEventSlim(true);
        private SyncSessionRuntimeDependencies runtimeDependencies;
        private readonly SyncSessionRuntimeState runtimeState = new SyncSessionRuntimeState();
        private Thread continuousSyncThread;
        private Thread keepSyncThread;
        private int syncLifecycleGeneration;
        private int nextContinuousSyncSessionId;
        private int activeContinuousSyncSessionId;
        private int nextKeepSyncSessionId;
        private int activeKeepSyncSessionId;

        public void AttachRuntime(SyncSessionRuntimeDependencies runtimeDependencies)
        {
            if (runtimeDependencies == null)
                throw new ArgumentNullException("runtimeDependencies");
            if (runtimeDependencies.Host == null)
                throw new ArgumentNullException("runtimeDependencies.Host");
            if (runtimeDependencies.CaptureService == null)
                throw new ArgumentNullException("runtimeDependencies.CaptureService");
            if (runtimeDependencies.RecognitionService == null)
                throw new ArgumentNullException("runtimeDependencies.RecognitionService");
            if (runtimeDependencies.PlacementService == null)
                throw new ArgumentNullException("runtimeDependencies.PlacementService");
            if (runtimeDependencies.OverlayService == null)
                throw new ArgumentNullException("runtimeDependencies.OverlayService");

            runtimeDependencies.WindowLocator = runtimeDependencies.WindowLocator ?? new LegacySyncWindowLocator();
            runtimeDependencies.WindowDescriptorFactory = runtimeDependencies.WindowDescriptorFactory ?? new LegacyWindowDescriptorFactory();
            this.runtimeDependencies = runtimeDependencies;
        }

        public bool TryRunOneTimeSync()
        {
            SyncSessionRuntimeDependencies runtime = GetRuntimeDependencies();
            ResetRuntimeSyncCaches(runtime);
            SyncCoordinatorHostSnapshot snapshot;
            if (!TryCaptureSnapshot(runtime, out snapshot))
                return false;
            runtimeState.SelectedWindowHandle = snapshot.SelectedWindowHandle;
            runtimeState.ResetProbeState();
            if (!EnsureSyncSourceSelected(runtime, snapshot, true))
                return false;

            RecognizedSyncSample sample;
            if (!TryRecognizeSample(runtime, snapshot, true, out sample))
            {
                runtime.Host.ShowRecognitionFailureMessage();
                return false;
            }

            DispatchRecognizedSampleProtocol(BuildRecognizedSampleProtocolDispatch(snapshot, sample, true), null);
            return true;
        }

        public bool TryStartKeepSync()
        {
            SyncSessionRuntimeDependencies runtime = GetRuntimeDependencies();
            if (StartedSync || IsContinuousSyncing)
                return false;
            int lifecycleGeneration = CaptureSyncLifecycleGeneration();

            SyncCoordinatorHostSnapshot snapshot;
            if (!TryCaptureSnapshot(runtime, out snapshot))
                return false;
            if (!IsKeepSyncStartCurrent(lifecycleGeneration, false))
                return false;
            runtimeState.SelectedWindowHandle = snapshot.SelectedWindowHandle;
            return TryStartKeepSyncSession(runtime, snapshot, true, lifecycleGeneration, false);
        }

        public bool TryStartContinuousSync()
        {
            SyncSessionRuntimeDependencies runtime = GetRuntimeDependencies();
            if (StartedSync || IsContinuousSyncing)
                return false;
            int lifecycleGeneration = CaptureSyncLifecycleGeneration();
            int continuousSyncSessionId;

            if (!TryActivateContinuousSyncSession(runtime, lifecycleGeneration, out continuousSyncSessionId))
                return false;

            SyncCoordinatorHostSnapshot snapshot;
            if (TryCaptureSnapshot(runtime, out snapshot)
                && IsContinuousSyncWorkerCurrent(lifecycleGeneration, continuousSyncSessionId)
                && snapshot.AutoMinimize)
                runtime.Host.MinimizeWindow();
            return true;
        }

        public void StopSyncSession()
        {
            lock (stateLock)
            {
                runtimeState.LastCapturedYikeGeometry = null;
            }
            StopSyncSessionCore(false);
        }

        private void StopSyncSessionCore(bool waitForWorkers)
        {
            Thread keepWorker;
            Thread continuousWorker;

            lock (workerLock)
            {
                Interlocked.Increment(ref syncLifecycleGeneration);
                EndContinuousSync();
                EndKeepSync();
                keepSyncStopRequestedEvent.Set();
                keepWorker = keepSyncThread;
                continuousWorker = continuousSyncThread;
            }

            CompleteWorkerStop(keepWorker, waitForWorkers);
            CompleteWorkerStop(continuousWorker, waitForWorkers);
            if (waitForWorkers)
            {
                CompleteStopCleanup();
                return;
            }

            CompleteStopCleanupIfIdle();
        }

        public PlaceRequestExecutionResult HandlePlaceRequest(MoveRequest request)
        {
            if (request == null)
                return PlaceRequestExecutionResult.NoResponse;

            SyncCoordinatorHostSnapshot snapshot;
            if (!TryCaptureSnapshot(GetRuntimeDependencies(), out snapshot))
                return PlaceRequestExecutionResult.NoResponse;
            int boardWidth = snapshot.BoardWidth;
            if (!TryQueuePendingMove(request, runtimeState.CurrentBoardPixelWidth, boardWidth))
                return PlaceRequestExecutionResult.NoResponse;
            return PlaceRequestExecutionResult.CreateResponse(WaitForPendingMoveResult());
        }

        private bool TryActivateContinuousSyncSession(
            SyncSessionRuntimeDependencies runtime,
            int lifecycleGeneration,
            out int continuousSyncSessionId)
        {
            Thread worker;

            lock (workerLock)
            {
                if (!IsContinuousSyncStartCurrent(lifecycleGeneration))
                {
                    continuousSyncSessionId = 0;
                    return false;
                }

                BeginContinuousSync();
                continuousSyncSessionId = Interlocked.Increment(ref nextContinuousSyncSessionId);
                Volatile.Write(ref activeContinuousSyncSessionId, continuousSyncSessionId);
                runtime.Host.OnContinuousSyncStarted();
                worker = CreateContinuousSyncWorker(lifecycleGeneration, continuousSyncSessionId);
                continuousSyncThread = worker;
                worker.Start();
            }

            return true;
        }

        private void RunContinuousSyncLoop(int lifecycleGeneration, int continuousSyncSessionId)
        {
            SyncSessionRuntimeDependencies runtime = GetRuntimeDependencies();
            Func<bool> isOperationCurrent = delegate
            {
                return IsContinuousSyncWorkerCurrent(lifecycleGeneration, continuousSyncSessionId);
            };
            try
            {
                while (IsOperationCurrent(isOperationCurrent))
                {
                    if (!WaitForSyncIdle(ContinuousSyncPollIntervalMs))
                    {
                        if (!IsOperationCurrent(isOperationCurrent))
                            return;
                        continue;
                    }

                    SyncCoordinatorHostSnapshot snapshot;
                    if (!TryCaptureSnapshot(runtime, out snapshot))
                        return;
                    if (!IsOperationCurrent(isOperationCurrent))
                        return;
                    if (snapshot.ShowInBoard)
                        SendNoInBoard();
                    TryStartDiscoveredKeepSync(runtime, snapshot);
                    if (!IsOperationCurrent(isOperationCurrent))
                        return;
                    if (WaitForContinuousSyncStop(ContinuousSyncPollIntervalMs))
                        return;
                }
            }
            finally
            {
                FinishContinuousSyncLoop(runtime, continuousSyncSessionId);
            }
        }

        private void FinishContinuousSyncLoop(
            SyncSessionRuntimeDependencies runtime,
            int continuousSyncSessionId)
        {
            bool notifyStop = false;

            lock (workerLock)
            {
                if (continuousSyncThread != Thread.CurrentThread
                    || continuousSyncSessionId != Volatile.Read(ref activeContinuousSyncSessionId))
                    return;

                Volatile.Write(ref activeContinuousSyncSessionId, 0);
                continuousSyncThread = null;
                notifyStop = true;
            }

            if (notifyStop)
                runtime.Host.OnContinuousSyncStopped();
        }

        private void TryStartDiscoveredKeepSync(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot)
        {
            int lifecycleGeneration = CaptureSyncLifecycleGeneration();
            IntPtr handle = runtime.WindowLocator.FindWindowHandle(snapshot.SyncMode);
            if (handle == IntPtr.Zero || !IsKeepSyncStartCurrent(lifecycleGeneration, true))
                return;

            runtimeState.SelectedWindowHandle = handle;
            runtime.Host.UpdateSelectedWindowHandle(handle);
            SyncCoordinatorHostSnapshot refreshedSnapshot;
            if (!TryCaptureSnapshot(runtime, out refreshedSnapshot))
                return;
            if (!IsKeepSyncStartCurrent(lifecycleGeneration, true))
                return;
            TryStartKeepSyncSession(runtime, refreshedSnapshot, false, lifecycleGeneration, true);
        }

        private bool TryStartKeepSyncSession(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            bool showMessages,
            int lifecycleGeneration,
            bool requireContinuousSync)
        {
            runtimeState.ResetProbeState();
            SendForegroundFoxState(snapshot);
            if (!TryPrimeSyncFrame(
                runtime,
                snapshot,
                showMessages,
                delegate { return IsSyncLifecycleCurrent(lifecycleGeneration); }))
            {
                if (IsKeepSyncStartCurrent(lifecycleGeneration, requireContinuousSync))
                    HandleKeepSyncStartFailure(runtime, showMessages);
                return false;
            }

            return TryActivateKeepSyncSession(
                runtime,
                snapshot,
                lifecycleGeneration,
                requireContinuousSync);
        }

        private void HandleKeepSyncStartFailure(
            SyncSessionRuntimeDependencies runtime,
            bool restoreUi)
        {
            ResetRuntimeSyncCaches(runtime);
            ClearRuntimeFrame();
            runtimeState.ResetProbeState();
            CancelPendingMove();
            if (restoreUi)
                runtime.Host.OnKeepSyncStopped(false);
        }

        private bool TryActivateKeepSyncSession(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            int lifecycleGeneration,
            bool requireContinuousSync)
        {
            Thread worker;

            lock (workerLock)
            {
                if (!IsKeepSyncStartCurrent(lifecycleGeneration, requireContinuousSync))
                    return false;

                int keepSyncSessionId = Interlocked.Increment(ref nextKeepSyncSessionId);
                Volatile.Write(ref activeKeepSyncSessionId, keepSyncSessionId);
                BeginKeepSync();
                runtime.Host.OnKeepSyncStarted();
                ResetRuntimeSyncCaches(runtime);
                keepSyncStopRequestedEvent.Reset();
                worker = CreateKeepSyncWorker(lifecycleGeneration, keepSyncSessionId);
                keepSyncThread = worker;
                if (!IsContinuousSyncing && snapshot.AutoMinimize)
                    runtime.Host.MinimizeWindow();
                worker.Start();
            }

            return true;
        }

        private void RunKeepSyncLoop(int lifecycleGeneration, int keepSyncSessionId)
        {
            bool firstSample = true;
            SyncSessionRuntimeDependencies runtime = GetRuntimeDependencies();
            Func<bool> isOperationCurrent = delegate
            {
                return IsKeepSyncWorkerCurrent(lifecycleGeneration, keepSyncSessionId);
            };
            try
            {
                bool shouldSendSync = false;
                lock (workerLock)
                {
                    if (!IsOperationCurrent(isOperationCurrent))
                        return;
                    shouldSendSync = true;
                }
                if (shouldSendSync)
                    SendSync();

                while (IsOperationCurrent(isOperationCurrent))
                {
                    SyncCoordinatorHostSnapshot snapshot;
                    if (!TryCaptureSnapshot(runtime, out snapshot))
                        return;
                    if (!IsOperationCurrent(isOperationCurrent))
                        return;
                    runtimeState.SelectedWindowHandle = ResolveSelectedWindowHandle(snapshot);
                    if (!EnsureSyncSourceSelected(runtime, snapshot, false))
                        return;
                    if (!IsOperationCurrent(isOperationCurrent))
                        return;
                    DispatchPendingMove(runtime, snapshot, isOperationCurrent);
                    if (!IsOperationCurrent(isOperationCurrent))
                        return;
                    if (!TryProcessKeepSyncSample(runtime, snapshot, firstSample, isOperationCurrent))
                    {
                        if (firstSample || !IsOperationCurrent(isOperationCurrent))
                            return;
                    }
                    firstSample = false;
                    if (WaitForNextSample(snapshot.SampleIntervalMs))
                        return;
                }
            }
            finally
            {
                FinishKeepSyncLoop(runtime);
            }
        }

        private void FinishKeepSyncLoop(SyncSessionRuntimeDependencies runtime)
        {
            bool notifyStop = false;
            bool shouldSendStopSync = false;
            bool continuousSyncActive = false;

            lock (workerLock)
            {
                if (keepSyncThread != Thread.CurrentThread)
                    return;

                Volatile.Write(ref activeKeepSyncSessionId, 0);
                EndKeepSync();
                keepSyncStopRequestedEvent.Set();
                ResetRuntimeSyncCaches(runtime);
                shouldSendStopSync = true;
                ClearRuntimeFrame();
                keepSyncThread = null;
                continuousSyncActive = IsContinuousSyncing;
                notifyStop = true;
            }

            if (shouldSendStopSync)
                SendStopSync();
            if (notifyStop)
                runtime.Host.OnKeepSyncStopped(continuousSyncActive);
        }

        private bool TryProcessKeepSyncSample(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            bool firstSample,
            Func<bool> isOperationCurrent)
        {
            RecognizedSyncSample sample;
            if (!TryRecognizeSample(runtime, snapshot, false, out sample, isOperationCurrent))
                return false;
            RecognizedSampleProtocolDispatch dispatch;
            lock (workerLock)
            {
                if (!IsOperationCurrent(isOperationCurrent))
                    return false;

                dispatch = BuildRecognizedSampleProtocolDispatch(snapshot, sample, firstSample);
            }
            if (!IsOperationCurrent(isOperationCurrent))
                return false;
            DispatchRecognizedSampleProtocol(dispatch, isOperationCurrent);
            return true;
        }

        private bool TryPrimeSyncFrame(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            bool showMessages,
            Func<bool> isOperationCurrent)
        {
            if (!IsOperationCurrent(isOperationCurrent))
                return false;
            if (!EnsureSyncSourceSelected(runtime, snapshot, showMessages))
                return false;

            RecognizedSyncSample sample;
            if (!TryRecognizeSample(runtime, snapshot, true, out sample, isOperationCurrent))
            {
                if (showMessages && IsOperationCurrent(isOperationCurrent))
                    runtime.Host.ShowRecognitionFailureMessage();
                return false;
            }
            if (sample.Snapshot == null || !sample.Snapshot.IsValid)
            {
                if (showMessages && IsOperationCurrent(isOperationCurrent))
                    runtime.Host.ShowRecognitionFailureMessage();
                return false;
            }
            if (!IsOperationCurrent(isOperationCurrent))
                return false;
            ResolvePendingMove(sample.Snapshot, snapshot.BoardWidth);
            return true;
        }

        private bool EnsureSyncSourceSelected(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            bool showMessages)
        {
            if (snapshot.SyncMode == SyncMode.Foreground)
                return EnsureSelectionBounds(snapshot, showMessages, runtime.Host);

            WindowDescriptor descriptor;
            IntPtr handle = ResolveSelectedWindowHandle(snapshot);
            if (handle == IntPtr.Zero || !runtime.WindowDescriptorFactory.TryCreate(handle, out descriptor))
            {
                if (showMessages)
                    runtime.Host.ShowMissingSyncSourceMessage();
                return false;
            }

            if (snapshot.SyncMode != SyncMode.Background)
                return true;
            return EnsureSelectionBounds(snapshot, showMessages, runtime.Host);
        }

        private static bool EnsureSelectionBounds(
            SyncCoordinatorHostSnapshot snapshot,
            bool showMessages,
            ISyncCoordinatorHost host)
        {
            PixelRect selection = snapshot.SelectionBounds;
            if (selection != null && selection.Width > 0 && selection.Height > 0)
                return true;
            if (showMessages)
                host.ShowMissingSyncSourceMessage();
            return false;
        }

        private bool TryRecognizeSample(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            bool allowBlackRetry,
            out RecognizedSyncSample sample)
        {
            return TryRecognizeSample(runtime, snapshot, allowBlackRetry, out sample, null);
        }

        private bool TryRecognizeSample(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            bool allowBlackRetry,
            out RecognizedSyncSample sample,
            Func<bool> isOperationCurrent)
        {
            sample = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!IsOperationCurrent(isOperationCurrent))
                    return false;

                BoardFrame frame = CaptureFrame(runtime, snapshot, isOperationCurrent);
                if (frame == null)
                    return false;

                BoardRecognitionResult recognition = RecognizeFrame(runtime, snapshot, frame, isOperationCurrent);
                if (recognition == null)
                    return false;

                lock (workerLock)
                {
                    if (!IsOperationCurrent(isOperationCurrent))
                    {
                        DisposeBoardFrame(frame);
                        return false;
                    }

                    sample = CompleteRecognizedSample(runtime, snapshot, frame, recognition);
                }
                if (!NeedsBlackRetry(sample, allowBlackRetry))
                    return true;
            }
            return false;
        }

        private BoardFrame CaptureFrame(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            Func<bool> isOperationCurrent)
        {
            BoardCaptureResult captureResult = runtime.CaptureService.Capture(CreateCaptureRequest(runtime, snapshot));
            if (!IsOperationCurrent(isOperationCurrent))
            {
                if (captureResult != null && captureResult.Frame != null)
                    DisposeBoardFrame(captureResult.Frame);
                return null;
            }
            if (captureResult == null || !captureResult.Success || captureResult.Frame == null)
            {
                string failureReason = captureResult == null ? "Capture returned no result." : captureResult.FailureReason;
                RecordCaptureFailure(runtime, snapshot, captureResult, failureReason);
                SendError(failureReason);
                return null;
            }
            BoardFrame frame = NormalizeCapturedFrame(captureResult.Frame, snapshot);
            if (frame != null)
                frame.CapturePath = captureResult.CapturePath;
            return frame;
        }

        private BoardRecognitionResult RecognizeFrame(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            BoardFrame frame,
            Func<bool> isOperationCurrent)
        {
            BoardRecognitionResult recognition = runtime.RecognitionService.Recognize(new BoardRecognitionRequest { Frame = frame });
            if (!IsOperationCurrent(isOperationCurrent))
            {
                DisposeBoardFrame(frame);
                return null;
            }
            if (recognition == null || !recognition.Success || recognition.Snapshot == null)
            {
                string failureReason = recognition == null ? "Recognition returned no result." : recognition.FailureReason;
                RecordRecognitionFailure(runtime, snapshot, frame, failureReason);
                SendError(failureReason);
                DisposeBoardFrame(frame);
                return null;
            }
            return recognition;
        }

        private RecognizedSyncSample CompleteRecognizedSample(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            BoardFrame frame,
            BoardRecognitionResult recognition)
        {
            int previousArea = runtimeState.CurrentBoardPixelWidth * runtimeState.CurrentBoardPixelHeight;
            ApplyRecognizedFrame(frame, recognition);
            RecordRecognitionSuccess(runtime, snapshot, frame, recognition.Snapshot);
            UpdateBoardGeometry(frame, snapshot);
            ReplaceRuntimeFrame(frame);
            runtimeState.InitialProbePending = false;
            return new RecognizedSyncSample(previousArea, frame, recognition.Snapshot);
        }

        private static void RecordCaptureFailure(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            BoardCaptureResult captureResult,
            string failureReason)
        {
            if (runtime == null || runtime.DebugDiagnostics == null)
                return;

            runtime.DebugDiagnostics.RecordCaptureFailure(CreateDebugDiagnosticRecord(
                snapshot,
                captureResult == null ? null : captureResult.Frame,
                null,
                captureResult == null ? CapturePathKind.Unknown : captureResult.CapturePath,
                failureReason));
        }

        private static void RecordRecognitionFailure(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            BoardFrame frame,
            string failureReason)
        {
            if (runtime == null || runtime.DebugDiagnostics == null)
                return;

            runtime.DebugDiagnostics.RecordRecognitionFailure(CreateDebugDiagnosticRecord(
                snapshot,
                frame,
                null,
                ResolveCapturePath(frame),
                failureReason));
        }

        private static void RecordRecognitionSuccess(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            BoardFrame frame,
            BoardSnapshot boardSnapshot)
        {
            if (runtime == null || runtime.DebugDiagnostics == null)
                return;

            runtime.DebugDiagnostics.RecordRecognitionSuccess(CreateDebugDiagnosticRecord(
                snapshot,
                frame,
                boardSnapshot,
                ResolveCapturePath(frame),
                null));
        }

        private static BoardDebugDiagnosticRecord CreateDebugDiagnosticRecord(
            SyncCoordinatorHostSnapshot snapshot,
            BoardFrame frame,
            BoardSnapshot boardSnapshot,
            CapturePathKind capturePath,
            string failureReason)
        {
            return new BoardDebugDiagnosticRecord
            {
                SyncMode = snapshot == null ? SyncMode.Foreground : snapshot.SyncMode,
                BoardWidth = ResolveDebugBoardWidth(snapshot, frame),
                BoardHeight = ResolveDebugBoardHeight(snapshot, frame),
                CapturePath = capturePath,
                Frame = frame,
                Snapshot = boardSnapshot,
                FailureReason = failureReason
            };
        }

        private static CapturePathKind ResolveCapturePath(BoardFrame frame)
        {
            return frame == null ? CapturePathKind.Unknown : frame.CapturePath;
        }

        private static int ResolveDebugBoardWidth(SyncCoordinatorHostSnapshot snapshot, BoardFrame frame)
        {
            if (snapshot != null && snapshot.BoardWidth > 0)
                return snapshot.BoardWidth;
            if (frame != null && frame.BoardSize != null)
                return frame.BoardSize.Width;
            return 0;
        }

        private static int ResolveDebugBoardHeight(SyncCoordinatorHostSnapshot snapshot, BoardFrame frame)
        {
            if (snapshot != null && snapshot.BoardHeight > 0)
                return snapshot.BoardHeight;
            if (frame != null && frame.BoardSize != null)
                return frame.BoardSize.Height;
            return 0;
        }

        private bool NeedsBlackRetry(RecognizedSyncSample sample, bool allowBlackRetry)
        {
            if (!allowBlackRetry || sample.Snapshot == null || !sample.Snapshot.IsAllBlack)
                return false;
            runtimeState.PreferPrintWindow = false;
            if (runtimeState.RetriedAfterBlackFrame)
                return false;
            runtimeState.RetriedAfterBlackFrame = true;
            return true;
        }

        private BoardCaptureRequest CreateCaptureRequest(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot)
        {
            WindowDescriptor descriptor = null;
            if (snapshot.SyncMode != SyncMode.Foreground)
            {
                runtime.WindowDescriptorFactory.TryCreate(
                    ResolveSelectedWindowHandle(snapshot),
                    out descriptor);
            }
            return new BoardCaptureRequest
            {
                Window = descriptor,
                SyncMode = snapshot.SyncMode,
                BoardSize = new BoardDimensions(snapshot.BoardWidth, snapshot.BoardHeight),
                SelectionBounds = ShouldAttachSelectionBounds(snapshot.SyncMode) ? snapshot.SelectionBounds : null,
                PreferPrintWindow = runtimeState.PreferPrintWindow,
                UseEnhancedCapture = snapshot.UseEnhancedCapture && runtimeState.PreferPrintWindow,
                IsInitialProbe = runtimeState.InitialProbePending && runtimeState.PreferPrintWindow
            };
        }

        private static bool ShouldAttachSelectionBounds(SyncMode syncMode)
        {
            return syncMode == SyncMode.Background || syncMode == SyncMode.Foreground;
        }

        private static BoardFrame NormalizeCapturedFrame(BoardFrame frame, SyncCoordinatorHostSnapshot snapshot)
        {
            if (frame == null || frame.Viewport == null || snapshot.SyncMode != SyncMode.Foreground || frame.Image == null)
                return frame;
            frame.Viewport.SourceBounds = new PixelRect(0, 0, frame.Image.Width, frame.Image.Height);
            frame.Viewport.CellWidth = frame.Image.Width / (double)snapshot.BoardWidth;
            frame.Viewport.CellHeight = frame.Image.Height / (double)snapshot.BoardHeight;
            return frame;
        }

        private static void ApplyRecognizedFrame(BoardFrame frame, BoardRecognitionResult recognition)
        {
            if (frame == null || recognition == null || recognition.Viewport == null)
                return;
            if (recognition.Viewport.ScreenBounds == null && frame.Viewport != null)
                recognition.Viewport.ScreenBounds = frame.Viewport.ScreenBounds;
            frame.Viewport = recognition.Viewport;
        }

        private void UpdateBoardGeometry(BoardFrame frame, SyncCoordinatorHostSnapshot snapshot)
        {
            PixelRect bounds = ResolveHostBounds(frame, snapshot.SyncMode);
            if (bounds == null)
                return;
            runtimeState.CurrentBoardPixelWidth = bounds.Width;
            runtimeState.CurrentBoardPixelHeight = bounds.Height;
        }

        private static PixelRect ResolveHostBounds(BoardFrame frame, SyncMode syncMode)
        {
            if (frame == null || frame.Viewport == null)
                return null;
            if (syncMode == SyncMode.Foreground)
                return frame.Viewport.ScreenBounds ?? frame.Viewport.SourceBounds;
            return frame.Viewport.SourceBounds ?? frame.Viewport.ScreenBounds;
        }

        private RecognizedSampleProtocolDispatch BuildRecognizedSampleProtocolDispatch(
            SyncCoordinatorHostSnapshot snapshot,
            RecognizedSyncSample sample,
            bool firstSample)
        {
            RecognizedSampleProtocolDispatch dispatch = new RecognizedSampleProtocolDispatch();
            if (!firstSample && sample.PreviousArea > 0 && sample.PreviousArea != (runtimeState.CurrentBoardPixelWidth * runtimeState.CurrentBoardPixelHeight))
            {
                ResetSyncCaches();
                dispatch.ShouldSendClear = true;
            }
            dispatch.OverlayProtocolLine = ReserveOverlayProtocolLine(BuildOverlayProtocolLineIfNeeded(snapshot, sample.Frame));
            if (firstSample)
            {
                dispatch.StartMessage = protocolAdapter.CreateStartMessage(
                    snapshot.BoardWidth,
                    snapshot.BoardHeight,
                    ResolveSelectedWindowHandle(snapshot),
                    IncludeWindowHandle(snapshot.SyncMode));
            }
            ResolvePendingMove(sample.Snapshot, snapshot.BoardWidth);
            if (sample.Snapshot != null && sample.Snapshot.IsValid)
                dispatch.BoardSnapshotBatch = TryBuildOutboundBoardSnapshotBatch(sample.Snapshot);
            return dispatch;
        }

        private void DispatchRecognizedSampleProtocol(
            RecognizedSampleProtocolDispatch dispatch,
            Func<bool> isOperationCurrent)
        {
            if (dispatch == null)
                return;

            outboundProtocolDispatcher.ExecuteBatch(delegate
            {
                if (isOperationCurrent != null && !IsOperationCurrent(isOperationCurrent))
                    return;
                if (dispatch.ShouldSendClear)
                    outboundProtocolDispatcher.SendMessageWhileSynchronized(protocolAdapter.CreateClearMessage());
                if (!string.IsNullOrWhiteSpace(dispatch.OverlayProtocolLine))
                    outboundProtocolDispatcher.SendLegacyLineWhileSynchronized(dispatch.OverlayProtocolLine);
                if (dispatch.StartMessage != null)
                    outboundProtocolDispatcher.SendMessageWhileSynchronized(dispatch.StartMessage);
                if (dispatch.BoardSnapshotBatch != null)
                    outboundBoardSnapshotEmitter.EmitWhileSynchronized(dispatch.BoardSnapshotBatch);
            });
        }

        private string BuildOverlayProtocolLineIfNeeded(SyncCoordinatorHostSnapshot snapshot, BoardFrame frame)
        {
            if (!snapshot.ShowInBoard || snapshot.SyncMode == SyncMode.Foreground)
                return null;
            OverlayUpdateResult update = GetRuntimeDependencies().OverlayService.BuildUpdate(new OverlayUpdateRequest
            {
                Visibility = OverlayVisibility.Visible,
                Frame = frame,
                LegacyTypeToken = snapshot.LegacyTypeToken
            });
            return update == null || string.IsNullOrWhiteSpace(update.ProtocolLine)
                ? null
                : update.ProtocolLine;
        }

        private static bool IncludeWindowHandle(SyncMode syncMode)
        {
            return syncMode != SyncMode.Background && syncMode != SyncMode.Foreground;
        }

        private bool IsYikeSyncPlatform()
        {
            return string.Equals(
                NormalizeSyncPlatform(syncPlatform),
                ProtocolKeywords.Yike,
                StringComparison.Ordinal);
        }

        private void DispatchPendingMove(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            Func<bool> isOperationCurrent)
        {
            MoveRequest request;
            if (!TryTakePendingMove(out request))
                return;
            HandlePendingMovePlacementResult(PlacePendingMove(runtime, snapshot, request, isOperationCurrent));
        }

        private bool PlacePendingMove(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            MoveRequest request,
            Func<bool> isOperationCurrent)
        {
            if (runtimeState.CurrentBoardFrame == null || !IsOperationCurrent(isOperationCurrent))
                return false;

            BoardFrame placementFrame = ResolvePlacementFrame(snapshot);
            if (placementFrame == null)
                return false;

            MovePlacementResult result = runtime.PlacementService.Place(new MovePlacementRequest
            {
                Frame = placementFrame,
                Move = new MoveRequest { X = request.X, Y = request.Y, VerifyMove = request.VerifyMove },
                BringTargetToFront = snapshot.SyncMode == SyncMode.Foreground,
                ShouldCancel = delegate { return !IsOperationCurrent(isOperationCurrent); }
            });
            if (result != null && result.Success)
            {
                return true;
            }
            if (!IsOperationCurrent(isOperationCurrent))
                return false;
            runtime.Host.TrySendPlaceProtocolError(result == null ? "Move placement returned no result." : result.FailureReason);
            return false;
        }

        private BoardFrame ResolvePlacementFrame(SyncCoordinatorHostSnapshot snapshot)
        {
            BoardFrame frame = runtimeState.CurrentBoardFrame;
            if (snapshot == null || snapshot.SyncMode != SyncMode.Yike)
                return frame;

            YikeBoardGeometry geometry = runtimeState.LastCapturedYikeGeometry;
            if (geometry == null || !geometry.IsUsable)
                return frame;

            return CreateYikePlacementFrame(frame, geometry);
        }

        private BoardFrame CreateYikePlacementFrame(BoardFrame currentFrame, YikeBoardGeometry geometry)
        {
            if (geometry == null || !geometry.IsUsable)
                return currentFrame;

            WindowDescriptor window = currentFrame == null ? null : currentFrame.Window;
            if (window == null)
            {
                window = new WindowDescriptor
                {
                    Handle = runtimeState.SelectedWindowHandle,
                    ClassName = "SunAwtFrame",
                    IsDpiAware = true,
                    DpiScale = 1d,
                    IsJavaWindow = true
                };
            }
            else
            {
                window = CloneWindowDescriptor(window);
                window.Handle = window.Handle == IntPtr.Zero ? runtimeState.SelectedWindowHandle : window.Handle;
                window.IsJavaWindow = true;
            }

            return new BoardFrame
            {
                Window = window,
                SyncMode = SyncMode.Yike,
                BoardSize = new BoardDimensions(geometry.BoardSize, geometry.BoardSize),
                Viewport = CreateYikeViewport(geometry)
            };
        }

        private static BoardViewport CreateYikeViewport(YikeBoardGeometry geometry)
        {
            BoardViewport viewport = new BoardViewport
            {
                SourceBounds = CloneRect(geometry.Bounds)
            };

            if (geometry.FirstIntersectionX.HasValue
                && geometry.FirstIntersectionY.HasValue
                && geometry.CellWidth > 0d
                && geometry.CellHeight > 0d)
            {
                viewport.FirstIntersectionX = geometry.FirstIntersectionX.Value;
                viewport.FirstIntersectionY = geometry.FirstIntersectionY.Value;
                viewport.CellWidth = geometry.CellWidth;
                viewport.CellHeight = geometry.CellHeight;
                return viewport;
            }

            viewport.CellWidth = geometry.Bounds.Width / (double)geometry.BoardSize;
            viewport.CellHeight = geometry.Bounds.Height / (double)geometry.BoardSize;
            return viewport;
        }

        private static WindowDescriptor CloneWindowDescriptor(WindowDescriptor window)
        {
            if (window == null)
                return null;

            return new WindowDescriptor
            {
                Handle = window.Handle,
                ClassName = window.ClassName,
                Title = window.Title,
                Bounds = CloneRect(window.Bounds),
                IsDpiAware = window.IsDpiAware,
                DpiScale = window.DpiScale,
                IsJavaWindow = window.IsJavaWindow
            };
        }

        private static PixelRect CloneRect(PixelRect rect)
        {
            return rect == null ? null : new PixelRect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private Thread CreateContinuousSyncWorker(int lifecycleGeneration, int continuousSyncSessionId)
        {
            Thread worker = new Thread(new ThreadStart(delegate
            {
                RunContinuousSyncLoop(lifecycleGeneration, continuousSyncSessionId);
            }));
            worker.IsBackground = true;
            worker.Name = "ReadboardContinuousSyncWorker";
            worker.SetApartmentState(ApartmentState.STA);
            return worker;
        }

        private Thread CreateKeepSyncWorker(int lifecycleGeneration, int keepSyncSessionId)
        {
            Thread worker = new Thread(new ThreadStart(delegate
            {
                RunKeepSyncLoop(lifecycleGeneration, keepSyncSessionId);
            }));
            worker.IsBackground = true;
            worker.Name = "ReadboardKeepSyncWorker";
            worker.SetApartmentState(ApartmentState.STA);
            return worker;
        }

        private void CompleteStopCleanup()
        {
            Volatile.Write(ref activeKeepSyncSessionId, 0);
            ClearRuntimeFrame();
            runtimeState.ResetProbeState();
            if (runtimeDependencies != null)
                ResetRuntimeSyncCaches(runtimeDependencies);
        }

        private void CompleteStopCleanupIfIdle()
        {
            lock (workerLock)
            {
                if (keepSyncThread != null)
                    return;
            }

            CompleteStopCleanup();
        }

        private static void CompleteWorkerStop(Thread worker, bool waitForWorker)
        {
            if (worker == null || worker == Thread.CurrentThread)
                return;
            worker.Join(waitForWorker ? Timeout.Infinite : 0);
        }

        private int CaptureSyncLifecycleGeneration()
        {
            return Volatile.Read(ref syncLifecycleGeneration);
        }

        private bool IsSyncLifecycleCurrent(int lifecycleGeneration)
        {
            return lifecycleGeneration == Volatile.Read(ref syncLifecycleGeneration);
        }

        private bool IsKeepSyncStartCurrent(int lifecycleGeneration, bool requireContinuousSync)
        {
            if (!IsSyncLifecycleCurrent(lifecycleGeneration) || StartedSync)
                return false;
            return requireContinuousSync ? IsContinuousSyncing : !IsContinuousSyncing;
        }

        private bool IsContinuousSyncStartCurrent(int lifecycleGeneration)
        {
            return IsSyncLifecycleCurrent(lifecycleGeneration)
                && !StartedSync
                && !IsContinuousSyncing;
        }

        private bool IsContinuousSyncWorkerCurrent(int lifecycleGeneration, int continuousSyncSessionId)
        {
            return IsSyncLifecycleCurrent(lifecycleGeneration)
                && continuousSyncSessionId == Volatile.Read(ref activeContinuousSyncSessionId)
                && IsContinuousSyncing;
        }

        private bool IsKeepSyncWorkerCurrent(int lifecycleGeneration, int keepSyncSessionId)
        {
            return IsSyncLifecycleCurrent(lifecycleGeneration)
                && keepSyncSessionId == Volatile.Read(ref activeKeepSyncSessionId)
                && StartedSync
                && KeepSync;
        }

        private static bool IsOperationCurrent(Func<bool> isOperationCurrent)
        {
            return isOperationCurrent == null || isOperationCurrent();
        }

        private bool WaitForNextSample(int sampleIntervalMs)
        {
            if (sampleIntervalMs <= 0)
            {
                Thread.Yield();
                return keepSyncStopRequestedEvent.Wait(0);
            }
            return keepSyncStopRequestedEvent.Wait(sampleIntervalMs);
        }

        private void SendForegroundFoxState(SyncCoordinatorHostSnapshot snapshot)
        {
            SendForegroundFoxInBoard(snapshot.ShowInBoard && snapshot.SupportsForegroundFoxInBoardProtocol);
            SendPlayIfSelected(snapshot);
        }

        private void SendPlayIfSelected(SyncCoordinatorHostSnapshot snapshot)
        {
            if (!SyncBoth || string.IsNullOrWhiteSpace(snapshot.PlayColor))
                return;
            SendPlay(
                snapshot.PlayColor,
                NormalizeNumericValue(snapshot.AiTimeValue),
                NormalizeNumericValue(snapshot.PlayoutsValue),
                NormalizeNumericValue(snapshot.FirstPolicyValue));
        }

        private static string NormalizeNumericValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "0" : value;
        }

        private void ResetRuntimeSyncCaches(SyncSessionRuntimeDependencies runtime)
        {
            ResetSyncCaches(false);
            runtime.OverlayService.Reset();
            runtime.Host.OnSyncCachesReset();
        }

        private SyncCoordinatorHostSnapshot CaptureSnapshot(SyncSessionRuntimeDependencies runtime)
        {
            SyncCoordinatorHostSnapshot snapshot = runtime.Host.CaptureSnapshot();
            if (snapshot == null)
                throw new InvalidOperationException("Sync coordinator host returned no snapshot.");
            return snapshot;
        }

        private bool TryCaptureSnapshot(SyncSessionRuntimeDependencies runtime, out SyncCoordinatorHostSnapshot snapshot)
        {
            try
            {
                snapshot = CaptureSnapshot(runtime);
                return true;
            }
            catch (SnapshotCaptureCancelledException)
            {
                snapshot = null;
                return false;
            }
        }

        private SyncSessionRuntimeDependencies GetRuntimeDependencies()
        {
            if (runtimeDependencies == null)
                throw new InvalidOperationException("Sync runtime has not been attached.");
            return runtimeDependencies;
        }

        private IntPtr ResolveSelectedWindowHandle(SyncCoordinatorHostSnapshot snapshot)
        {
            if (runtimeState.SelectedWindowHandle != IntPtr.Zero)
                return runtimeState.SelectedWindowHandle;
            return snapshot.SelectedWindowHandle;
        }

        private void ReplaceRuntimeFrame(BoardFrame frame)
        {
            BoardFrame previous = runtimeState.CurrentBoardFrame;
            runtimeState.CurrentBoardFrame = frame;
            DisposeBoardFrame(previous);
        }

        private void ClearRuntimeFrame()
        {
            ReplaceRuntimeFrame(null);
            runtimeState.CurrentBoardPixelWidth = 0;
            runtimeState.CurrentBoardPixelHeight = 0;
        }

        private static void DisposeBoardFrame(BoardFrame frame)
        {
            if (frame == null || frame.Image == null)
                return;
            frame.Image.Dispose();
            frame.Image = null;
        }

        private sealed class RecognizedSyncSample
        {
            public RecognizedSyncSample(int previousArea, BoardFrame frame, BoardSnapshot snapshot)
            {
                PreviousArea = previousArea;
                Frame = frame;
                Snapshot = snapshot;
            }

            public int PreviousArea { get; private set; }
            public BoardFrame Frame { get; private set; }
            public BoardSnapshot Snapshot { get; private set; }
        }

        private sealed class RecognizedSampleProtocolDispatch
        {
            public bool ShouldSendClear { get; set; }
            public string OverlayProtocolLine { get; set; }
            public ProtocolMessage StartMessage { get; set; }
            public OutboundBoardSnapshotBatch BoardSnapshotBatch { get; set; }
        }
    }
}
