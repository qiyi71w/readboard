using System;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class ControlCenterActionAdapterTests
    {
        [Theory]
        [InlineData("sync.quick", (int)ControlCenterActionKind.QuickSync)]
        [InlineData("sync.continuous", (int)ControlCenterActionKind.ContinuousSync)]
        [InlineData("sync.once", (int)ControlCenterActionKind.OneTimeSync)]
        [InlineData("sync.toggleAnalysis", (int)ControlCenterActionKind.ToggleAnalysis)]
        [InlineData("sync.swapOrder", (int)ControlCenterActionKind.SwapOrder)]
        [InlineData("sync.rebuild", (int)ControlCenterActionKind.ForceRebuild)]
        [InlineData("sync.clearBoard", (int)ControlCenterActionKind.ClearBoard)]
        public void ActionCommand_IsConvertedToTypedIntent(
            string commandType,
            int expectedKind)
        {
            Assert.True(
                MainForm.TryParseWebViewCommand(
                    "{\"type\":\"" + commandType + "\"}",
                    out ReadBoardUiCommand command));
            Assert.True(MainForm.TryCreateControlCenterActionIntent(
                command,
                out ControlCenterActionIntent intent));

            Assert.Equal((ControlCenterActionKind)expectedKind, intent.Kind);
        }

        [Theory]
        [InlineData("inside", (int)ControlCenterBoardSelectionMode.Inside)]
        [InlineData("rectangle", (int)ControlCenterBoardSelectionMode.Rectangle)]
        [InlineData("line1", (int)ControlCenterBoardSelectionMode.Line1)]
        public void BoardSelectionCommand_IsConvertedToTypedIntent(
            string mode,
            int expectedMode)
        {
            string json = "{\"type\":\"board.select\",\"payload\":{\"mode\":\""
                + mode
                + "\"}}";
            Assert.True(MainForm.TryParseWebViewCommand(json, out ReadBoardUiCommand command));
            Assert.True(MainForm.TryCreateControlCenterActionIntent(
                command,
                out ControlCenterActionIntent intent));

            Assert.Equal(ControlCenterActionKind.SelectBoard, intent.Kind);
            Assert.Equal((ControlCenterBoardSelectionMode)expectedMode, intent.BoardSelectionMode);
        }

        [Fact]
        public void Snapshot_UsesSessionStateForActionEnablement()
        {
            ControlCenterRuntime idle = CreateRuntime(new InMemoryControlCenterActionAdapter());
            Assert.True(idle.Snapshot.QuickSyncEnabled);
            Assert.True(idle.Snapshot.ContinuousSyncEnabled);
            Assert.True(idle.Snapshot.OneTimeSyncEnabled);
            Assert.False(idle.Snapshot.AnalysisToggleEnabled);
            Assert.True(idle.Snapshot.BoardSelectionInsideEnabled);
            Assert.False(idle.Snapshot.BoardSelectionRectangleEnabled);
            Assert.False(idle.Snapshot.BoardSelectionLine1Enabled);

            ControlCenterRuntime quick = CreateRuntime(
                new InMemoryControlCenterActionAdapter(),
                new ControlCenterSessionState { QuickSyncActive = true });
            Assert.True(quick.Snapshot.QuickSyncEnabled);
            Assert.False(quick.Snapshot.ContinuousSyncEnabled);
            Assert.False(quick.Snapshot.OneTimeSyncEnabled);
            Assert.False(quick.Snapshot.BoardSelectionInsideEnabled);

            ControlCenterRuntime continuous = CreateRuntime(
                new InMemoryControlCenterActionAdapter(),
                new ControlCenterSessionState { ContinuousSyncActive = true });
            Assert.True(continuous.Snapshot.QuickSyncEnabled);
            Assert.True(continuous.Snapshot.ContinuousSyncEnabled);
            Assert.False(continuous.Snapshot.OneTimeSyncEnabled);

            InMemoryControlCenterActionAdapter stopAdapter = new InMemoryControlCenterActionAdapter();
            ControlCenterActionApplyResult stop = CreateRuntime(
                stopAdapter,
                new ControlCenterSessionState { ContinuousSyncActive = true }).ApplyAction(
                    ControlCenterActionIntent.QuickSync());
            Assert.Equal(ControlCenterActionApplyOutcome.Accepted, stop.Outcome);
            Assert.Equal(ControlCenterActionEffectKind.StopSync, stopAdapter.Effects[0].Kind);

            ControlCenterRuntime runningWithoutCapability = CreateRuntime(
                new InMemoryControlCenterActionAdapter(),
                new ControlCenterSessionState { AnalysisRunning = true });
            Assert.True(runningWithoutCapability.Snapshot.AnalysisToggleEnabled);
        }

        [Fact]
        public void ActionEffects_PreserveExistingActionMappingAndOrder()
        {
            AssertSingleEffect(
                ControlCenterActionIntent.QuickSync(),
                ControlCenterActionEffectKind.StartQuickSync);
            AssertSingleEffect(
                ControlCenterActionIntent.ContinuousSync(),
                ControlCenterActionEffectKind.StartContinuousSync);
            AssertSingleEffect(
                ControlCenterActionIntent.OneTimeSync(),
                ControlCenterActionEffectKind.RunOneTimeSync);
            AssertSingleEffect(
                ControlCenterActionIntent.SwapOrder(),
                ControlCenterActionEffectKind.SwapOrder);
            AssertSingleEffect(
                ControlCenterActionIntent.ForceRebuild(),
                ControlCenterActionEffectKind.ForceRebuild);
            AssertSingleEffect(
                ControlCenterActionIntent.ClearBoard(),
                ControlCenterActionEffectKind.ClearBoard);

            InMemoryControlCenterActionAdapter analysisAdapter = new InMemoryControlCenterActionAdapter();
            ControlCenterRuntime analysis = CreateRuntime(
                analysisAdapter,
                new ControlCenterSessionState { AnalysisStateAvailable = true });
            ControlCenterActionApplyResult resume = analysis.ApplyAction(
                ControlCenterActionIntent.ToggleAnalysis());
            Assert.Equal(ControlCenterActionApplyOutcome.Accepted, resume.Outcome);
            Assert.Equal(
                ControlCenterActionEffectKind.ResumeAnalysis,
                analysisAdapter.Effects[0].Kind);

            InMemoryControlCenterActionAdapter pauseAdapter = new InMemoryControlCenterActionAdapter();
            ControlCenterRuntime paused = CreateRuntime(
                pauseAdapter,
                new ControlCenterSessionState
                {
                    AnalysisRunning = true,
                    AnalysisStateAvailable = true
                });
            ControlCenterActionApplyResult pause = paused.ApplyAction(
                ControlCenterActionIntent.ToggleAnalysis());
            Assert.Equal(ControlCenterActionApplyOutcome.Accepted, pause.Outcome);
            Assert.Equal(
                ControlCenterActionEffectKind.PauseAnalysis,
                pauseAdapter.Effects[0].Kind);
        }

        [Fact]
        public void ActiveSync_RejectsStaleActionsAndPublishesAuthoritativeSnapshot()
        {
            InMemoryControlCenterActionAdapter adapter = new InMemoryControlCenterActionAdapter();
            ControlCenterRuntime runtime = CreateRuntime(
                adapter,
                new ControlCenterSessionState { QuickSyncActive = true });

            ControlCenterActionApplyResult continuous = runtime.ApplyAction(
                ControlCenterActionIntent.ContinuousSync());
            ControlCenterActionApplyResult once = runtime.ApplyAction(
                ControlCenterActionIntent.OneTimeSync());
            ControlCenterActionApplyResult board = runtime.ApplyAction(
                ControlCenterActionIntent.SelectBoard(ControlCenterBoardSelectionMode.Inside));

            Assert.Equal(ControlCenterActionApplyOutcome.Rejected, continuous.Outcome);
            Assert.Equal(ControlCenterActionApplyOutcome.Rejected, once.Outcome);
            Assert.Equal(ControlCenterActionApplyOutcome.Rejected, board.Outcome);
            Assert.True(continuous.ShouldPublishSnapshot);
            Assert.True(once.ShouldPublishSnapshot);
            Assert.True(board.ShouldPublishSnapshot);
            Assert.True(continuous.Snapshot.QuickSyncActive);
            Assert.Empty(adapter.Effects);

            InMemoryControlCenterActionAdapter liveAdapter = new InMemoryControlCenterActionAdapter();
            ControlCenterRuntime live = CreateRuntime(
                liveAdapter,
                liveSyncOperationActive: true);
            Assert.False(live.Snapshot.OneTimeSyncEnabled);
            Assert.False(live.Snapshot.BoardSelectionInsideEnabled);
            Assert.Equal(
                ControlCenterActionApplyOutcome.Rejected,
                live.ApplyAction(ControlCenterActionIntent.OneTimeSync()).Outcome);
            Assert.Equal(
                ControlCenterActionApplyOutcome.Rejected,
                live.ApplyAction(ControlCenterActionIntent.SelectBoard(
                    ControlCenterBoardSelectionMode.Inside)).Outcome);
            Assert.Empty(liveAdapter.Effects);
        }

        [Fact]
        public void BoardSelection_UsesPlatformModeWithoutPersistingCoordinates()
        {
            InMemoryControlCenterActionAdapter manualAdapter = new InMemoryControlCenterActionAdapter();
            ControlCenterRuntime manual = CreateRuntime(
                manualAdapter,
                platform: SyncMode.Background);

            Assert.Equal(
                ControlCenterActionApplyOutcome.Rejected,
                manual.ApplyAction(ControlCenterActionIntent.SelectBoard(
                    ControlCenterBoardSelectionMode.Inside)).Outcome);
            Assert.Equal(
                ControlCenterActionApplyOutcome.Accepted,
                manual.ApplyAction(ControlCenterActionIntent.SelectBoard(
                    ControlCenterBoardSelectionMode.Rectangle)).Outcome);
            Assert.Equal(
                ControlCenterBoardSelectionMode.Rectangle,
                manualAdapter.Effects[0].BoardSelectionMode);

            InMemoryControlCenterActionAdapter nativeAdapter = new InMemoryControlCenterActionAdapter();
            ControlCenterRuntime native = CreateRuntime(nativeAdapter);
            Assert.Equal(
                ControlCenterActionApplyOutcome.Rejected,
                native.ApplyAction(ControlCenterActionIntent.SelectBoard(
                    ControlCenterBoardSelectionMode.Line1)).Outcome);
            Assert.Empty(nativeAdapter.Effects);
            Assert.Equal(ControlCenterActionEffectKind.SelectBoard, manualAdapter.Effects[0].Kind);
        }

        [Fact]
        public void UnavailableAnalysis_IsRejectedWithoutProtocolEffect()
        {
            InMemoryControlCenterActionAdapter adapter = new InMemoryControlCenterActionAdapter();
            ControlCenterRuntime runtime = CreateRuntime(adapter);

            ControlCenterActionApplyResult result = runtime.ApplyAction(
                ControlCenterActionIntent.ToggleAnalysis());

            Assert.Equal(ControlCenterActionApplyOutcome.Rejected, result.Outcome);
            Assert.True(result.ShouldPublishSnapshot);
            Assert.Empty(adapter.Effects);
        }

        [Fact]
        public void AdapterRejection_PublishesOneAuthoritativeSnapshotForEveryAction()
        {
            AssertAdapterRejected(ControlCenterActionIntent.QuickSync());
            AssertAdapterRejected(ControlCenterActionIntent.ContinuousSync());
            AssertAdapterRejected(ControlCenterActionIntent.OneTimeSync());
            AssertAdapterRejected(
                ControlCenterActionIntent.ToggleAnalysis(),
                new ControlCenterSessionState { AnalysisStateAvailable = true });
            AssertAdapterRejected(ControlCenterActionIntent.SwapOrder());
            AssertAdapterRejected(ControlCenterActionIntent.ForceRebuild());
            AssertAdapterRejected(ControlCenterActionIntent.ClearBoard());
            AssertAdapterRejected(ControlCenterActionIntent.SelectBoard(
                ControlCenterBoardSelectionMode.Inside));
        }

        [Fact]
        public void AdapterNoOp_PublishesNothingForEveryAction()
        {
            AssertAdapterNoOp(ControlCenterActionIntent.QuickSync());
            AssertAdapterNoOp(ControlCenterActionIntent.ContinuousSync());
            AssertAdapterNoOp(ControlCenterActionIntent.OneTimeSync());
            AssertAdapterNoOp(
                ControlCenterActionIntent.ToggleAnalysis(),
                new ControlCenterSessionState { AnalysisStateAvailable = true });
            AssertAdapterNoOp(ControlCenterActionIntent.SwapOrder());
            AssertAdapterNoOp(ControlCenterActionIntent.ForceRebuild());
            AssertAdapterNoOp(ControlCenterActionIntent.ClearBoard());
            AssertAdapterNoOp(ControlCenterActionIntent.SelectBoard(
                ControlCenterBoardSelectionMode.Inside));
        }

        [Fact]
        public void AdapterNoOp_PublishesNothingWhileRejectedAndAcceptedPublishOnce()
        {
            InMemoryControlCenterActionAdapter noOpAdapter = new InMemoryControlCenterActionAdapter();
            noOpAdapter.EnqueueOutcome(ControlCenterActionExecutionOutcome.NoOp);
            ControlCenterRuntime noOpRuntime = CreateRuntime(noOpAdapter);
            ControlCenterActionApplyResult noOp = noOpRuntime.ApplyAction(
                ControlCenterActionIntent.QuickSync());
            int noOpPublications = 0;
            Assert.Equal(ControlCenterActionApplyOutcome.NoOp, noOp.Outcome);
            Assert.False(ControlCenterSnapshotPublisher.PublishIfNeeded(
                noOp,
                delegate { noOpPublications++; }));
            Assert.Equal(0, noOpPublications);

            InMemoryControlCenterActionAdapter acceptedAdapter = new InMemoryControlCenterActionAdapter();
            ControlCenterRuntime acceptedRuntime = CreateRuntime(acceptedAdapter);
            ControlCenterActionApplyResult accepted = acceptedRuntime.ApplyAction(
                ControlCenterActionIntent.QuickSync());
            int acceptedPublications = 0;
            Assert.True(ControlCenterSnapshotPublisher.PublishIfNeeded(
                accepted,
                delegate { acceptedPublications++; }));
            Assert.Equal(1, acceptedPublications);

            ControlCenterActionApplyResult rejected = acceptedRuntime.ApplyAction(
                ControlCenterActionIntent.ToggleAnalysis());
            int rejectedPublications = 0;
            Assert.True(ControlCenterSnapshotPublisher.PublishIfNeeded(
                rejected,
                delegate { rejectedPublications++; }));
            Assert.Equal(1, rejectedPublications);
        }

        [Fact]
        public void AcceptedAction_DoesNotReplaceLaterAuthoritativeObservation()
        {
            InMemoryControlCenterActionAdapter adapter = new InMemoryControlCenterActionAdapter();
            ControlCenterRuntime runtime = CreateRuntime(adapter);

            ControlCenterActionApplyResult action = runtime.ApplyAction(
                ControlCenterActionIntent.QuickSync());
            ControlCenterSessionObservationApplyResult observation = runtime.ApplyObservation(
                new ControlCenterSessionObservation(runtime.BeginSessionObservationGeneration())
                    .WithSyncActivity(true, false));

            Assert.Equal(ControlCenterActionApplyOutcome.Accepted, action.Outcome);
            Assert.False(action.Snapshot.QuickSyncActive);
            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Applied, observation.Outcome);
            Assert.True(observation.Snapshot.QuickSyncActive);
            Assert.False(observation.Snapshot.ContinuousSyncActive);
        }

        private static void AssertSingleEffect(
            ControlCenterActionIntent intent,
            ControlCenterActionEffectKind expectedKind)
        {
            InMemoryControlCenterActionAdapter adapter = new InMemoryControlCenterActionAdapter();
            ControlCenterActionApplyResult result = CreateRuntime(adapter).ApplyAction(intent);

            Assert.Equal(ControlCenterActionApplyOutcome.Accepted, result.Outcome);
            Assert.Single(adapter.Effects);
            Assert.Equal(expectedKind, adapter.Effects[0].Kind);
        }

        private static void AssertAdapterRejected(
            ControlCenterActionIntent intent,
            ControlCenterSessionState sessionState = null)
        {
            InMemoryControlCenterActionAdapter adapter = new InMemoryControlCenterActionAdapter
            {
                DefaultOutcome = ControlCenterActionExecutionOutcome.Rejected
            };
            ControlCenterActionApplyResult result = CreateRuntime(
                adapter,
                sessionState).ApplyAction(intent);
            int publicationCount = 0;

            Assert.Equal(ControlCenterActionApplyOutcome.Rejected, result.Outcome);
            Assert.True(ControlCenterSnapshotPublisher.PublishIfNeeded(
                result,
                delegate { publicationCount++; }));
            Assert.Equal(1, publicationCount);
            Assert.Single(adapter.Effects);
        }

        private static void AssertAdapterNoOp(
            ControlCenterActionIntent intent,
            ControlCenterSessionState sessionState = null)
        {
            InMemoryControlCenterActionAdapter adapter = new InMemoryControlCenterActionAdapter
            {
                DefaultOutcome = ControlCenterActionExecutionOutcome.NoOp
            };
            ControlCenterActionApplyResult result = CreateRuntime(
                adapter,
                sessionState).ApplyAction(intent);
            int publicationCount = 0;

            Assert.Equal(ControlCenterActionApplyOutcome.NoOp, result.Outcome);
            Assert.False(ControlCenterSnapshotPublisher.PublishIfNeeded(
                result,
                delegate { publicationCount++; }));
            Assert.Equal(0, publicationCount);
            Assert.Single(adapter.Effects);
        }

        private static ControlCenterRuntime CreateRuntime(
            InMemoryControlCenterActionAdapter actionAdapter,
            ControlCenterSessionState sessionState = null,
            SyncMode platform = SyncMode.Fox,
            bool liveSyncOperationActive = false)
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncMode = platform;
            return new ControlCenterRuntime(
                ControlCenterPreferences.FromConfig(config),
                sessionState ?? new ControlCenterSessionState(),
                new RecordingSessionAdapter { HasActiveSyncOperation = liveSyncOperationActive },
                new RecordingPersistence(),
                actionAdapter);
        }

        private sealed class RecordingSessionAdapter : IControlCenterSessionAdapter
        {
            public bool HasActiveSyncOperation { get; set; }

            public void Apply(
                ControlCenterPreferences preferences,
                ControlCenterSessionState sessionState)
            {
            }
        }

        private sealed class RecordingPersistence : IControlCenterPreferencePersistence
        {
            public void Save(ControlCenterPreferences preferences)
            {
            }
        }
    }
}
