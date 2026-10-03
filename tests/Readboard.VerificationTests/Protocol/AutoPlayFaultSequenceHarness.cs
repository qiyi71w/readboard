using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using readboard;
using Readboard.VerificationTests.Host;
using Readboard.VerificationTests.Support;

namespace Readboard.VerificationTests.Protocol
{
    // Only external facts, I/O and scheduling are controlled. Authorization stays in production.
    internal sealed class AutoPlayFaultSequenceHarness : ISyncCoordinatorHost, IWebViewSyncCoordinatorHost, IDisposable
    {
        private readonly object uiLock = new object();
        private readonly object traceLock = new object();
        private readonly List<string> trace = new List<string>();
        private readonly List<ControlCenterSessionObservation> deferredObservations = new List<ControlCenterSessionObservation>();
        private readonly TranscriptTransport transport;
        private readonly ManualResetEventSlim keepStopped = new ManualResetEventSlim(true);
        private readonly ManualResetEventSlim transportDisconnected = new ManualResetEventSlim(false);
        private bool deferBoardObservations;

        public AutoPlayFaultSequenceHarness(IReadBoardTransport externalTransport = null)
        {
            Environment = new SequenceEnvironment { Players = Players("black"), TargetWindowValid = true };
            transport = new TranscriptTransport(this, externalTransport);
            Coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncMode = SyncMode.Fox;
            config.SyncBoth = true;
            config.AutoPlayColorMode = AutoPlayColorMode.FoxAuto;
            Runtime = new ControlCenterRuntime(ControlCenterPreferences.FromConfig(config),
                new ControlCenterSessionState(), Environment, Environment,
                new RejectingControlCenterActionAdapter(), Coordinator,
                new FoxIdentitySelection(new RuntimeIdentityPersistence { Saved = "self" }));
            Runtime.ProjectCurrentState();
            Runtime.CompleteInitialization();
            Samples = new RecognitionSteps(this);
            Coordinator.AttachRuntime(new SyncSessionRuntimeDependencies
            {
                Host = this,
                CaptureService = Samples,
                RecognitionService = Samples,
                PlacementService = new ExternalPlacement(),
                OverlayService = new ExternalOverlay(),
                WindowDescriptorFactory = new ExternalWindowDescriptors()
            });
            Coordinator.Start();
            transport.Disconnected += (_, _) =>
            {
                Record("transport disconnected after coordinator closed protocol session");
                transportDisconnected.Set();
            };
        }

        public SequenceEnvironment Environment { get; }
        public ControlCenterRuntime Runtime { get; }
        public SyncSessionCoordinator Coordinator { get; }
        public RecognitionSteps Samples { get; }
        public List<ControlCenterSessionObservationApplyOutcome> DeferredOutcomes { get; } = new List<ControlCenterSessionObservationApplyOutcome>();
        public string[] Wire { get { return transport.CopyLines(); } }
        public string[] AutoPlayWire { get { return Wire.Where(line => line.StartsWith("play>", StringComparison.Ordinal) || line == "stopAutoPlay").ToArray(); } }
        public string Transcript { get { lock (traceLock) return string.Join(System.Environment.NewLine, trace); } }

        public void AtUi(string label, Action action)
        {
            lock (uiLock)
            {
                Record(label);
                action();
                RecordState(label);
            }
        }

        public void EnableAndStart()
        {
            AtUi("enable automatic", () => Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true)));
            if (!StartKeepSync())
                throw new InvalidOperationException("Keep sync did not start.");
            Samples.Wait(1);
        }

        public bool StartKeepSync()
        {
            return Samples.RunStartup(Coordinator.TryStartKeepSync);
        }

        public void DeferBoardObservations()
        {
            lock (uiLock) deferBoardObservations = true;
        }

        public void DeliverDeferredObservations()
        {
            lock (uiLock)
            {
                deferBoardObservations = false;
                foreach (ControlCenterSessionObservation observation in deferredObservations)
                {
                    ControlCenterSessionObservationApplyOutcome outcome = Runtime.ApplyObservation(observation).Outcome;
                    DeferredOutcomes.Add(outcome);
                    Record("deliver board observation generation=" + observation.Generation + " outcome=" + outcome);
                }
                deferredObservations.Clear();
                RecordState("late observations delivered");
            }
        }

        public static FoxMatchBarReading Players(string color)
        {
            return new FoxMatchBarReading(new[]
            {
                new FoxPlayerListEntry(color == "white" ? "self" : "other", null),
                new FoxPlayerListEntry(color == "black" ? "self" : "other", null)
            });
        }

        public void Record(string entry)
        {
            lock (traceLock) trace.Add((trace.Count + 1).ToString("D3") + " " + entry);
        }

        private void RecordState(string label)
        {
            ControlCenterRuntimeSnapshot state = Runtime.Snapshot;
            Record(label + " state: enabled=" + state.AutoPlayEnabled + " color=" + (state.PlayColor ?? "unknown")
                + " room=" + state.FoxWindowContext.RoomToken + " keep=" + Coordinator.KeepSync
                + " generation=" + Runtime.CaptureSessionObservationGeneration());
        }

        public SyncCoordinatorHostSnapshot CaptureSnapshot()
        {
            lock (uiLock)
            {
                Runtime.RefreshAutoPlayColor(out _);
                ControlCenterRuntimeSnapshot state = Runtime.Snapshot;
                ControlCenterPreferences preferences = Runtime.CurrentPreferences;
                Coordinator.SetFoxWindowContext(state.FoxWindowContext);
                RecordState("capture host snapshot");
                return new SyncCoordinatorHostSnapshot
                {
                    SyncMode = preferences.Platform,
                    BoardWidth = preferences.BoardWidth,
                    BoardHeight = preferences.BoardHeight,
                    SelectionBounds = new PixelRect(0, 0, 190, 190),
                    SelectedWindowHandle = Environment.Handle,
                    DpiScale = 1f,
                    LegacyTypeToken = ((int)preferences.Platform).ToString(),
                    SampleIntervalMs = 0,
                    FoxMoveNumber = state.FoxWindowContext.ResolveDisplayedMoveNumber(),
                    PlayColor = state.PlayColor,
                    AutoPlayColorMode = state.AutoPlayColorMode,
                    AiTimeValue = state.AiTimeValue,
                    PlayoutsValue = state.PlayoutsValue,
                    FirstPolicyValue = state.FirstPolicyValue,
                    AutoPlayMoveMode = state.AutoPlayMoveMode
                };
            }
        }

        public long AllocateSessionObservationGeneration()
        {
            lock (uiLock) return Runtime.BeginSessionObservationGeneration();
        }

        public void UpdateSelectedWindowHandle(IntPtr handle, long generation)
        {
            AtUi("selected handle=" + handle, () =>
            {
                Environment.Handle = handle;
                Runtime.ApplyObservation(new ControlCenterSessionObservation(generation).WithTargetWindowValid(handle != IntPtr.Zero));
            });
        }

        public void OnKeepSyncStarted(long generation)
        {
            keepStopped.Reset();
            AtUi("keep sync started", () =>
            {
                Environment.HasActiveSyncOperation = true;
                Runtime.ApplyObservation(new ControlCenterSessionObservation(generation).WithSyncActivity(false, true));
            });
        }

        public void OnKeepSyncStopped(bool continuousSyncActive, long generation)
        {
            AtUi("keep sync stopped", () =>
            {
                Environment.HasActiveSyncOperation = continuousSyncActive;
                Runtime.ApplyObservation(new ControlCenterSessionObservation(generation).WithSyncActivity(continuousSyncActive, false));
            });
            keepStopped.Set();
        }

        public void WaitForTransportDisconnected()
        {
            VerificationCompletion.Wait(transportDisconnected, "Unexpected transport disconnect was not handled.\n" + Transcript);
        }

        public void WaitForKeepSyncStopped()
        {
            if (!keepStopped.Wait(VerificationCompletion.WatchdogTimeout))
                throw new TimeoutException("Keep-sync retirement callback did not complete.\n" + Transcript);
        }

        public void OnContinuousSyncStarted(long generation) { Runtime.ApplyObservation(new ControlCenterSessionObservation(generation).WithSyncActivity(true, false)); }
        public void OnContinuousSyncStopped(long generation) { Runtime.ApplyObservation(new ControlCenterSessionObservation(generation).WithSyncActivity(false, Coordinator.StartedSync)); }
        public void OnSyncCachesReset(long generation) { Runtime.ApplyObservation(new ControlCenterSessionObservation(generation).WithTitleTurn(MainWindowTitleTurn.None)); }
        public void OnRuntimeFrameCleared(long generation) { Runtime.ApplyObservation(new ControlCenterSessionObservation(generation).ClearRuntimeFrame()); }

        private void ObserveBoard(ControlCenterSessionObservation observation)
        {
            lock (uiLock)
            {
                if (deferBoardObservations)
                {
                    deferredObservations.Add(observation);
                    Record("queue board observation generation=" + observation.Generation);
                }
                else Runtime.ApplyObservation(observation);
            }
        }

        public void OnBoardSnapshotRecognized(BoardSnapshot snapshot, TimeSpan duration, long generation)
        {
            ObserveBoard(new ControlCenterSessionObservation(generation).WithRecentSync(
                "12:00:00", snapshot.BlackStoneCount + snapshot.WhiteStoneCount, "1ms"));
        }
        public void OnBoardFrameRecognized(BoardFrame frame, int width, int height, bool placementResolved, long generation)
        {
            ObserveBoard(new ControlCenterSessionObservation(generation).WithBoardRegion(true, placementResolved));
        }
        public void OnBoardSnapshotSent(BoardSnapshot snapshot, long generation) { Record("board sent generation=" + generation); }
        public void ShowMissingSyncSourceMessage() { Record("missing sync source"); }
        public void ShowRecognitionFailureMessage() { Record("recognition failure"); }
        public void MinimizeWindow() { Record("minimize window"); }
        public bool TrySendPlaceProtocolError(string message) { return false; }

        public void Dispose()
        {
            Coordinator.StopSyncSession();
            Samples.ReleaseAll();
            WaitForKeepSyncStopped();
            Coordinator.Dispose();
            Samples.Dispose();
            keepStopped.Dispose();
            transportDisconnected.Dispose();
        }

        internal sealed class SequenceEnvironment : RuntimeTestEnvironment, IControlCenterPreferencePersistence
        {
            public void Save(ControlCenterPreferences preferences) { }
        }

        internal sealed class RecognitionSteps : IBoardCaptureService, IBoardRecognitionService, IDisposable
        {
            private readonly AutoPlayFaultSequenceHarness owner;
            private readonly object syncRoot = new object();
            private readonly Dictionary<int, Step> steps = new Dictionary<int, Step>();
            private int count;
            private bool released;
            [ThreadStatic] private static bool startupRecognition;

            public bool RunStartup(Func<bool> start)
            {
                startupRecognition = true;
                try { return start(); }
                finally { startupRecognition = false; }
            }
            public RecognitionSteps(AutoPlayFaultSequenceHarness owner) { this.owner = owner; }
            private Step Get(int number)
            {
                lock (syncRoot)
                {
                    if (!steps.TryGetValue(number, out Step step)) steps.Add(number, step = new Step());
                    return step;
                }
            }
            public void Wait(int number)
            {
                if (!Get(number).Entered.Wait(VerificationCompletion.WatchdogTimeout))
                    throw new TimeoutException("Recognition step " + number + " did not start.\n" + owner.Transcript);
            }
            public void Release(int number) { owner.Record("release recognition step=" + number); Get(number).Released.Set(); }
            public void ReleaseAll()
            {
                lock (syncRoot)
                {
                    released = true;
                    foreach (Step step in steps.Values) step.Released.Set();
                }
            }
            public BoardCaptureResult Capture(BoardCaptureRequest request)
            {
                return new BoardCaptureResult
                {
                    Success = true,
                    Frame = new BoardFrame
                    {
                        SyncMode = request.SyncMode,
                        BoardSize = new BoardDimensions(19, 19),
                        Viewport = new BoardViewport { SourceBounds = new PixelRect(0, 0, 190, 190), ScreenBounds = new PixelRect(0, 0, 190, 190), CellWidth = 10, CellHeight = 10 }
                    }
                };
            }
            public BoardRecognitionResult Recognize(BoardRecognitionRequest request)
            {
                if (startupRecognition)
                {
                    owner.Record("synchronous startup recognition");
                    return CreateResult(request, "re=prime");
                }
                int number = Interlocked.Increment(ref count);
                Step step = Get(number);
                owner.Record("recognition entered step=" + number);
                step.Entered.Set();
                bool shouldWait;
                lock (syncRoot) shouldWait = !released;
                // The controller has bounded waits and always releases gates in finally.
                if (shouldWait) step.Released.Wait();
                owner.Record("recognition returned step=" + number);
                return CreateResult(request, "re=step-" + number);
            }
            private static BoardRecognitionResult CreateResult(BoardRecognitionRequest request, string payload)
            {
                return new BoardRecognitionResult
                {
                    Success = true,
                    Viewport = request.Frame.Viewport,
                    Snapshot = new BoardSnapshot { Width = 19, Height = 19, IsValid = true, Payload = payload, ProtocolLines = new[] { payload } }
                };
            }
            public void Dispose()
            {
                foreach (Step step in steps.Values) { step.Entered.Dispose(); step.Released.Dispose(); }
            }
            private sealed class Step
            {
                public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false);
                public readonly ManualResetEventSlim Released = new ManualResetEventSlim(false);
            }
        }

        private sealed class TranscriptTransport : IReadBoardTransport
        {
            private readonly AutoPlayFaultSequenceHarness owner;
            private readonly IReadBoardTransport external;
            private readonly List<string> lines = new List<string>();
            private bool connected;
            public TranscriptTransport(AutoPlayFaultSequenceHarness owner, IReadBoardTransport external) { this.owner = owner; this.external = external; }
            public event EventHandler<string> MessageReceived
            {
                add { if (external != null) external.MessageReceived += value; }
                remove { if (external != null) external.MessageReceived -= value; }
            }
            public event EventHandler Disconnected
            {
                add { if (external != null) external.Disconnected += value; }
                remove { if (external != null) external.Disconnected -= value; }
            }
            public bool IsConnected { get { return external == null ? connected : external.IsConnected; } }
            public void Start() { external?.Start(); connected = true; }
            public void Stop() { external?.Stop(); connected = false; }
            public void Send(string line)
            {
                owner.Record("send attempt: " + line);
                external?.Send(line);
                lock (lines) lines.Add(line);
                owner.Record("wire: " + line);
            }
            public void SendError(string message) { Send("error: " + message); }
            public string[] CopyLines() { lock (lines) return lines.ToArray(); }
            public void Dispose() { external?.Dispose(); }
        }

        private sealed class ExternalWindowDescriptors : IWindowDescriptorFactory
        {
            public bool TryCreate(IntPtr handle, out WindowDescriptor descriptor)
            {
                descriptor = new WindowDescriptor { Handle = handle, Bounds = new PixelRect(0, 0, 190, 190), ClassName = "FoxBoard", Title = "Fox", IsDpiAware = true, DpiScale = 1d };
                return handle != IntPtr.Zero;
            }
        }
        private sealed class ExternalPlacement : IMovePlacementService
        {
            public bool CanResolvePlacementRegion(BoardFrame frame) { return false; }
            public MovePlacementResult Place(MovePlacementRequest request) { throw new InvalidOperationException("These sequences must not place moves."); }
        }
        private sealed class ExternalOverlay : IOverlayService
        {
            public OverlayUpdateResult BuildUpdate(OverlayUpdateRequest request) { return new OverlayUpdateResult(); }
            public void Reset() { }
        }
    }
}
