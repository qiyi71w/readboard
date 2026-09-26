using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Readboard.VerificationTests.Support;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Protocol
{
    // Runs the real request, worker, capture, recognition and result path with controlled observations.
    internal sealed class PendingMoveRequestHarness : IDisposable, ISyncCoordinatorHost,
        IBoardCaptureService, IBoardRecognitionService, IMovePlacementService, IOverlayService
    {
        private readonly BlockingCollection<BoardSnapshot> observations = new BlockingCollection<BoardSnapshot>();
        private readonly SemaphoreSlim observationRequested = new SemaphoreSlim(0);
        private readonly ManualResetEventSlim stopped = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim placementStarted = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim placementRelease = new ManualResetEventSlim(true);
        private readonly List<Task<PlaceRequestExecutionResult>> requests = new List<Task<PlaceRequestExecutionResult>>();
        private readonly ManualClock clock = new ManualClock();
        private readonly SyncCoordinatorHostSnapshot hostSnapshot = new SyncCoordinatorHostSnapshot
        {
            SyncMode = SyncMode.Foreground,
            BoardWidth = 3,
            BoardHeight = 3,
            SelectionBounds = new PixelRect(0, 0, 30, 30),
            DpiScale = 1f,
            LegacyTypeToken = "0",
            SampleIntervalMs = 0
        };
        private BoardSnapshot capturedSnapshot;
        private long observationGeneration;
        private int placementCount;
        private bool started;

        public PendingMoveRequestHarness()
        {
            Coordinator = new SyncSessionCoordinator(new QuietTransport(), new LegacyProtocolAdapter(), clock);
            Coordinator.AttachRuntime(new SyncSessionRuntimeDependencies
            {
                Host = this,
                CaptureService = this,
                RecognitionService = this,
                PlacementService = this,
                OverlayService = this
            });
        }

        public SyncSessionCoordinator Coordinator { get; }
        public int PlacementCount => Volatile.Read(ref placementCount);
        public MoveRequest LastMove { get; private set; }
        public bool PlacementSuccess { get; set; } = true;

        public void Start()
        {
            Coordinator.SetSyncBoth(true);
            started = Coordinator.TryStartKeepSync();
            Assert.True(started);
        }

        public Task<PlaceRequestExecutionResult> Request(bool verify = true, int? maxAttempts = null)
        {
            Task<PlaceRequestExecutionResult> task = Task.Factory.StartNew(
                () => Coordinator.HandlePlaceRequest(new MoveRequest
                {
                    X = 1, Y = 1, VerifyMove = verify, MoveVerifyMaxAttempts = maxAttempts
                }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            requests.Add(task);
            return task;
        }

        public void WaitForObservation()
        {
            Assert.True(observationRequested.Wait(VerificationCompletion.WatchdogTimeout),
                "The worker did not request a post-placement observation.");
        }

        public void Observe(bool occupied, bool valid = true)
        {
            observations.Add(CreateSnapshot(occupied, valid));
        }

        public void AdvanceMilliseconds(long milliseconds) => clock.AdvanceMilliseconds(milliseconds);
        public void BlockPlacement() => placementRelease.Reset();
        public void ReleasePlacement() => placementRelease.Set();
        public void WaitForPlacement() => VerificationCompletion.Wait(placementStarted, "Placement did not start.");

        public BoardCaptureResult Capture(BoardCaptureRequest request)
        {
            if (PlacementCount == 0)
                capturedSnapshot = CreateSnapshot(false, true);
            else
            {
                observationRequested.Release();
                if (!observations.TryTake(out capturedSnapshot, Timeout.Infinite))
                    return new BoardCaptureResult { Success = false };
            }
            return new BoardCaptureResult
            {
                Success = true,
                Frame = new BoardFrame
                {
                    SyncMode = SyncMode.Foreground,
                    BoardSize = new BoardDimensions(3, 3),
                    Viewport = CreateViewport()
                }
            };
        }

        public BoardRecognitionResult Recognize(BoardRecognitionRequest request)
        {
            return new BoardRecognitionResult
            {
                Success = true, Snapshot = capturedSnapshot, Viewport = CreateViewport()
            };
        }

        public bool CanResolvePlacementRegion(BoardFrame frame) => true;

        public MovePlacementResult Place(MovePlacementRequest request)
        {
            LastMove = request.Move;
            Interlocked.Increment(ref placementCount);
            placementStarted.Set();
            VerificationCompletion.Wait(placementRelease, "Placement was not released.");
            return new MovePlacementResult { Success = PlacementSuccess };
        }

        public OverlayUpdateResult BuildUpdate(OverlayUpdateRequest request) => new OverlayUpdateResult();
        public void Reset() { }
        public SyncCoordinatorHostSnapshot CaptureSnapshot() => hostSnapshot;
        public long AllocateSessionObservationGeneration() => Interlocked.Increment(ref observationGeneration);
        public void UpdateSelectedWindowHandle(IntPtr handle, long generation) { }
        public void OnKeepSyncStarted(long generation) { }
        public void OnKeepSyncStopped(bool continuousSyncActive, long generation) => stopped.Set();
        public void OnContinuousSyncStarted(long generation) { }
        public void OnContinuousSyncStopped(long generation) { }
        public void OnSyncCachesReset(long generation) { }
        public void OnBoardSnapshotRecognized(BoardSnapshot snapshot, TimeSpan duration, long generation) { }
        public void ShowMissingSyncSourceMessage() { }
        public void ShowRecognitionFailureMessage() { }
        public void MinimizeWindow() { }
        public bool TrySendPlaceProtocolError(string message) => false;

        public void Dispose()
        {
            Coordinator.StopSyncSession();
            placementRelease.Set();
            observations.CompleteAdding();
            if (started)
                VerificationCompletion.Wait(stopped, "Pending move worker did not stop.");
            foreach (Task<PlaceRequestExecutionResult> request in requests)
                Assert.True(request.Wait(VerificationCompletion.WatchdogTimeout), "Pending move request did not retire.");
            Coordinator.Dispose();
            observations.Dispose();
            observationRequested.Dispose();
            stopped.Dispose();
            placementStarted.Dispose();
            placementRelease.Dispose();
        }

        private static BoardViewport CreateViewport() => new BoardViewport
        {
            SourceBounds = new PixelRect(0, 0, 30, 30),
            ScreenBounds = new PixelRect(0, 0, 30, 30),
            CellWidth = 10d, CellHeight = 10d
        };

        private static BoardSnapshot CreateSnapshot(bool occupied, bool valid)
        {
            BoardCellState[] board = new BoardCellState[9];
            if (occupied)
                board[4] = BoardCellState.Black;
            return new BoardSnapshot
            {
                Width = 3, Height = 3, IsValid = valid, BoardState = board,
                Payload = occupied ? "0,0,0\n0,1,0\n0,0,0\n" : "0,0,0\n0,0,0\n0,0,0\n",
                ProtocolLines = new[] { "re=0,0,0", occupied ? "re=0,1,0" : "re=0,0,0", "re=0,0,0" }
            };
        }

        private sealed class ManualClock : TimeProvider
        {
            private long timestamp;
            public override long TimestampFrequency => 1000;
            public override long GetTimestamp() => Interlocked.Read(ref timestamp);
            public void AdvanceMilliseconds(long milliseconds) => Interlocked.Add(ref timestamp, milliseconds);
        }

        private sealed class QuietTransport : IReadBoardTransport
        {
            public event EventHandler<string> MessageReceived { add { } remove { } }
            public bool IsConnected => true;
            public void Start() { }
            public void Stop() { }
            public void Send(string line) { }
            public void SendError(string message) { }
            public void Dispose() { }
        }
    }
}
