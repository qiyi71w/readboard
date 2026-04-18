using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Readboard.VerificationTests;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    internal static class SustainedSyncAcceptanceHarness
    {
        private const int BaseStableTickCount = 8;
        private const int ChangedStableTickCount = 6;
        private const int ReturnBaseTickCount = 6;
        private const int FinalChangedTickCount = 4;
        private const int SampleIntervalMs = 200;
        private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(12);

        public static SustainedSyncAcceptanceReport MeasureDefaultAcceptance()
        {
            ReplayFixture fixture = ReplayFixtureCatalog.LoadForeground5x5();
            ReplayVariant[] sequence = CreateTickSequence();
            SustainedSyncObserver observer = new SustainedSyncObserver(sequence.Length);
            RecordingTransport transport = new RecordingTransport();
            RuntimeHost host = new RuntimeHost(CreateSnapshot(fixture));
            StaticWindowLocator locator = new StaticWindowLocator(new IntPtr(4242));
            StaticDescriptorFactory descriptorFactory = new StaticDescriptorFactory();
            SequencedCaptureService capture = new SequencedCaptureService(fixture, sequence, observer);
            MeasuringRecognitionService recognition = new MeasuringRecognitionService(observer);
            SyncSessionCoordinator coordinator = CreateCoordinator(transport, host, locator, descriptorFactory, capture, recognition);

            bool started = false;
            try
            {
                started = coordinator.TryStartContinuousSync();
                if (!started)
                    return observer.BuildReport(transport.GetSentLinesSnapshot(), transport.ErrorCount, host, locator.CallCount, false);
                if (!observer.WaitForWorkerTicks(CompletionTimeout))
                    return observer.BuildReport(transport.GetSentLinesSnapshot(), transport.ErrorCount, host, locator.CallCount, false);
                if (!transport.WaitForLineCount(observer.ExpectedLineCountBeforeStop, CompletionTimeout))
                    return observer.BuildReport(transport.GetSentLinesSnapshot(), transport.ErrorCount, host, locator.CallCount, false);
                coordinator.StopSyncSession();
                started = false;
                return observer.BuildReport(transport.GetSentLinesSnapshot(), transport.ErrorCount, host, locator.CallCount, true);
            }
            finally
            {
                if (started)
                    coordinator.StopSyncSession();
            }
        }

        private static ReplayVariant[] CreateTickSequence()
        {
            List<ReplayVariant> sequence = new List<ReplayVariant>();
            Append(sequence, ReplayVariant.Base, BaseStableTickCount);
            Append(sequence, ReplayVariant.Changed, ChangedStableTickCount);
            Append(sequence, ReplayVariant.Base, ReturnBaseTickCount);
            Append(sequence, ReplayVariant.Changed, FinalChangedTickCount);
            return sequence.ToArray();
        }

        private static void Append(List<ReplayVariant> sequence, ReplayVariant variant, int count)
        {
            for (int index = 0; index < count; index++)
                sequence.Add(variant);
        }

        private static SyncCoordinatorHostSnapshot CreateSnapshot(ReplayFixture fixture)
        {
            return new SyncCoordinatorHostSnapshot
            {
                SyncMode = SyncMode.Background,
                BoardWidth = fixture.BoardWidth,
                BoardHeight = fixture.BoardHeight,
                SelectionBounds = new PixelRect(40, 60, 80, 80),
                SelectedWindowHandle = IntPtr.Zero,
                DpiScale = 1f,
                LegacyTypeToken = "5",
                ShowInBoard = true,
                SupportsForegroundFoxInBoardProtocol = false,
                CanUseLightweightInterop = false,
                AutoMinimize = false,
                SampleIntervalMs = SampleIntervalMs
            };
        }

        private static SyncSessionCoordinator CreateCoordinator(
            RecordingTransport transport,
            RuntimeHost host,
            StaticWindowLocator locator,
            StaticDescriptorFactory descriptorFactory,
            SequencedCaptureService capture,
            MeasuringRecognitionService recognition)
        {
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.BindSessionState(new SessionState());
            coordinator.AttachRuntime(new SyncSessionRuntimeDependencies
            {
                Host = host,
                WindowLocator = locator,
                WindowDescriptorFactory = descriptorFactory,
                CaptureService = capture,
                RecognitionService = recognition,
                PlacementService = new PassivePlacementService(),
                OverlayService = new LegacyOverlayService()
            });
            return coordinator;
        }

        private sealed class RecordingTransport : IReadBoardTransport
        {
            private readonly object gate = new object();
            private readonly List<string> sentLines = new List<string>();

            public event EventHandler<string> MessageReceived
            {
                add { }
                remove { }
            }

            public bool IsConnected
            {
                get { return true; }
            }

            public int ErrorCount { get; private set; }

            public void Dispose()
            {
            }

            public void Send(string line)
            {
                lock (gate)
                {
                    sentLines.Add(line);
                    System.Threading.Monitor.PulseAll(gate);
                }
            }

            public void SendError(string message)
            {
                ErrorCount++;
            }

            public void Start()
            {
            }

            public void Stop()
            {
            }

            public string[] GetSentLinesSnapshot()
            {
                lock (gate)
                {
                    return sentLines.ToArray();
                }
            }

            public bool WaitForLineCount(int expectedLineCount, TimeSpan timeout)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                lock (gate)
                {
                    while (sentLines.Count < expectedLineCount)
                    {
                        TimeSpan remaining = timeout - stopwatch.Elapsed;
                        if (remaining <= TimeSpan.Zero)
                            return false;
                        if (!System.Threading.Monitor.Wait(gate, remaining))
                            return sentLines.Count >= expectedLineCount;
                    }
                    return true;
                }
            }
        }

        internal sealed class RuntimeHost : ISyncCoordinatorHost
        {
            private readonly SyncCoordinatorHostSnapshot snapshot;

            public RuntimeHost(SyncCoordinatorHostSnapshot snapshot)
            {
                this.snapshot = snapshot;
            }

            public int CaptureSnapshotCount { get; private set; }
            public bool KeepSyncStartedObserved { get; private set; }
            public bool KeepSyncStoppedObserved { get; private set; }
            public bool ContinuousSyncStartedObserved { get; private set; }
            public bool ContinuousSyncStoppedObserved { get; private set; }

            public SyncCoordinatorHostSnapshot CaptureSnapshot()
            {
                CaptureSnapshotCount++;
                return snapshot;
            }

            public void UpdateSelectedWindowHandle(IntPtr handle)
            {
                snapshot.SelectedWindowHandle = handle;
            }

            public void OnKeepSyncStarted()
            {
                KeepSyncStartedObserved = true;
            }

            public void OnKeepSyncStopped(bool continuousSyncActive)
            {
                KeepSyncStoppedObserved = true;
            }

            public void OnContinuousSyncStarted()
            {
                ContinuousSyncStartedObserved = true;
            }

            public void OnContinuousSyncStopped()
            {
                ContinuousSyncStoppedObserved = true;
            }

            public void ShowMissingSyncSourceMessage()
            {
            }

            public void ShowRecognitionFailureMessage()
            {
            }

            public void MinimizeWindow()
            {
            }

            public void ReleasePlacementBinding(IntPtr handle)
            {
            }

            public bool TrySendPlaceProtocolError(string message)
            {
                return false;
            }
        }

        internal sealed class StaticWindowLocator : ISyncWindowLocator
        {
            private readonly IntPtr handle;

            public StaticWindowLocator(IntPtr handle)
            {
                this.handle = handle;
            }

            public int CallCount { get; private set; }

            public IntPtr FindWindowHandle(SyncMode syncMode)
            {
                CallCount++;
                return handle;
            }
        }

        private sealed class StaticDescriptorFactory : IWindowDescriptorFactory
        {
            public bool TryCreate(IntPtr handle, float dpiScale, out WindowDescriptor descriptor)
            {
                descriptor = new WindowDescriptor
                {
                    Handle = handle,
                    Bounds = new PixelRect(40, 60, 80, 80),
                    ClassName = "ReplayBoard",
                    Title = "Replay",
                    IsDpiAware = true,
                    DpiScale = dpiScale <= 0f ? 1f : dpiScale
                };
                return true;
            }
        }

        private sealed class SequencedCaptureService : IBoardCaptureService
        {
            private readonly ReplayFixture fixture;
            private readonly ReplayVariant[] sequence;
            private readonly SustainedSyncObserver observer;
            private int captureIndex;

            public SequencedCaptureService(
                ReplayFixture fixture,
                ReplayVariant[] sequence,
                SustainedSyncObserver observer)
            {
                this.fixture = fixture;
                this.sequence = sequence;
                this.observer = observer;
            }

            public BoardCaptureResult Capture(BoardCaptureRequest request)
            {
                int index = captureIndex++;
                observer.OnCaptureStarted(index == 0);
                ReplayVariant variant = ResolveVariant(index);
                BoardCaptureResult result = fixture.CreateCaptureResult(variant);
                BoardFrame frame = result.Frame;
                frame.SyncMode = request.SyncMode;
                frame.Window = request.Window;
                frame.Viewport.ScreenBounds = BuildScreenBounds(request.Window, frame.Viewport.SourceBounds);
                return result;
            }

            private ReplayVariant ResolveVariant(int captureCallIndex)
            {
                if (captureCallIndex <= 0)
                    return sequence[0];
                return sequence[captureCallIndex - 1];
            }

            private static PixelRect BuildScreenBounds(WindowDescriptor window, PixelRect sourceBounds)
            {
                if (window == null || window.Bounds == null || sourceBounds == null)
                    return sourceBounds;
                return new PixelRect(
                    window.Bounds.X + sourceBounds.X,
                    window.Bounds.Y + sourceBounds.Y,
                    sourceBounds.Width,
                    sourceBounds.Height);
            }
        }

        private sealed class MeasuringRecognitionService : IBoardRecognitionService
        {
            private readonly SustainedSyncObserver observer;
            private readonly LegacyBoardRecognitionService inner = new LegacyBoardRecognitionService();

            public MeasuringRecognitionService(SustainedSyncObserver observer)
            {
                this.observer = observer;
            }

            public BoardRecognitionResult Recognize(BoardRecognitionRequest request)
            {
                BoardRecognitionResult result = inner.Recognize(request);
                observer.OnRecognitionCompleted(result);
                return result;
            }
        }

        private sealed class PassivePlacementService : IMovePlacementService
        {
            public MovePlacementResult Place(MovePlacementRequest request)
            {
                return new MovePlacementResult { Success = true };
            }
        }
    }

    internal sealed class SustainedSyncAcceptanceReport
    {
        public static readonly TimeSpan TickBudget = TimeSpan.FromMilliseconds(200);
        private const int FixedCoordinatorControlLineCount = 3;

        private SustainedSyncAcceptanceReport(
            int expectedWorkerTickCount,
            int workerTickCount,
            int captureCallCount,
            int recognitionCallCount,
            int windowLocatorCallCount,
            int outboundLineCount,
            int expectedOutboundLineCount,
            int overBudgetTickCount,
            int payloadTransitionCount,
            int errorCount,
            int captureSnapshotCount,
            TimeSpan maxTickElapsed,
            bool keepSyncStartedObserved,
            bool keepSyncStoppedObserved,
            bool continuousSyncStartedObserved,
            bool continuousSyncStoppedObserved,
            bool coordinatorStartObserved,
            bool coordinatorHandshakeObserved)
        {
            ExpectedWorkerTickCount = expectedWorkerTickCount;
            WorkerTickCount = workerTickCount;
            CaptureCallCount = captureCallCount;
            RecognitionCallCount = recognitionCallCount;
            WindowLocatorCallCount = windowLocatorCallCount;
            OutboundLineCount = outboundLineCount;
            ExpectedOutboundLineCount = expectedOutboundLineCount;
            OverBudgetTickCount = overBudgetTickCount;
            PayloadTransitionCount = payloadTransitionCount;
            ErrorCount = errorCount;
            CaptureSnapshotCount = captureSnapshotCount;
            MaxTickElapsed = maxTickElapsed;
            KeepSyncStartedObserved = keepSyncStartedObserved;
            KeepSyncStoppedObserved = keepSyncStoppedObserved;
            ContinuousSyncStartedObserved = continuousSyncStartedObserved;
            ContinuousSyncStoppedObserved = continuousSyncStoppedObserved;
            CoordinatorStartObserved = coordinatorStartObserved;
            CoordinatorHandshakeObserved = coordinatorHandshakeObserved;
        }

        public int ExpectedWorkerTickCount { get; private set; }
        public int WorkerTickCount { get; private set; }
        public int CaptureCallCount { get; private set; }
        public int RecognitionCallCount { get; private set; }
        public int ExpectedCaptureCallCount
        {
            get { return ExpectedWorkerTickCount + 1; }
        }

        public int ExpectedRecognitionCallCount
        {
            get { return ExpectedWorkerTickCount + 1; }
        }

        public int WindowLocatorCallCount { get; private set; }
        public int OutboundLineCount { get; private set; }
        public int ExpectedOutboundLineCount { get; private set; }
        public int OverBudgetTickCount { get; private set; }
        public int PayloadTransitionCount { get; private set; }
        public int ErrorCount { get; private set; }
        public int CaptureSnapshotCount { get; private set; }
        public TimeSpan MaxTickElapsed { get; private set; }
        public bool KeepSyncStartedObserved { get; private set; }
        public bool KeepSyncStoppedObserved { get; private set; }
        public bool ContinuousSyncStartedObserved { get; private set; }
        public bool ContinuousSyncStoppedObserved { get; private set; }
        public bool CoordinatorStartObserved { get; private set; }
        public bool CoordinatorHandshakeObserved { get; private set; }

        public bool MeetsAcceptance
        {
            get
            {
                return CoordinatorStartObserved
                    && CoordinatorHandshakeObserved
                    && KeepSyncStartedObserved
                    && KeepSyncStoppedObserved
                    && ContinuousSyncStartedObserved
                    && ContinuousSyncStoppedObserved
                    && WorkerTickCount == ExpectedWorkerTickCount
                    && CaptureCallCount == ExpectedCaptureCallCount
                    && RecognitionCallCount == ExpectedRecognitionCallCount
                    && WindowLocatorCallCount > 0
                    && OutboundLineCount == ExpectedOutboundLineCount
                    && OverBudgetTickCount == 0
                    && PayloadTransitionCount > 0
                    && PayloadTransitionCount < WorkerTickCount
                    && ErrorCount == 0;
            }
        }

        public string DescribeFailure()
        {
            return "CoordinatorStart=" + CoordinatorStartObserved
                + ", Handshake=" + CoordinatorHandshakeObserved
                + ", WorkerTicks=" + WorkerTickCount + "/" + ExpectedWorkerTickCount
                + ", CaptureCalls=" + CaptureCallCount + "/" + ExpectedCaptureCallCount
                + ", RecognitionCalls=" + RecognitionCallCount + "/" + ExpectedRecognitionCallCount
                + ", WindowLocatorCalls=" + WindowLocatorCallCount
                + ", OutboundLines=" + OutboundLineCount + "/" + ExpectedOutboundLineCount
                + ", OverBudgetTicks=" + OverBudgetTickCount
                + ", PayloadTransitions=" + PayloadTransitionCount
                + ", Errors=" + ErrorCount
                + ", CaptureSnapshots=" + CaptureSnapshotCount
                + ", MaxTickMs=" + MaxTickElapsed.TotalMilliseconds.ToString("F3");
        }

        public static SustainedSyncAcceptanceReport Create(
            int expectedWorkerTickCount,
            int workerTickCount,
            int captureCallCount,
            int recognitionCallCount,
            int windowLocatorCallCount,
            int outboundLineCount,
            int dynamicOutboundLineCount,
            int overBudgetTickCount,
            int payloadTransitionCount,
            int errorCount,
            int captureSnapshotCount,
            TimeSpan maxTickElapsed,
            bool keepSyncStartedObserved,
            bool keepSyncStoppedObserved,
            bool continuousSyncStartedObserved,
            bool continuousSyncStoppedObserved,
            bool coordinatorStartObserved,
            bool coordinatorHandshakeObserved)
        {
            return new SustainedSyncAcceptanceReport(
                expectedWorkerTickCount,
                workerTickCount,
                captureCallCount,
                recognitionCallCount,
                windowLocatorCallCount,
                outboundLineCount,
                FixedCoordinatorControlLineCount + windowLocatorCallCount + dynamicOutboundLineCount,
                overBudgetTickCount,
                payloadTransitionCount,
                errorCount,
                captureSnapshotCount,
                maxTickElapsed,
                keepSyncStartedObserved,
                keepSyncStoppedObserved,
                continuousSyncStartedObserved,
                continuousSyncStoppedObserved,
                coordinatorStartObserved,
                coordinatorHandshakeObserved);
        }
    }

    internal sealed class SustainedSyncObserver
    {
        private readonly object gate = new object();
        private readonly Queue<Stopwatch> activeTicks = new Queue<Stopwatch>();
        private readonly ManualResetEventSlim workerTicksCompleted = new ManualResetEventSlim(false);
        private readonly int expectedWorkerTickCount;
        private string lastPayload;
        private int dynamicOutboundLineCount;

        public SustainedSyncObserver(int expectedWorkerTickCount)
        {
            this.expectedWorkerTickCount = expectedWorkerTickCount;
        }

        public int CaptureCallCount { get; private set; }
        public int RecognitionCallCount { get; private set; }
        public int WorkerTickCount { get; private set; }
        public int OverBudgetTickCount { get; private set; }
        public int PayloadTransitionCount { get; private set; }
        public TimeSpan MaxTickElapsed { get; private set; }
        public int ExpectedLineCountBeforeStop
        {
            get { return FixedPreStopCoordinatorLineCount + dynamicOutboundLineCount; }
        }

        public void OnCaptureStarted(bool isPrimeProbe)
        {
            lock (gate)
            {
                CaptureCallCount++;
                activeTicks.Enqueue(Stopwatch.StartNew());
            }
        }

        public void OnRecognitionCompleted(BoardRecognitionResult result)
        {
            lock (gate)
            {
                RecognitionCallCount++;
                Stopwatch stopwatch = activeTicks.Dequeue();
                stopwatch.Stop();
                if (RecognitionCallCount == 1)
                    return;

                WorkerTickCount++;
                ObserveTickBudget(stopwatch.Elapsed);
                ObservePayload(result == null ? null : result.Snapshot);
                if (WorkerTickCount == expectedWorkerTickCount)
                    workerTicksCompleted.Set();
            }
        }

        public bool WaitForWorkerTicks(TimeSpan timeout)
        {
            return workerTicksCompleted.Wait(timeout);
        }

        public SustainedSyncAcceptanceReport BuildReport(
            string[] sentLines,
            int errorCount,
            SustainedSyncAcceptanceHarness.RuntimeHost host,
            int windowLocatorCallCount,
            bool coordinatorStartObserved)
        {
            return SustainedSyncAcceptanceReport.Create(
                expectedWorkerTickCount,
                WorkerTickCount,
                CaptureCallCount,
                RecognitionCallCount,
                windowLocatorCallCount,
                sentLines == null ? 0 : sentLines.Length,
                dynamicOutboundLineCount,
                OverBudgetTickCount,
                PayloadTransitionCount,
                errorCount,
                host == null ? 0 : host.CaptureSnapshotCount,
                MaxTickElapsed,
                host != null && host.KeepSyncStartedObserved,
                host != null && host.KeepSyncStoppedObserved,
                host != null && host.ContinuousSyncStartedObserved,
                host != null && host.ContinuousSyncStoppedObserved,
                coordinatorStartObserved,
                HasCoordinatorHandshake(sentLines));
        }

        private void ObserveTickBudget(TimeSpan elapsed)
        {
            if (elapsed > SustainedSyncAcceptanceReport.TickBudget)
                OverBudgetTickCount++;
            if (elapsed > MaxTickElapsed)
                MaxTickElapsed = elapsed;
        }

        private void ObservePayload(BoardSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.Payload))
                return;
            if (snapshot.ProtocolLines == null || snapshot.ProtocolLines.Count == 0)
                return;
            if (string.Equals(lastPayload, snapshot.Payload, StringComparison.Ordinal))
                return;

            lastPayload = snapshot.Payload;
            PayloadTransitionCount++;
            dynamicOutboundLineCount += snapshot.ProtocolLines.Count + 1;
            if (PayloadTransitionCount == 1)
                dynamicOutboundLineCount += 2;
        }

        private static bool HasCoordinatorHandshake(string[] sentLines)
        {
            if (sentLines == null || sentLines.Length < 6)
                return false;
            if (!string.Equals(sentLines[0], "noinboard", StringComparison.Ordinal))
                return false;
            if (!string.Equals(sentLines[1], "notForeFoxWithInBoard", StringComparison.Ordinal))
                return false;
            if (!string.Equals(sentLines[2], "sync", StringComparison.Ordinal))
                return false;
            if (Array.IndexOf(sentLines, "stopsync") < 0)
                return false;
            for (int index = 0; index < sentLines.Length; index++)
            {
                if (string.Equals(sentLines[index], "start 5 5", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private const int FixedPreStopCoordinatorLineCount = 3;
    }
}
