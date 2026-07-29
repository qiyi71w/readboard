using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class ControlCenterRuntimeTests
    {
        [Fact]
        public void PlatformIntent_UpdatesSessionPersistsOnceAndPublishesSavedSnapshot()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(initial, session, persistence);

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetPlatform(SyncMode.Yike));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.Equal(SyncMode.Yike, result.Snapshot.Platform);
            Assert.Equal(SyncMode.Yike, session.Applied[0].Platform);
            Assert.Single(persistence.Saved);
            Assert.True(result.Snapshot.PreferencesSaved);
            Assert.False(result.Snapshot.CustomBoardSizeEnabled);
            Assert.True(result.ShouldPublishSnapshot);
        }

        [Fact]
        public void SameValueIntent_IsNoOpWithoutAdapterOrPersistenceCall()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(initial, session, persistence);

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetPlatform(SyncMode.Fox));

            Assert.Equal(ControlCenterApplyOutcome.NoOp, result.Outcome);
            Assert.Empty(session.Applied);
            Assert.Empty(persistence.Saved);
            Assert.False(result.ShouldPublishSnapshot);
        }

        [Fact]
        public void ActiveSync_RejectsPlatformChangeAndReturnsAuthoritativeSnapshot()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RecordingSessionAdapter session = new RecordingSessionAdapter { HasActiveSyncOperation = true };
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(initial, session, persistence);

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetPlatform(SyncMode.Yike));

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.Equal(SyncMode.Fox, result.Snapshot.Platform);
            Assert.Equal(19, result.Snapshot.BoardWidth);
            Assert.False(result.Snapshot.ConfigurationEnabled);
            Assert.False(result.Snapshot.CustomBoardSizeEnabled);
            Assert.False(result.Snapshot.CustomBoardDimensionsEnabled);
            Assert.Empty(session.Applied);
            Assert.Empty(persistence.Saved);
            Assert.True(result.ShouldPublishSnapshot);
        }

        [Theory]
        [InlineData(99, true)]
        [InlineData(99, false)]
        public void InvalidEnumIntent_IsRejectedWithoutSessionOrPersistence(int value, bool platform)
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(initial, session, persistence);

            ControlCenterApplyResult result = runtime.Apply(platform
                ? ControlCenterIntent.SetPlatform((SyncMode)value)
                : ControlCenterIntent.SetBoardSize((ControlCenterBoardSizeKind)value));

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.Equal(SyncMode.Fox, result.Snapshot.Platform);
            Assert.Equal(ControlCenterBoardSizeKind.Preset19, result.Snapshot.BoardSizeKind);
            Assert.Empty(session.Applied);
            Assert.Empty(persistence.Saved);
            Assert.True(result.ShouldPublishSnapshot);
        }

        [Fact]
        public void CustomBoardIntent_UsesExistingCustomDimensionsAndReportsEnablement()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncMode = SyncMode.Background;
            config.BoardWidth = 17;
            config.BoardHeight = 9;
            config.CustomBoardWidth = 17;
            config.CustomBoardHeight = 9;
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                ControlCenterPreferences.FromConfig(config),
                new RecordingSessionAdapter(),
                new RecordingPersistence());

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetBoardSize(ControlCenterBoardSizeKind.Custom));

            Assert.Equal(ControlCenterApplyOutcome.NoOp, result.Outcome);
            Assert.Equal(ControlCenterBoardSizeKind.Custom, result.Snapshot.BoardSizeKind);
            Assert.Equal(17, result.Snapshot.BoardWidth);
            Assert.Equal(9, result.Snapshot.BoardHeight);
            Assert.True(result.Snapshot.ConfigurationEnabled);
            Assert.True(result.Snapshot.CustomBoardSizeEnabled);
            Assert.True(result.Snapshot.CustomBoardDimensionsEnabled);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CustomBoardDimensionIntent_IsRejectedWhenBoardSizeIsNotCustom(bool width)
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncMode = SyncMode.Background;
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                ControlCenterPreferences.FromConfig(config),
                session,
                persistence);

            ControlCenterApplyResult result = runtime.Apply(width
                ? ControlCenterIntent.SetCustomBoardWidth(17)
                : ControlCenterIntent.SetCustomBoardHeight(9));

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.Equal(ControlCenterBoardSizeKind.Preset19, result.Snapshot.BoardSizeKind);
            Assert.True(result.Snapshot.CustomBoardSizeEnabled);
            Assert.False(result.Snapshot.CustomBoardDimensionsEnabled);
            Assert.Empty(session.Applied);
            Assert.Empty(persistence.Saved);
            Assert.True(result.ShouldPublishSnapshot);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CustomBoardDimensionIntent_IsRejectedWhenPlatformDoesNotAllowManualSelection(bool width)
        {
            ControlCenterPreferences initial = new ControlCenterPreferences
            {
                Platform = SyncMode.Fox,
                BoardSizeKind = ControlCenterBoardSizeKind.Custom,
                BoardWidth = 17,
                BoardHeight = 9,
                CustomBoardWidth = 17,
                CustomBoardHeight = 9
            };
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(initial, session, persistence);

            ControlCenterApplyResult result = runtime.Apply(width
                ? ControlCenterIntent.SetCustomBoardWidth(18)
                : ControlCenterIntent.SetCustomBoardHeight(10));

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.Equal(SyncMode.Fox, result.Snapshot.Platform);
            Assert.Equal(ControlCenterBoardSizeKind.Custom, result.Snapshot.BoardSizeKind);
            Assert.False(result.Snapshot.CustomBoardSizeEnabled);
            Assert.False(result.Snapshot.CustomBoardDimensionsEnabled);
            Assert.Empty(session.Applied);
            Assert.Empty(persistence.Saved);
            Assert.True(result.ShouldPublishSnapshot);
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(26, true)]
        [InlineData(1, false)]
        [InlineData(26, false)]
        public void InvalidCustomBoardDimensionIntent_IsRejectedWithoutPersistence(
            int dimension,
            bool width)
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncMode = SyncMode.Background;
            config.BoardWidth = 17;
            config.BoardHeight = 9;
            config.CustomBoardWidth = 17;
            config.CustomBoardHeight = 9;
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                ControlCenterPreferences.FromConfig(config),
                session,
                persistence);

            ControlCenterApplyResult result = runtime.Apply(width
                ? ControlCenterIntent.SetCustomBoardWidth(dimension)
                : ControlCenterIntent.SetCustomBoardHeight(dimension));

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.Equal(17, result.Snapshot.BoardWidth);
            Assert.Equal(9, result.Snapshot.BoardHeight);
            Assert.Empty(session.Applied);
            Assert.Empty(persistence.Saved);
            Assert.True(result.ShouldPublishSnapshot);
        }

        [Fact]
        public void CustomBoardDimensionIntent_UpdatesSessionAndPersistsOnce()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncMode = SyncMode.Background;
            config.BoardWidth = 19;
            config.BoardHeight = 19;
            config.CustomBoardWidth = 19;
            config.CustomBoardHeight = 19;
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                ControlCenterPreferences.FromConfig(config),
                session,
                persistence);

            ControlCenterApplyResult selectCustom = runtime.Apply(
                ControlCenterIntent.SetBoardSize(ControlCenterBoardSizeKind.Custom));
            ControlCenterApplyResult setWidth = runtime.Apply(
                ControlCenterIntent.SetCustomBoardWidth(17));

            Assert.Equal(ControlCenterApplyOutcome.Changed, selectCustom.Outcome);
            Assert.Equal(ControlCenterApplyOutcome.Changed, setWidth.Outcome);
            Assert.Equal(17, setWidth.Snapshot.BoardWidth);
            Assert.Equal(19, setWidth.Snapshot.BoardHeight);
            Assert.Equal(2, session.Applied.Count);
            Assert.Equal(2, persistence.Saved.Count);
            Assert.True(setWidth.Snapshot.CustomBoardSizeEnabled);
            Assert.True(setWidth.Snapshot.CustomBoardDimensionsEnabled);
        }

        [Fact]
        public void UnavailableCustomBoardIntent_IsRejectedWithoutPersistence()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(initial, session, persistence);

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetBoardSize(ControlCenterBoardSizeKind.Custom));

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.Equal(ControlCenterBoardSizeKind.Preset19, result.Snapshot.BoardSizeKind);
            Assert.False(result.Snapshot.CustomBoardSizeEnabled);
            Assert.False(result.Snapshot.CustomBoardDimensionsEnabled);
            Assert.Empty(session.Applied);
            Assert.Empty(persistence.Saved);
        }

        [Fact]
        public void PersistenceFailure_LeavesChangedSessionActiveAndMarksSnapshotNotSaved()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence { Failure = new IOException("disk full") };
            ControlCenterRuntime runtime = new ControlCenterRuntime(initial, session, persistence);

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetPlatform(SyncMode.Yike));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.Equal(SyncMode.Yike, runtime.Snapshot.Platform);
            Assert.False(result.Snapshot.PreferencesSaved);
            Assert.Equal("disk full", result.Snapshot.PersistenceError);
            Assert.Single(session.Applied);
            Assert.Single(persistence.Saved);
        }

        [Fact]
        public void LaterChange_RetriesPersistenceOnceWithoutBackgroundRetry()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence
            {
                Failure = new IOException("disk full")
            };
            ControlCenterRuntime runtime = new ControlCenterRuntime(initial, session, persistence);

            runtime.Apply(ControlCenterIntent.SetPlatform(SyncMode.Yike));
            Assert.Single(persistence.Saved);

            persistence.Failure = null;
            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetBoardSize(ControlCenterBoardSizeKind.Preset13));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.Equal(2, persistence.Saved.Count);
            Assert.True(result.Snapshot.PreferencesSaved);
            Assert.Equal(13, result.Snapshot.BoardWidth);
            Assert.Equal(13, result.Snapshot.BoardHeight);
        }

        [Fact]
        public void TwoWaySyncIntent_UpdatesSessionAndPersistsOnce()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(initial, session, persistence);

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetTwoWaySync(true));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.True(result.Snapshot.TwoWaySync);
            Assert.True(session.Applied[0].TwoWaySync);
            Assert.Single(persistence.Saved);
            Assert.True(persistence.Saved[0].TwoWaySync);
            Assert.True(result.Snapshot.TwoWaySyncEnabled);
        }

        [Fact]
        public void ShowOnBoardIntent_OnSupportedPlatformUpdatesSessionAndPersistsOnce()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(initial, session, persistence);

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetShowOnBoard(true));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.True(result.Snapshot.ShowOnBoard);
            Assert.True(result.Snapshot.ShowOnBoardEnabled);
            Assert.True(session.Applied[0].ShowOnBoard);
            Assert.Single(persistence.Saved);
            Assert.True(persistence.Saved[0].ShowOnBoard);
        }

        [Fact]
        public void ShowOnBoardIntent_OnForegroundIsRejectedWithoutSessionOrPersistence()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncMode = SyncMode.Foreground;
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                ControlCenterPreferences.FromConfig(config),
                session,
                persistence);

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetShowOnBoard(true));

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.False(result.Snapshot.ShowOnBoard);
            Assert.False(result.Snapshot.ShowOnBoardEnabled);
            Assert.Empty(session.Applied);
            Assert.Empty(persistence.Saved);
            Assert.True(result.ShouldPublishSnapshot);
        }

        [Fact]
        public void RuntimeConstructor_NormalizesUnsupportedShowOnBoardPreference()
        {
            ControlCenterPreferences initial = new ControlCenterPreferences
            {
                Platform = SyncMode.Foreground,
                BoardSizeKind = ControlCenterBoardSizeKind.Preset19,
                BoardWidth = 19,
                BoardHeight = 19,
                ShowOnBoard = true
            };
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                initial,
                new RecordingSessionAdapter(),
                new RecordingPersistence());

            Assert.False(runtime.Snapshot.ShowOnBoard);
            Assert.False(runtime.Snapshot.ShowOnBoardEnabled);
        }

        [Fact]
        public void PlatformIntent_NormalizesShowOnBoardWhenSwitchingToForeground()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.ShowInBoard = true;
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                ControlCenterPreferences.FromConfig(config),
                session,
                persistence);

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetPlatform(SyncMode.Foreground));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.Equal(SyncMode.Foreground, result.Snapshot.Platform);
            Assert.False(result.Snapshot.ShowOnBoard);
            Assert.False(result.Snapshot.ShowOnBoardEnabled);
            Assert.Single(session.Applied);
            Assert.False(session.Applied[0].ShowOnBoard);
            Assert.Single(persistence.Saved);
            Assert.False(persistence.Saved[0].ShowOnBoard);
        }

        [Fact]
        public void SyncPreferences_CanChangeWhileSyncOperationIsActive()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RecordingSessionAdapter session = new RecordingSessionAdapter { HasActiveSyncOperation = true };
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(initial, session, persistence);

            ControlCenterApplyResult twoWay = runtime.Apply(
                ControlCenterIntent.SetTwoWaySync(true));
            ControlCenterApplyResult show = runtime.Apply(
                ControlCenterIntent.SetShowOnBoard(true));

            Assert.Equal(ControlCenterApplyOutcome.Changed, twoWay.Outcome);
            Assert.Equal(ControlCenterApplyOutcome.Changed, show.Outcome);
            Assert.Equal(2, session.Applied.Count);
            Assert.Equal(2, persistence.Saved.Count);
            Assert.False(twoWay.Snapshot.ConfigurationEnabled);
            Assert.True(twoWay.Snapshot.TwoWaySyncEnabled);
            Assert.True(show.Snapshot.ShowOnBoardEnabled);
        }

        [Fact]
        public void ShowOnBoardPersistenceFailure_LeavesActiveChoiceAndMarksNotSaved()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence
            {
                Failure = new IOException("disk full")
            };
            ControlCenterRuntime runtime = new ControlCenterRuntime(initial, session, persistence);

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetShowOnBoard(true));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.True(runtime.Snapshot.ShowOnBoard);
            Assert.False(result.Snapshot.PreferencesSaved);
            Assert.Equal("disk full", result.Snapshot.PersistenceError);
            Assert.Single(session.Applied);
            Assert.Single(persistence.Saved);
        }

        [Fact]
        public void TwoWaySyncEffectPlan_PreservesProtocolOrderAndForegroundFoxCondition()
        {
            ControlCenterPreferences preferences = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            preferences.TwoWaySync = true;
            preferences.ShowOnBoard = true;

            IList<ControlCenterSessionEffect> effects = ControlCenterSessionEffectPlanner.PlanTwoWaySync(
                preferences,
                true);

            Assert.Equal(
                new[]
                {
                    ControlCenterSessionEffectKind.SendBothSync,
                    ControlCenterSessionEffectKind.SendForegroundFoxInBoard,
                    ControlCenterSessionEffectKind.ResendSyncSessionState
                },
                effects.Select(effect => effect.Kind));
            Assert.True(effects[0].Enabled);
            Assert.True(effects[1].Enabled);
        }

        [Fact]
        public void ShowOnBoardEffectPlan_PreservesForegroundFoxNotInBoardAndHintSemantics()
        {
            IList<ControlCenterSessionEffect> enabledEffects = ControlCenterSessionEffectPlanner.PlanShowOnBoard(
                true,
                false,
                true,
                true);
            IList<ControlCenterSessionEffect> disabledEffects = ControlCenterSessionEffectPlanner.PlanShowOnBoard(
                false,
                true,
                true,
                true);
            IList<ControlCenterSessionEffect> noHintEffects = ControlCenterSessionEffectPlanner.PlanShowOnBoard(
                true,
                true,
                true,
                false);

            Assert.Equal(
                new[]
                {
                    ControlCenterSessionEffectKind.SendForegroundFoxInBoard,
                    ControlCenterSessionEffectKind.ShowOnBoardHint
                },
                enabledEffects.Select(effect => effect.Kind));
            Assert.False(enabledEffects[0].Enabled);
            Assert.Equal(
                new[]
                {
                    ControlCenterSessionEffectKind.SendForegroundFoxInBoard,
                    ControlCenterSessionEffectKind.SendNotInBoard
                },
                disabledEffects.Select(effect => effect.Kind));
            Assert.DoesNotContain(
                noHintEffects,
                effect => effect.Kind == ControlCenterSessionEffectKind.ShowOnBoardHint);
        }

        [Fact]
        public void SnapshotPublisher_PublishesExactlyOnceForChangedRejectedAndInvalidResults()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncMode = SyncMode.Foreground;
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(config);
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                initial,
                new RecordingSessionAdapter(),
                new RecordingPersistence());
            int publicationCount = 0;
            Action publish = delegate { publicationCount++; };

            ControlCenterApplyResult changed = runtime.Apply(
                ControlCenterIntent.SetTwoWaySync(true));
            ControlCenterApplyResult noOp = runtime.Apply(
                ControlCenterIntent.SetTwoWaySync(true));
            ControlCenterApplyResult rejected = runtime.Apply(
                ControlCenterIntent.SetShowOnBoard(true));
            ControlCenterApplyResult invalid = runtime.Apply(
                ControlCenterIntent.SetPlatform((SyncMode)99));

            Assert.True(ControlCenterSnapshotPublisher.PublishIfNeeded(changed, publish));
            Assert.False(ControlCenterSnapshotPublisher.PublishIfNeeded(noOp, publish));
            Assert.True(ControlCenterSnapshotPublisher.PublishIfNeeded(rejected, publish));
            Assert.True(ControlCenterSnapshotPublisher.PublishIfNeeded(invalid, publish));
            Assert.Equal(3, publicationCount);
        }

        [Theory]
        [InlineData("fox", (int)SyncMode.Fox)]
        [InlineData("foxBackground", (int)SyncMode.FoxBackgroundPlace)]
        [InlineData("yike", (int)SyncMode.Yike)]
        [InlineData("yicheng", (int)SyncMode.Tygem)]
        [InlineData("sina", (int)SyncMode.Sina)]
        [InlineData("otherBackground", (int)SyncMode.Background)]
        [InlineData("otherForeground", (int)SyncMode.Foreground)]
        public void WebViewPlatformShape_IsConvertedToTypedIntent(string value, int expected)
        {
            string json = "{\"type\":\"control.update\",\"payload\":{\"key\":\"platform\",\"value\":\""
                + value
                + "\"}}";
            Assert.True(MainForm.TryParseWebViewCommand(json, out ReadBoardUiCommand command));
            Assert.True(MainForm.TryCreateControlCenterIntent(command, out ControlCenterIntent intent));
            Assert.Equal(ControlCenterIntentKind.SetPlatform, intent.Kind);
            Assert.Equal((SyncMode)expected, intent.Platform);
        }

        [Theory]
        [InlineData("19", (int)ControlCenterBoardSizeKind.Preset19)]
        [InlineData("13", (int)ControlCenterBoardSizeKind.Preset13)]
        [InlineData("9", (int)ControlCenterBoardSizeKind.Preset9)]
        [InlineData("custom", (int)ControlCenterBoardSizeKind.Custom)]
        public void WebViewBoardSizeShape_IsConvertedToTypedIntent(string value, int expected)
        {
            string json = "{\"type\":\"control.update\",\"payload\":{\"key\":\"boardSize\",\"value\":\""
                + value
                + "\"}}";

            Assert.True(MainForm.TryParseWebViewCommand(json, out ReadBoardUiCommand command));
            Assert.True(MainForm.TryCreateControlCenterIntent(command, out ControlCenterIntent intent));
            Assert.Equal(ControlCenterIntentKind.SetBoardSize, intent.Kind);
            Assert.Equal((ControlCenterBoardSizeKind)expected, intent.BoardSizeKind);
        }

        [Theory]
        [InlineData("board-width", 17, (int)ControlCenterIntentKind.SetCustomBoardWidth)]
        [InlineData("board-height", 9, (int)ControlCenterIntentKind.SetCustomBoardHeight)]
        public void WebViewCustomBoardDimensionShape_IsConvertedToTypedIntent(
            string key,
            int expectedDimension,
            int expectedKind)
        {
            string json = "{\"type\":\"control.update\",\"payload\":{\"key\":\""
                + key
                + "\",\"value\":\""
                + expectedDimension
                + "\"}}";

            Assert.True(MainForm.TryParseWebViewCommand(json, out ReadBoardUiCommand command));
            Assert.True(MainForm.TryCreateControlCenterIntent(command, out ControlCenterIntent intent));
            Assert.Equal((ControlCenterIntentKind)expectedKind, intent.Kind);
            Assert.Equal(expectedDimension, intent.Dimension);
        }

        [Theory]
        [InlineData("two-way", true)]
        [InlineData("two-way", false)]
        [InlineData("show-on-board", true)]
        [InlineData("show-on-board", false)]
        public void WebViewBooleanPreferenceShape_IsConvertedToTypedIntent(
            string key,
            bool expectedValue)
        {
            string json = "{\"type\":\"control.update\",\"payload\":{\"key\":\""
                + key
                + "\",\"value\":"
                + (expectedValue ? "true" : "false")
                + "}}";

            Assert.True(MainForm.TryParseWebViewCommand(json, out ReadBoardUiCommand command));
            Assert.True(MainForm.TryCreateControlCenterIntent(command, out ControlCenterIntent intent));
            Assert.Equal(
                key == "two-way"
                    ? ControlCenterIntentKind.SetTwoWaySync
                    : ControlCenterIntentKind.SetShowOnBoard,
                intent.Kind);
            Assert.Equal(expectedValue, intent.Enabled);
        }

        private sealed class RecordingSessionAdapter : IControlCenterSessionAdapter
        {
            public bool HasActiveSyncOperation { get; set; }
            public List<ControlCenterPreferences> Applied { get; } = new List<ControlCenterPreferences>();

            public void Apply(ControlCenterPreferences preferences)
            {
                Applied.Add(preferences.Clone());
            }
        }

        private sealed class RecordingPersistence : IControlCenterPreferencePersistence
        {
            public List<ControlCenterPreferences> Saved { get; } = new List<ControlCenterPreferences>();
            public Exception Failure { get; set; }

            public void Save(ControlCenterPreferences preferences)
            {
                Saved.Add(preferences.Clone());
                if (Failure != null)
                    throw Failure;
            }
        }
    }
}
