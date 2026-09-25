using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Xunit;
using readboard;
using Readboard.VerificationTests.Support;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class StableSnapshotSamplingTests
    {
        [Fact]
        public void TraceExport_GenericResume_EmitsExactlyTwoTargetBatchesAndExportsTrace()
        {
            RunTraceExportTest(
                "generic-resume.txt",
                SyncMode.Foreground,
                "generic",
                null,
                IntPtr.Zero,
                null,
                CreateGenericTargetBoard());
        }

        [Fact]
        public void TraceExport_FoxKnown_EmitsExactlyTwoTargetBatchesAndExportsTrace()
        {
            RunTraceExportTest(
                "fox-known.txt",
                SyncMode.Fox,
                "fox",
                new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    LiveRoomState = FoxLiveRoomState.Playing,
                    RoomToken = "123",
                    LiveTitleMove = 3
                },
                new IntPtr(5151),
                3,
                CreateFoxTargetBoard());
        }

        [Fact]
        public void TraceExport_FoxUnknown_EmitsExactlyTwoTargetBatchesAndExportsTrace()
        {
            RunTraceExportTest(
                "fox-unknown.txt",
                SyncMode.Fox,
                "fox",
                FoxWindowContext.Unknown(),
                new IntPtr(5151),
                null,
                CreateFoxTargetBoard());
        }

        private void RunTraceExportTest(
            string fileName,
            SyncMode syncMode,
            string platform,
            FoxWindowContext foxContext,
            IntPtr handle,
            int? foxMoveNumber,
            BoardCellState[] targetBoard)
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform(platform);
            if (foxContext != null)
                coordinator.SetFoxWindowContext(foxContext);

            SyncCoordinatorHostSnapshot snapshot = CreateSnapshot(syncMode, handle, foxMoveNumber);
            TestSyncCoordinatorHost host = new TestSyncCoordinatorHost(snapshot);

            BoardRecognitionResult result = Create3x3Result(targetBoard, LastMoveSource.None, foxMoveNumber);

            DeterministicSampleController controller = new DeterministicSampleController();
            controller.ArmGate(2); // Pause worker at sample 2
            controller.ArmGate(4); // Pause worker at sample 4 to prove sample 3 was silent
            controller.RecognitionFactory = _ => result;

            SyncSessionRuntimeDependencies runtime = CreateRuntime(host, controller, controller);
            coordinator.AttachRuntime(runtime);

            Assert.True(coordinator.TryStartKeepSync());
            try
            {
                transport.WaitForBatchCount(1);
                controller.WaitForStepStarted(2);

                // Same-sample replay via public SendBoardSnapshot during first-sample pause stays one.
                coordinator.SendBoardSnapshot(result.Snapshot);
                coordinator.SendBoardSnapshot(result.Snapshot);
                Assert.Equal(1, transport.CountLines("end"));

                // Second real worker sample provides confirmation.
                controller.ReleaseStep(2);
                transport.WaitForBatchCount(2);
                Assert.Equal(2, transport.CountLines("end"));

                // Third worker sample is silent; gate at sample 4 proves sample 3 dispatch completed.
                controller.WaitForStepStarted(4);
                Assert.Equal(2, transport.CountLines("end"));
            }
            finally
            {
                coordinator.StopSyncSession();
                controller.ReleaseAll();
                VerificationCompletion.Wait(host.KeepStopped, "Keep sync did not stop.");
            }

            Assert.Equal(2, transport.CountLines("end"));
            MaybeExportTrace(fileName, transport);
        }

        [Fact]
        public void TraceExport_ConflictsRequireConsecutiveIndependentObservations()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("generic");
            TestSyncCoordinatorHost host = new TestSyncCoordinatorHost(CreateSnapshot(SyncMode.Foreground, IntPtr.Zero));
            BoardRecognitionResult conflictA = Create3x3Result(CreateGenericTargetBoard());
            BoardRecognitionResult conflictB = Create3x3Result(CreateFoxTargetBoard());
            BoardCellState[] baseline = new BoardCellState[9];
            baseline[0] = BoardCellState.Black;
            baseline[1] = BoardCellState.White;
            BoardRecognitionResult correct = Create3x3Result(baseline);
            DeterministicSampleController controller = new DeterministicSampleController();
            controller.RecognitionFactory = step => step == 2 ? correct : step == 4 ? conflictB : conflictA;
            controller.ArmGate(2);
            controller.ArmGate(8);
            coordinator.AttachRuntime(CreateRuntime(host, controller, controller));

            Assert.True(coordinator.TryStartKeepSync());
            try
            {
                controller.WaitForStepStarted(2);
                Assert.Equal(1, transport.CountLines("end"));
                coordinator.SendBoardSnapshot(conflictA.Snapshot);
                coordinator.SendBoardSnapshot(conflictA.Snapshot);
                Assert.Equal(1, transport.CountLines("end"));
                controller.ReleaseStep(2);
                controller.WaitForStepStarted(8);
                // A, correct, A, B, A, A; the seventh independent A is silent.
                Assert.Equal(6, transport.CountLines("end"));
            }
            finally
            {
                coordinator.StopSyncSession();
                controller.ReleaseAll();
                VerificationCompletion.Wait(host.KeepStopped, "Keep sync did not stop.");
            }

            MaybeExportTrace("conflict-safety.txt", transport);
        }

        [Fact]
        public void KeepSync_ResultSegmentTransition_ABA_ResetsConfirmationForEachSegment()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            SyncCoordinatorHostSnapshot snapshot = CreateSnapshot(SyncMode.Foreground, IntPtr.Zero);
            TestSyncCoordinatorHost host = new TestSyncCoordinatorHost(snapshot);

            BoardRecognitionResult resultA = Create3x3Result(CreateGenericTargetBoard());
            BoardRecognitionResult resultB = Create3x3Result(CreateAlternateTargetBoard());

            DeterministicSampleController controller = new DeterministicSampleController();
            controller.RecognitionFactory = step =>
            {
                if (step <= 3) return resultA;
                if (step <= 6) return resultB;
                return resultA;
            };
            // Step 0: Prime (resultA)
            // Step 1: Worker 1 (resultA) -> Batch 1 (A1)
            // Step 2: Worker 2 (resultA) -> Batch 2 (A2 - confirmed)
            // Step 3: Worker 3 (resultA) -> Silent
            // Step 4: Worker 4 (resultB) -> Batch 3 (B1)
            // Step 5: Worker 5 (resultB) -> Batch 4 (B2 - confirmed)
            // Step 6: Worker 6 (resultB) -> Silent
            // Step 7: Worker 7 (resultA) -> Batch 5 (A3 - new segment!)
            // Step 8: Worker 8 (resultA) -> Batch 6 (A4 - confirmed!)
            // Step 9: Worker 9 (resultA) -> Silent
            // Gate at step 10 to prove sample 9 dispatch completed.
            controller.ArmGate(10);

            SyncSessionRuntimeDependencies runtime = CreateRuntime(host, controller, controller);
            coordinator.AttachRuntime(runtime);

            Assert.True(coordinator.TryStartKeepSync());
            try
            {
                controller.WaitForStepStarted(10);
                Assert.Equal(6, transport.CountLines("end"));
            }
            finally
            {
                coordinator.StopSyncSession();
                controller.ReleaseAll();
                VerificationCompletion.Wait(host.KeepStopped, "Keep sync did not stop.");
            }

            Assert.Equal(6, transport.CountLines("end"));
        }

        [Fact]
        public void KeepSync_MarkerAndSourceChanges_AreVisibleAndNotDeduplicatedAsIdentical()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            SyncCoordinatorHostSnapshot snapshot = CreateSnapshot(SyncMode.Foreground, IntPtr.Zero);
            TestSyncCoordinatorHost host = new TestSyncCoordinatorHost(snapshot);

            // Plain stone, no marker, source None
            BoardCellState[] boardNone = CreateGenericTargetBoard();
            BoardRecognitionResult resultNone = Create3x3Result(boardNone, LastMoveSource.None);

            // Marker presence change: BlackLastMove at (0,0), source RedBlueMarker
            BoardCellState[] boardBlackMarker = CreateGenericTargetBoard();
            boardBlackMarker[0] = BoardCellState.BlackLastMove;
            BoardRecognitionResult resultBlackMarker = Create3x3Result(boardBlackMarker, LastMoveSource.RedBlueMarker);

            // Marker color change: WhiteLastMove at (1,0), source Deviation
            BoardCellState[] boardWhiteMarker = CreateGenericTargetBoard();
            boardWhiteMarker[1] = BoardCellState.WhiteLastMove;
            BoardRecognitionResult resultWhiteMarker = Create3x3Result(boardWhiteMarker, LastMoveSource.Deviation);

            DeterministicSampleController controller = new DeterministicSampleController();
            controller.RecognitionFactory = step =>
            {
                if (step < 2) return resultNone;
                if (step == 2) return resultBlackMarker;
                return resultWhiteMarker;
            };
            // Step 1: resultNone -> Batch 1 (lastMoveSource none)
            // Step 2: resultBlackMarker -> Batch 2 (marker presence, lastMoveSource redBlueMarker)
            // Step 3: resultWhiteMarker -> Batch 3 (marker color, lastMoveSource deviation)
            // Step 4: resultWhiteMarker -> Batch 4 (confirmed deviation batch)
            // Step 5: resultWhiteMarker -> Silent
            // Gate at step 6 to prove sample 5 completed.
            controller.ArmGate(6);

            SyncSessionRuntimeDependencies runtime = CreateRuntime(host, controller, controller);
            coordinator.AttachRuntime(runtime);

            Assert.True(coordinator.TryStartKeepSync());
            try
            {
                controller.WaitForStepStarted(6);
                Assert.Equal(4, transport.CountLines("end"));
                Assert.Equal(1, transport.CountLines("lastMoveSource none"));
                Assert.Equal(1, transport.CountLines("lastMoveSource redBlueMarker"));
                Assert.Equal(2, transport.CountLines("lastMoveSource deviation"));
            }
            finally
            {
                coordinator.StopSyncSession();
                controller.ReleaseAll();
                VerificationCompletion.Wait(host.KeepStopped, "Keep sync did not stop.");
            }
        }

        [Fact]
        public void KeepSync_ForceRebuild_ConsumedOnceAndNotRepeatedInConfirmation()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            SyncCoordinatorHostSnapshot snapshot = CreateSnapshot(SyncMode.Foreground, IntPtr.Zero);
            TestSyncCoordinatorHost host = new TestSyncCoordinatorHost(snapshot);

            BoardRecognitionResult result = Create3x3Result(CreateGenericTargetBoard());

            DeterministicSampleController controller = new DeterministicSampleController();
            controller.ArmGate(2);
            controller.ArmGate(5);
            controller.RecognitionFactory = _ => result;

            SyncSessionRuntimeDependencies runtime = CreateRuntime(host, controller, controller);
            coordinator.AttachRuntime(runtime);

            Assert.True(coordinator.TryStartKeepSync());
            try
            {
                transport.WaitForBatchCount(1);
                controller.WaitForStepStarted(2);
                Assert.Equal(0, transport.CountLines("forceRebuild"));

                coordinator.ArmForceRebuild();

                controller.ReleaseStep(2);
                transport.WaitForBatchCount(2);
                Assert.Equal(1, transport.CountLines("forceRebuild"));

                // Confirmation frame of the same stable result does not repeat forceRebuild.
                transport.WaitForBatchCount(3);
                Assert.Equal(1, transport.CountLines("forceRebuild"));

                // Sample 4 is silent; gate at sample 5 proves sample 4 completed.
                controller.WaitForStepStarted(5);
                Assert.Equal(3, transport.CountLines("end"));
            }
            finally
            {
                coordinator.StopSyncSession();
                controller.ReleaseAll();
                VerificationCompletion.Wait(host.KeepStopped, "Keep sync did not stop.");
            }
        }

        [Fact]
        public void KeepSync_InvalidOrFailedRecognition_DoesNotConfirmOrEmit()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            SyncCoordinatorHostSnapshot snapshot = CreateSnapshot(SyncMode.Foreground, IntPtr.Zero);
            TestSyncCoordinatorHost host = new TestSyncCoordinatorHost(snapshot);

            BoardRecognitionResult validResult = Create3x3Result(CreateGenericTargetBoard());
            BoardRecognitionResult invalidResult = Create3x3Result(CreateGenericTargetBoard(), isValid: false);

            DeterministicSampleController controller = new DeterministicSampleController();
            controller.CaptureFactory = step => step == 3
                ? new BoardCaptureResult { Success = false, FailureReason = "Simulated capture failure" }
                : new BoardCaptureResult { Success = true, Frame = CreateFrame() };
            controller.RecognitionFactory = step => step == 2 ? invalidResult : validResult;
            controller.ArmGate(6);

            SyncSessionRuntimeDependencies runtime = CreateRuntime(host, controller, controller);
            coordinator.AttachRuntime(runtime);

            Assert.True(coordinator.TryStartKeepSync());
            try
            {
                controller.WaitForStepStarted(6);
                // Step 1: batch 1 (valid)
                // Step 2: invalid (no batch)
                // Step 3: capture failed (no batch)
                // Step 4: batch 2 (confirmation from fresh valid sample)
                // Step 5: silent (deduped)
                // Step 6: gated
                Assert.Equal(2, transport.CountLines("end"));
            }
            finally
            {
                coordinator.StopSyncSession();
                controller.ReleaseAll();
                VerificationCompletion.Wait(host.KeepStopped, "Keep sync did not stop.");
            }
        }

        [Fact]
        public void KeepSync_PlatformChangeWhileCaptureGated_DiscardsOldSampleAndPreservesNewConfirmation()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("generic");
            SyncCoordinatorHostSnapshot snapshot = CreateSnapshot(SyncMode.Foreground, IntPtr.Zero);
            TestSyncCoordinatorHost host = new TestSyncCoordinatorHost(snapshot);

            BoardRecognitionResult genericResult = Create3x3Result(CreateGenericTargetBoard());
            BoardRecognitionResult foxResult = Create3x3Result(CreateFoxTargetBoard());

            DeterministicSampleController controller = new DeterministicSampleController();
            controller.ArmGate(2);
            controller.ArmGate(6);
            controller.RecognitionFactory = step => step < 2 ? genericResult : foxResult;

            SyncSessionRuntimeDependencies runtime = CreateRuntime(host, controller, controller);
            coordinator.AttachRuntime(runtime);

            Assert.True(coordinator.TryStartKeepSync());
            try
            {
                transport.WaitForBatchCount(1);
                controller.WaitForStepStarted(2);

                // Change platform while worker is gated inside capture for step 2.
                coordinator.SetSyncPlatform("fox");
                coordinator.SetFoxWindowContext(FoxWindowContext.Unknown());

                // Release step 2: old sample had stale cache generation and must be discarded.
                controller.ReleaseStep(2);

                // Step 3 captures under new "fox" platform: emits first batch for fox.
                transport.WaitForBatchCount(2);

                // Step 4 provides fox confirmation batch.
                transport.WaitForBatchCount(3);

                // Step 5 is silent; gate at step 6 proves step 5 completed.
                controller.WaitForStepStarted(6);

                Assert.Equal(1, transport.CountLines("syncPlatform generic"));
                Assert.Equal(2, transport.CountLines("syncPlatform fox"));
                Assert.Equal(3, transport.CountLines("end"));
            }
            finally
            {
                coordinator.StopSyncSession();
                controller.ReleaseAll();
                VerificationCompletion.Wait(host.KeepStopped, "Keep sync did not stop.");
            }
        }

        [Fact]
        public void KeepSync_RepeatedSamePlatform_DoesNotResetConfirmationState()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("generic");
            SyncCoordinatorHostSnapshot snapshot = CreateSnapshot(SyncMode.Foreground, IntPtr.Zero);
            TestSyncCoordinatorHost host = new TestSyncCoordinatorHost(snapshot);

            BoardRecognitionResult result = Create3x3Result(CreateGenericTargetBoard());

            DeterministicSampleController controller = new DeterministicSampleController();
            controller.ArmGate(2);
            controller.ArmGate(3);
            controller.ArmGate(4);
            controller.RecognitionFactory = _ => result;

            SyncSessionRuntimeDependencies runtime = CreateRuntime(host, controller, controller);
            coordinator.AttachRuntime(runtime);

            Assert.True(coordinator.TryStartKeepSync());
            try
            {
                transport.WaitForBatchCount(1);
                controller.ReleaseStep(2);
                transport.WaitForBatchCount(2);

                controller.WaitForStepStarted(3);
                // Repeated same platform must not reset confirmation state.
                coordinator.SetSyncPlatform("generic");

                controller.ReleaseStep(3);
                // Gate at step 4 proves step 3 dispatch completed silently.
                controller.WaitForStepStarted(4);
                Assert.Equal(2, transport.CountLines("end"));
            }
            finally
            {
                coordinator.StopSyncSession();
                controller.ReleaseAll();
                VerificationCompletion.Wait(host.KeepStopped, "Keep sync did not stop.");
            }
        }

        private static BoardCellState[] CreateGenericTargetBoard()
        {
            // B(0,0), W(1,0), B(0,1), W(1,1)
            BoardCellState[] board = new BoardCellState[9];
            board[0] = BoardCellState.Black; // (0,0)
            board[1] = BoardCellState.White; // (1,0)
            board[3] = BoardCellState.Black; // (0,1)
            board[4] = BoardCellState.White; // (1,1)
            return board;
        }

        private static BoardCellState[] CreateFoxTargetBoard()
        {
            // B(0,2), W(1,2), B(2,2)
            BoardCellState[] board = new BoardCellState[9];
            board[6] = BoardCellState.Black; // (0,2)
            board[7] = BoardCellState.White; // (1,2)
            board[8] = BoardCellState.Black; // (2,2)
            return board;
        }

        private static BoardCellState[] CreateAlternateTargetBoard()
        {
            // B(2,0), W(2,1)
            BoardCellState[] board = new BoardCellState[9];
            board[2] = BoardCellState.Black; // (2,0)
            board[5] = BoardCellState.White; // (2,1)
            return board;
        }

        private static BoardFrame CreateFrame(SyncMode syncMode = SyncMode.Foreground)
        {
            return new BoardFrame
            {
                SyncMode = syncMode,
                BoardSize = new BoardDimensions(3, 3),
                Viewport = new BoardViewport
                {
                    SourceBounds = new PixelRect(0, 0, 30, 30),
                    ScreenBounds = new PixelRect(0, 0, 30, 30),
                    CellWidth = 10d,
                    CellHeight = 10d
                }
            };
        }

        private static BoardRecognitionResult Create3x3Result(
            BoardCellState[] boardState,
            LastMoveSource lastMoveSource = LastMoveSource.None,
            int? foxMoveNumber = null,
            bool isValid = true)
        {
            List<string> protocolLines = new List<string>(3);
            StringBuilder payloadBuilder = new StringBuilder(3 * 3 * 2);
            for (int y = 0; y < 3; y++)
            {
                StringBuilder lineBuilder = new StringBuilder(3 * 2);
                for (int x = 0; x < 3; x++)
                {
                    if (x > 0)
                        lineBuilder.Append(',');
                    BoardCellState state = boardState[(y * 3) + x];
                    lineBuilder.Append((int)state);
                }
                string row = lineBuilder.ToString();
                protocolLines.Add("re=" + row);
                payloadBuilder.Append(row).Append('\n');
            }

            return new BoardRecognitionResult
            {
                Success = true,
                Viewport = new BoardViewport
                {
                    SourceBounds = new PixelRect(0, 0, 30, 30),
                    ScreenBounds = new PixelRect(0, 0, 30, 30),
                    CellWidth = 10d,
                    CellHeight = 10d
                },
                Snapshot = new BoardSnapshot
                {
                    Width = 3,
                    Height = 3,
                    BoardState = boardState,
                    IsValid = isValid,
                    Payload = payloadBuilder.ToString(),
                    ProtocolLines = protocolLines,
                    LastMoveSource = lastMoveSource,
                    FoxMoveNumber = foxMoveNumber
                }
            };
        }

        private static SyncCoordinatorHostSnapshot CreateSnapshot(SyncMode syncMode, IntPtr handle, int? foxMoveNumber = null)
        {
            return new SyncCoordinatorHostSnapshot
            {
                SyncMode = syncMode,
                BoardWidth = 3,
                BoardHeight = 3,
                SelectionBounds = new PixelRect(0, 0, 30, 30),
                SelectedWindowHandle = handle,
                FoxMoveNumber = foxMoveNumber,
                DpiScale = 1f,
                LegacyTypeToken = "0",
                ShowInBoard = false,
                SupportsForegroundFoxInBoardProtocol = false,
                AutoMinimize = false,
                SampleIntervalMs = 0
            };
        }

        private static SyncSessionRuntimeDependencies CreateRuntime(
            ISyncCoordinatorHost host,
            IBoardCaptureService captureService,
            IBoardRecognitionService recognitionService,
            IWindowDescriptorFactory descriptorFactory = null)
        {
            return new SyncSessionRuntimeDependencies
            {
                Host = host,
                CaptureService = captureService,
                RecognitionService = recognitionService,
                PlacementService = new PassivePlacementService(),
                OverlayService = new PassiveOverlayService(),
                WindowLocator = new LegacySyncWindowLocator(),
                WindowDescriptorFactory = descriptorFactory ?? new FixedWindowDescriptorFactory()
            };
        }

        private static void MaybeExportTrace(string fileName, RecordingTransport transport)
        {
            string traceDir = Environment.GetEnvironmentVariable("READBOARD_SNAPSHOT_TRACE_DIR");
            if (string.IsNullOrWhiteSpace(traceDir))
                return;

            traceDir = traceDir.Trim().Trim('"');
            Directory.CreateDirectory(traceDir);
            string filePath = Path.Combine(traceDir, fileName);
            File.WriteAllText(filePath, string.Join("\n", transport.SentLines) + "\n", new UTF8Encoding(false));
        }

        private sealed class RecordingTransport : IReadBoardTransport
        {
            private readonly object syncRoot = new object();

            public event EventHandler<string> MessageReceived;

            public List<string> SentLines { get; } = new List<string>();

            public bool IsConnected { get; private set; }

            public void Dispose()
            {
            }

            public void Emit(string rawLine)
            {
                MessageReceived?.Invoke(this, rawLine);
            }

            public void Send(string line)
            {
                lock (syncRoot)
                {
                    SentLines.Add(line);
                    Monitor.PulseAll(syncRoot);
                }
            }

            public void SendError(string message)
            {
            }

            public void Start()
            {
                IsConnected = true;
            }

            public void Stop()
            {
                IsConnected = false;
            }

            public bool WaitForBatchCount(int expectedEndCount)
            {
                lock (syncRoot)
                {
                    while (CountLinesUnsafe("end") < expectedEndCount)
                    {
                        if (!Monitor.Wait(syncRoot, VerificationCompletion.WatchdogTimeout))
                            throw new TimeoutException("Expected batch count " + expectedEndCount + " was not reached.");
                    }
                    return true;
                }
            }

            public bool WaitForLine(string expectedLine)
            {
                lock (syncRoot)
                {
                    while (!ContainsLineUnsafe(expectedLine))
                    {
                        if (!Monitor.Wait(syncRoot, VerificationCompletion.WatchdogTimeout))
                            throw new TimeoutException("Expected transport line was not sent: " + expectedLine);
                    }
                    return true;
                }
            }

            public int CountLines(string line)
            {
                lock (syncRoot)
                {
                    return CountLinesUnsafe(line);
                }
            }

            private int CountLinesUnsafe(string line)
            {
                int count = 0;
                for (int index = 0; index < SentLines.Count; index++)
                {
                    if (string.Equals(SentLines[index], line, StringComparison.Ordinal))
                        count++;
                }
                return count;
            }

            private bool ContainsLineUnsafe(string line)
            {
                for (int index = 0; index < SentLines.Count; index++)
                {
                    if (string.Equals(SentLines[index], line, StringComparison.Ordinal))
                        return true;
                }
                return false;
            }
        }

        private sealed class TestSyncCoordinatorHost : ISyncCoordinatorHost
        {
            private readonly SyncCoordinatorHostSnapshot snapshot;
            private long observationGeneration;

            public TestSyncCoordinatorHost(SyncCoordinatorHostSnapshot snapshot)
            {
                this.snapshot = snapshot;
            }

            public ManualResetEventSlim KeepStarted { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim KeepStopped { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim SyncCachesReset { get; } = new ManualResetEventSlim(false);
            public int SnapshotRequests { get; private set; }
            public IntPtr LastSelectedWindow { get; private set; }

            public SyncCoordinatorHostSnapshot CaptureSnapshot()
            {
                SnapshotRequests++;
                return snapshot;
            }

            public long AllocateSessionObservationGeneration()
            {
                return Interlocked.Increment(ref observationGeneration);
            }

            public void UpdateSelectedWindowHandle(IntPtr handle, long observationGeneration)
            {
                LastSelectedWindow = handle;
                if (snapshot != null)
                    snapshot.SelectedWindowHandle = handle;
            }

            public void OnKeepSyncStarted(long observationGeneration)
            {
                KeepStarted.Set();
            }

            public void OnKeepSyncStopped(bool continuousSyncActive, long observationGeneration)
            {
                KeepStopped.Set();
            }

            public void OnContinuousSyncStarted(long observationGeneration) { }
            public void OnContinuousSyncStopped(long observationGeneration) { }

            public void OnSyncCachesReset(long observationGeneration)
            {
                SyncCachesReset.Set();
            }

            public void OnBoardSnapshotRecognized(BoardSnapshot snapshot, TimeSpan duration, long observationGeneration) { }
            public void ShowMissingSyncSourceMessage() { }
            public void ShowRecognitionFailureMessage() { }
            public void MinimizeWindow() { }
            public bool TrySendPlaceProtocolError(string message) { return false; }
        }

        private sealed class DeterministicSampleController : IBoardCaptureService, IBoardRecognitionService
        {
            private const int MaxSteps = 32;
            private readonly ManualResetEventSlim[] stepStarted = new ManualResetEventSlim[MaxSteps];
            private readonly ManualResetEventSlim[] stepGate = new ManualResetEventSlim[MaxSteps];
            private readonly bool[] armedGates = new bool[MaxSteps];
            private readonly object gateLock = new object();
            private volatile bool releaseAllSignaled;

            private int sampleCount;
            [ThreadStatic] private static int currentThreadSample;

            public DeterministicSampleController()
            {
                for (int i = 0; i < MaxSteps; i++)
                {
                    stepStarted[i] = new ManualResetEventSlim(false);
                    stepGate[i] = new ManualResetEventSlim(false);
                }
            }

            public Func<int, BoardCaptureResult> CaptureFactory { get; set; }
            public Func<int, BoardRecognitionResult> RecognitionFactory { get; set; }

            public int SampleCount => Volatile.Read(ref sampleCount);

            public void ArmGate(int step)
            {
                if (step >= 0 && step < MaxSteps)
                {
                    lock (gateLock)
                    {
                        armedGates[step] = true;
                        stepGate[step].Reset();
                    }
                }
            }

            public void WaitForStepStarted(int step)
            {
                if (step >= 0 && step < MaxSteps)
                {
                    VerificationCompletion.Wait(stepStarted[step], "Step " + step + " did not start.");
                }
            }

            public void ReleaseStep(int step)
            {
                if (step >= 0 && step < MaxSteps)
                {
                    lock (gateLock)
                    {
                        stepGate[step].Set();
                    }
                }
            }

            public void ReleaseAll()
            {
                lock (gateLock)
                {
                    releaseAllSignaled = true;
                    for (int i = 0; i < MaxSteps; i++)
                    {
                        stepGate[i].Set();
                    }
                }
            }

            public BoardCaptureResult Capture(BoardCaptureRequest request)
            {
                int step = Interlocked.Increment(ref sampleCount) - 1;
                currentThreadSample = step;

                if (step >= 0 && step < MaxSteps)
                {
                    stepStarted[step].Set();

                    bool shouldWait;
                    lock (gateLock)
                    {
                        shouldWait = armedGates[step] && !releaseAllSignaled;
                    }

                    if (shouldWait)
                    {
                        stepGate[step].Wait();
                    }
                }

                if (CaptureFactory != null)
                {
                    return CaptureFactory(step);
                }

                return new BoardCaptureResult
                {
                    Success = true,
                    Frame = CreateFrame(request != null ? request.SyncMode : SyncMode.Foreground)
                };
            }

            public BoardRecognitionResult Recognize(BoardRecognitionRequest request)
            {
                int step = currentThreadSample;
                if (RecognitionFactory != null)
                {
                    return RecognitionFactory(step);
                }
                return null;
            }
        }

        private sealed class PassivePlacementService : IMovePlacementService
        {
            public bool CanResolvePlacementRegion(BoardFrame frame)
            {
                return false;
            }

            public MovePlacementResult Place(MovePlacementRequest request)
            {
                return new MovePlacementResult { Success = true };
            }
        }

        private sealed class PassiveOverlayService : IOverlayService
        {
            public OverlayUpdateResult BuildUpdate(OverlayUpdateRequest request)
            {
                return new OverlayUpdateResult();
            }

            public void Reset()
            {
            }
        }

        private sealed class FixedWindowDescriptorFactory : IWindowDescriptorFactory
        {
            private readonly string title;

            public FixedWindowDescriptorFactory(string title = "Fox")
            {
                this.title = title;
            }

            public bool TryCreate(IntPtr handle, out WindowDescriptor descriptor)
            {
                descriptor = new WindowDescriptor
                {
                    Handle = handle,
                    Title = title,
                    Bounds = new PixelRect(0, 0, 190, 190),
                    ClassName = "TestWindowClass",
                    DpiScale = 1f
                };
                return true;
            }
        }
    }
}
