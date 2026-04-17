using System;
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

            ProcessRecognizedSample(snapshot, sample, true);
            return true;
        }

        public bool TryStartKeepSync()
        {
            SyncSessionRuntimeDependencies runtime = GetRuntimeDependencies();
            if (StartedSync || IsContinuousSyncing)
                return false;

            SyncCoordinatorHostSnapshot snapshot;
            if (!TryCaptureSnapshot(runtime, out snapshot))
                return false;
            runtimeState.SelectedWindowHandle = snapshot.SelectedWindowHandle;
            return TryStartKeepSyncSession(runtime, snapshot, true);
        }

        public bool TryStartContinuousSync()
        {
            SyncSessionRuntimeDependencies runtime = GetRuntimeDependencies();
            if (StartedSync || IsContinuousSyncing)
                return false;

            BeginContinuousSync();
            runtime.Host.OnContinuousSyncStarted();
            StartContinuousSyncWorker();
            if (CaptureSnapshot(runtime).AutoMinimize)
                runtime.Host.MinimizeWindow();
            return true;
        }

        public void StopSyncSession()
        {
            StopSyncSessionCore(true);
        }

        private void StopSyncSessionCore(bool waitForWorkers)
        {
            EndContinuousSync();
            EndKeepSync();
            keepSyncStopRequestedEvent.Set();
            CompleteWorkerStop(keepSyncThread, waitForWorkers);
            CompleteWorkerStop(continuousSyncThread, waitForWorkers);
            if (waitForWorkers)
                CompleteStopCleanup();
        }

        public void HandlePlaceRequest(MoveRequest request)
        {
            if (request == null)
                return;

            SyncCoordinatorHostSnapshot snapshot;
            if (!TryCaptureSnapshot(GetRuntimeDependencies(), out snapshot))
                return;
            int boardWidth = snapshot.BoardWidth;
            if (!TryQueuePendingMove(request, runtimeState.CurrentBoardPixelWidth, boardWidth))
                return;
            SendPlacementResult(WaitForPendingMoveResult());
        }

        private void RunContinuousSyncLoop()
        {
            SyncSessionRuntimeDependencies runtime = GetRuntimeDependencies();
            try
            {
                while (IsContinuousSyncing)
                {
                    if (!WaitForSyncIdle(ContinuousSyncPollIntervalMs))
                        continue;

                    SyncCoordinatorHostSnapshot snapshot;
                    if (!TryCaptureSnapshot(runtime, out snapshot))
                        return;
                    if (snapshot.ShowInBoard)
                        SendNoInBoard();
                    TryStartDiscoveredKeepSync(runtime, snapshot);
                    if (WaitForContinuousSyncStop(ContinuousSyncPollIntervalMs))
                        return;
                }
            }
            finally
            {
                lock (workerLock)
                {
                    continuousSyncThread = null;
                }
                runtime.Host.OnContinuousSyncStopped();
            }
        }

        private void TryStartDiscoveredKeepSync(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot)
        {
            IntPtr handle = runtime.WindowLocator.FindWindowHandle(snapshot.SyncMode);
            if (handle == IntPtr.Zero || !IsContinuousSyncing)
                return;

            runtimeState.SelectedWindowHandle = handle;
            runtime.Host.UpdateSelectedWindowHandle(handle);
            SyncCoordinatorHostSnapshot refreshedSnapshot;
            if (!TryCaptureSnapshot(runtime, out refreshedSnapshot))
                return;
            TryStartKeepSyncSession(runtime, refreshedSnapshot, false);
        }

        private bool TryStartKeepSyncSession(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            bool showMessages)
        {
            runtimeState.ResetProbeState();
            SendForegroundFoxState(snapshot);
            if (!TryPrimeSyncFrame(runtime, snapshot, showMessages))
            {
                HandleKeepSyncStartFailure(runtime, showMessages);
                return false;
            }

            BeginKeepSync();
            runtime.Host.OnKeepSyncStarted();
            ResetRuntimeSyncCaches(runtime);
            keepSyncStopRequestedEvent.Reset();
            StartKeepSyncWorker();
            if (!IsContinuousSyncing && snapshot.AutoMinimize)
                runtime.Host.MinimizeWindow();
            return true;
        }

        private void HandleKeepSyncStartFailure(
            SyncSessionRuntimeDependencies runtime,
            bool restoreUi)
        {
            ResetRuntimeSyncCaches(runtime);
            ClearRuntimeFrame();
            runtimeState.ResetProbeState();
            CancelPendingMove();
            if (runtimeState.ReleasePlacementBindingRequired)
                ReleasePlacementBinding(runtime.Host);
            if (restoreUi)
                runtime.Host.OnKeepSyncStopped(false);
        }

        private void RunKeepSyncLoop()
        {
            bool firstSample = true;
            SyncSessionRuntimeDependencies runtime = GetRuntimeDependencies();
            try
            {
                SendSync();
                while (KeepSync)
                {
                    SyncCoordinatorHostSnapshot snapshot;
                    if (!TryCaptureSnapshot(runtime, out snapshot))
                        return;
                    runtimeState.SelectedWindowHandle = ResolveSelectedWindowHandle(snapshot);
                    if (!EnsureSyncSourceSelected(runtime, snapshot, false))
                        return;
                    DispatchPendingMove(runtime, snapshot);
                    if (!TryProcessKeepSyncSample(runtime, snapshot, firstSample) && firstSample)
                        return;
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
            EndKeepSync();
            keepSyncStopRequestedEvent.Set();
            ResetRuntimeSyncCaches(runtime);
            SendStopSync();
            ClearRuntimeFrame();
            ReleasePlacementBinding(runtime.Host);
            lock (workerLock)
            {
                keepSyncThread = null;
            }
            runtime.Host.OnKeepSyncStopped(IsContinuousSyncing);
        }

        private bool TryProcessKeepSyncSample(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            bool firstSample)
        {
            RecognizedSyncSample sample;
            if (!TryRecognizeSample(runtime, snapshot, false, out sample))
                return false;
            ProcessRecognizedSample(snapshot, sample, firstSample);
            return true;
        }

        private bool TryPrimeSyncFrame(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            bool showMessages)
        {
            if (!EnsureSyncSourceSelected(runtime, snapshot, showMessages))
                return false;

            RecognizedSyncSample sample;
            if (!TryRecognizeSample(runtime, snapshot, true, out sample))
            {
                if (showMessages)
                    runtime.Host.ShowRecognitionFailureMessage();
                return false;
            }
            if (sample.Snapshot == null || !sample.Snapshot.IsValid)
            {
                if (showMessages)
                    runtime.Host.ShowRecognitionFailureMessage();
                return false;
            }
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
            if (handle == IntPtr.Zero || !runtime.WindowDescriptorFactory.TryCreate(handle, snapshot.DpiScale, out descriptor))
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
            sample = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                BoardFrame frame = CaptureFrame(runtime, snapshot);
                if (frame == null)
                    return false;

                BoardRecognitionResult recognition = RecognizeFrame(runtime, frame);
                if (recognition == null)
                    return false;

                sample = CompleteRecognizedSample(snapshot, frame, recognition);
                if (!NeedsBlackRetry(sample, allowBlackRetry))
                    return true;
            }
            return false;
        }

        private BoardFrame CaptureFrame(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot)
        {
            BoardCaptureResult captureResult = runtime.CaptureService.Capture(CreateCaptureRequest(runtime, snapshot));
            if (captureResult == null || !captureResult.Success || captureResult.Frame == null)
            {
                SendError(captureResult == null ? "Capture returned no result." : captureResult.FailureReason);
                return null;
            }
            return NormalizeCapturedFrame(captureResult.Frame, snapshot);
        }

        private BoardRecognitionResult RecognizeFrame(
            SyncSessionRuntimeDependencies runtime,
            BoardFrame frame)
        {
            BoardRecognitionResult recognition = runtime.RecognitionService.Recognize(new BoardRecognitionRequest { Frame = frame });
            if (recognition == null || !recognition.Success || recognition.Snapshot == null)
            {
                SendError(recognition == null ? "Recognition returned no result." : recognition.FailureReason);
                DisposeBoardFrame(frame);
                return null;
            }
            return recognition;
        }

        private RecognizedSyncSample CompleteRecognizedSample(
            SyncCoordinatorHostSnapshot snapshot,
            BoardFrame frame,
            BoardRecognitionResult recognition)
        {
            int previousArea = runtimeState.CurrentBoardPixelWidth * runtimeState.CurrentBoardPixelHeight;
            ApplyRecognizedFrame(frame, recognition);
            UpdateBoardGeometry(frame, snapshot);
            ReplaceRuntimeFrame(frame);
            runtimeState.InitialProbePending = false;
            return new RecognizedSyncSample(previousArea, frame, recognition.Snapshot);
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
                    snapshot.DpiScale,
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

        private void ProcessRecognizedSample(
            SyncCoordinatorHostSnapshot snapshot,
            RecognizedSyncSample sample,
            bool firstSample)
        {
            if (!firstSample && sample.PreviousArea > 0 && sample.PreviousArea != (runtimeState.CurrentBoardPixelWidth * runtimeState.CurrentBoardPixelHeight))
                SendClear();
            SendOverlayIfNeeded(snapshot, sample.Frame);
            if (firstSample)
                SendStart(snapshot.BoardWidth, snapshot.BoardHeight, ResolveSelectedWindowHandle(snapshot), IncludeWindowHandle(snapshot.SyncMode));
            ResolvePendingMove(sample.Snapshot, snapshot.BoardWidth);
            if (sample.Snapshot != null && sample.Snapshot.IsValid)
                SendBoardSnapshot(sample.Snapshot);
        }

        private void SendOverlayIfNeeded(SyncCoordinatorHostSnapshot snapshot, BoardFrame frame)
        {
            if (!snapshot.ShowInBoard || snapshot.SyncMode == SyncMode.Foreground)
                return;
            OverlayUpdateResult update = GetRuntimeDependencies().OverlayService.BuildUpdate(new OverlayUpdateRequest
            {
                Visibility = OverlayVisibility.Visible,
                Frame = frame,
                LegacyTypeToken = snapshot.LegacyTypeToken
            });
            if (update != null && !string.IsNullOrWhiteSpace(update.ProtocolLine))
                SendOverlayLine(update.ProtocolLine);
        }

        private static bool IncludeWindowHandle(SyncMode syncMode)
        {
            return syncMode != SyncMode.Background && syncMode != SyncMode.Foreground;
        }

        private void DispatchPendingMove(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot)
        {
            MoveRequest request;
            if (!TryTakePendingMove(out request))
                return;
            HandlePendingMovePlacementResult(PlacePendingMove(runtime, snapshot, request));
        }

        private bool PlacePendingMove(
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            MoveRequest request)
        {
            if (runtimeState.CurrentBoardFrame == null)
                return false;

            MovePlacementResult result = runtime.PlacementService.Place(new MovePlacementRequest
            {
                Frame = runtimeState.CurrentBoardFrame,
                Move = new MoveRequest { X = request.X, Y = request.Y, VerifyMove = request.VerifyMove },
                UseLightweightInterop = snapshot.CanUseLightweightInterop,
                BringTargetToFront = snapshot.SyncMode == SyncMode.Foreground
            });
            if (result != null && result.Success)
            {
                runtimeState.ReleasePlacementBindingRequired = result.PlacementPath == PlacementPathKind.LightweightInterop;
                return true;
            }
            SendError(result == null ? "Move placement returned no result." : result.FailureReason);
            return false;
        }

        private void StartContinuousSyncWorker()
        {
            Thread worker = new Thread(RunContinuousSyncLoop);
            worker.IsBackground = true;
            worker.Name = "ReadboardContinuousSyncWorker";
            worker.SetApartmentState(ApartmentState.STA);
            lock (workerLock)
            {
                continuousSyncThread = worker;
            }
            worker.Start();
        }

        private void StartKeepSyncWorker()
        {
            Thread worker = new Thread(RunKeepSyncLoop);
            worker.IsBackground = true;
            worker.Name = "ReadboardKeepSyncWorker";
            worker.SetApartmentState(ApartmentState.STA);
            lock (workerLock)
            {
                keepSyncThread = worker;
            }
            worker.Start();
        }

        private void CompleteStopCleanup()
        {
            ClearRuntimeFrame();
            runtimeState.ResetProbeState();
            if (runtimeDependencies != null)
                ResetRuntimeSyncCaches(runtimeDependencies);
            if (runtimeDependencies != null)
                ReleasePlacementBinding(runtimeDependencies.Host);
        }

        private static void CompleteWorkerStop(Thread worker, bool waitForWorker)
        {
            if (worker == null || worker == Thread.CurrentThread)
                return;
            worker.Join(waitForWorker ? Timeout.Infinite : 0);
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
            ResetSyncCaches();
            runtime.OverlayService.Reset();
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

        private void ReleasePlacementBinding(ISyncCoordinatorHost host)
        {
            if (!runtimeState.ReleasePlacementBindingRequired || host == null)
                return;
            runtimeState.ReleasePlacementBindingRequired = false;
            host.ReleasePlacementBinding(runtimeState.SelectedWindowHandle);
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
    }
}
