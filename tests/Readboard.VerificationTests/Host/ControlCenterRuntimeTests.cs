using System;
using System.Collections.Generic;
using System.IO;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class ControlCenterRuntimeTests
    {
        [Fact]
        public void StartupEngineValues_AreSessionStateWithLaunchWhitespaceNormalized()
        {
            ControlCenterSessionState state = ControlCenterSessionState.FromLaunchOptions(
                new LaunchOptions
                {
                    AiTime = " 5 ",
                    Playouts = " ",
                    FirstPolicy = "0"
                });

            Assert.Equal("5", state.AiTimeValue);
            Assert.Equal(string.Empty, state.PlayoutsValue);
            Assert.Equal("0", state.FirstPolicyValue);
            Assert.False(state.AutoPlayEnabled);
        }

        public static IEnumerable<object[]> IdentityEnablementCases()
        {
            yield return new object[] { (int)SyncMode.Fox, true };
            yield return new object[] { (int)SyncMode.FoxBackgroundPlace, true };
            yield return new object[] { (int)SyncMode.Yike, false };
            yield return new object[] { (int)SyncMode.Foreground, false };
        }

        [Theory]
        [MemberData(nameof(IdentityEnablementCases))]
        public void Snapshot_IdentityEnablementComesFromPlatformRuntime(
            int platformValue,
            bool expectedEnabled)
        {
            SyncMode platform = (SyncMode)platformValue;
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncMode = platform;
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(ControlCenterPreferences.FromConfig(config), new RuntimeTestEnvironment(), new RecordingPersistence(), new RejectingControlCenterActionAdapter());

            Assert.Equal(expectedEnabled, runtime.Snapshot.IdentityEnabled);
        }

        [Fact]
        public void PlatformIntent_UpdatesSessionPersistsOnceAndPublishesSavedSnapshot()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetPlatform(SyncMode.Yike));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.Equal(SyncMode.Yike, result.Snapshot.Platform);
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
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetPlatform(SyncMode.Fox));

            Assert.Equal(ControlCenterApplyOutcome.NoOp, result.Outcome);
            Assert.Empty(persistence.Saved);
            Assert.False(result.ShouldPublishSnapshot);
        }

        [Fact]
        public void ActiveSync_RejectsPlatformChangeAndReturnsAuthoritativeSnapshot()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RuntimeTestEnvironment session = new RuntimeTestEnvironment { HasActiveSyncOperation = true };
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetPlatform(SyncMode.Yike));

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.Equal(SyncMode.Fox, result.Snapshot.Platform);
            Assert.Equal(19, result.Snapshot.BoardWidth);
            Assert.False(result.Snapshot.ConfigurationEnabled);
            Assert.False(result.Snapshot.CustomBoardSizeEnabled);
            Assert.False(result.Snapshot.CustomBoardDimensionsEnabled);
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
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(platform
                ? ControlCenterIntent.SetPlatform((SyncMode)value)
                : ControlCenterIntent.SetBoardSize((ControlCenterBoardSizeKind)value));

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.Equal(SyncMode.Fox, result.Snapshot.Platform);
            Assert.Equal(ControlCenterBoardSizeKind.Preset19, result.Snapshot.BoardSizeKind);
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
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(ControlCenterPreferences.FromConfig(config), new RuntimeTestEnvironment(), new RecordingPersistence(), new RejectingControlCenterActionAdapter());

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
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(ControlCenterPreferences.FromConfig(config), session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(width
                ? ControlCenterIntent.SetCustomBoardWidth(17)
                : ControlCenterIntent.SetCustomBoardHeight(9));

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.Equal(ControlCenterBoardSizeKind.Preset19, result.Snapshot.BoardSizeKind);
            Assert.True(result.Snapshot.CustomBoardSizeEnabled);
            Assert.False(result.Snapshot.CustomBoardDimensionsEnabled);
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
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(width
                ? ControlCenterIntent.SetCustomBoardWidth(18)
                : ControlCenterIntent.SetCustomBoardHeight(10));

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.Equal(SyncMode.Fox, result.Snapshot.Platform);
            Assert.Equal(ControlCenterBoardSizeKind.Custom, result.Snapshot.BoardSizeKind);
            Assert.False(result.Snapshot.CustomBoardSizeEnabled);
            Assert.False(result.Snapshot.CustomBoardDimensionsEnabled);
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
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(ControlCenterPreferences.FromConfig(config), session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(width
                ? ControlCenterIntent.SetCustomBoardWidth(dimension)
                : ControlCenterIntent.SetCustomBoardHeight(dimension));

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.Equal(17, result.Snapshot.BoardWidth);
            Assert.Equal(9, result.Snapshot.BoardHeight);
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
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(ControlCenterPreferences.FromConfig(config), session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult selectCustom = runtime.Apply(
                ControlCenterIntent.SetBoardSize(ControlCenterBoardSizeKind.Custom));
            ControlCenterApplyResult setWidth = runtime.Apply(
                ControlCenterIntent.SetCustomBoardWidth(17));

            Assert.Equal(ControlCenterApplyOutcome.Changed, selectCustom.Outcome);
            Assert.Equal(ControlCenterApplyOutcome.Changed, setWidth.Outcome);
            Assert.Equal(17, setWidth.Snapshot.BoardWidth);
            Assert.Equal(19, setWidth.Snapshot.BoardHeight);
            Assert.Equal(2, persistence.Saved.Count);
            Assert.True(setWidth.Snapshot.CustomBoardSizeEnabled);
            Assert.True(setWidth.Snapshot.CustomBoardDimensionsEnabled);
        }

        [Fact]
        public void UnavailableCustomBoardIntent_IsRejectedWithoutPersistence()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetBoardSize(ControlCenterBoardSizeKind.Custom));

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.Equal(ControlCenterBoardSizeKind.Preset19, result.Snapshot.BoardSizeKind);
            Assert.False(result.Snapshot.CustomBoardSizeEnabled);
            Assert.False(result.Snapshot.CustomBoardDimensionsEnabled);
            Assert.Empty(persistence.Saved);
        }

        [Fact]
        public void PersistenceFailure_LeavesChangedSessionActiveAndMarksSnapshotNotSaved()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence { Failure = new IOException("disk full") };
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetPlatform(SyncMode.Yike));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.Equal(SyncMode.Yike, runtime.Snapshot.Platform);
            Assert.False(result.Snapshot.PreferencesSaved);
            Assert.Equal("disk full", result.Snapshot.PersistenceError);
            Assert.Single(persistence.Saved);
        }

        [Fact]
        public void LaterChange_RetriesPersistenceOnceWithoutBackgroundRetry()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence
            {
                Failure = new IOException("disk full")
            };
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, session, persistence, new RejectingControlCenterActionAdapter());

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
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetTwoWaySync(true));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.True(result.Snapshot.TwoWaySync);
            Assert.Single(persistence.Saved);
            Assert.True(persistence.Saved[0].TwoWaySync);
            Assert.True(result.Snapshot.TwoWaySyncEnabled);
        }

        [Fact]
        public void ShowOnBoardIntent_OnSupportedPlatformUpdatesSessionAndPersistsOnce()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetShowOnBoard(true));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.True(result.Snapshot.ShowOnBoard);
            Assert.True(result.Snapshot.ShowOnBoardEnabled);
            Assert.Single(persistence.Saved);
            Assert.True(persistence.Saved[0].ShowOnBoard);
        }

        [Fact]
        public void ShowOnBoardIntent_OnForegroundIsRejectedWithoutSessionOrPersistence()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncMode = SyncMode.Foreground;
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(ControlCenterPreferences.FromConfig(config), session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetShowOnBoard(true));

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.False(result.Snapshot.ShowOnBoard);
            Assert.False(result.Snapshot.ShowOnBoardEnabled);
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
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, new RuntimeTestEnvironment(), new RecordingPersistence(), new RejectingControlCenterActionAdapter());

            Assert.False(runtime.Snapshot.ShowOnBoard);
            Assert.False(runtime.Snapshot.ShowOnBoardEnabled);
        }

        [Fact]
        public void PlatformIntent_NormalizesShowOnBoardWhenSwitchingToForeground()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.ShowInBoard = true;
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(ControlCenterPreferences.FromConfig(config), session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetPlatform(SyncMode.Foreground));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.Equal(SyncMode.Foreground, result.Snapshot.Platform);
            Assert.False(result.Snapshot.ShowOnBoard);
            Assert.False(result.Snapshot.ShowOnBoardEnabled);
            Assert.Single(persistence.Saved);
            Assert.False(persistence.Saved[0].ShowOnBoard);
        }

        [Fact]
        public void SyncPreferences_CanChangeWhileSyncOperationIsActive()
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RuntimeTestEnvironment session = new RuntimeTestEnvironment { HasActiveSyncOperation = true };
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult twoWay = runtime.Apply(
                ControlCenterIntent.SetTwoWaySync(true));
            ControlCenterApplyResult show = runtime.Apply(
                ControlCenterIntent.SetShowOnBoard(true));

            Assert.Equal(ControlCenterApplyOutcome.Changed, twoWay.Outcome);
            Assert.Equal(ControlCenterApplyOutcome.Changed, show.Outcome);
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
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence
            {
                Failure = new IOException("disk full")
            };
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetShowOnBoard(true));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.True(runtime.Snapshot.ShowOnBoard);
            Assert.False(result.Snapshot.PreferencesSaved);
            Assert.Equal("disk full", result.Snapshot.PersistenceError);
            Assert.Single(persistence.Saved);
        }

        [Fact]
        public void AutoPlayEnabled_IsSessionStateAndDoesNotPersist()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncBoth = true;
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(config);
            ControlCenterSessionState sessionState = new ControlCenterSessionState
            {
                AiTimeValue = "5",
                PlayoutsValue = "1000",
                FirstPolicyValue = "200"
            };
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, sessionState, session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetAutoPlayEnabled(true));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.True(result.Snapshot.AutoPlayEnabled);
            Assert.True(result.Snapshot.AutoPlayToggleEnabled);
            Assert.True(result.Snapshot.ManualColorEnabled);
            Assert.True(result.Snapshot.FoxAutoColorEnabled);
            Assert.True(result.Snapshot.MoveModeEnabled);
            Assert.True(result.Snapshot.AiTimeEnabled);
            Assert.True(result.Snapshot.PlayoutsEnabled);
            Assert.True(result.Snapshot.FirstPolicyEnabled);
            Assert.Equal("5", result.Snapshot.AiTimeValue);
            Assert.Equal("1000", result.Snapshot.PlayoutsValue);
            Assert.Equal("200", result.Snapshot.FirstPolicyValue);
            Assert.Empty(persistence.Saved);
        }

        [Theory]
        [InlineData(0, "black", false)]
        [InlineData(1, "white", false)]
        [InlineData(0, "black", true)]
        [InlineData(1, "white", true)]
        public void AutoPlayEnableCycle_RequiresExplicitManualColor(int modeValue, string color, bool disableTwoWay)
        {
            AutoPlayColorMode mode = (AutoPlayColorMode)modeValue;
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncBoth = true;
            config.AutoPlayColorMode = mode;
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(
                ControlCenterPreferences.FromConfig(config), new RuntimeTestEnvironment(),
                persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult enabled = runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true));
            Assert.Null(enabled.Snapshot.PlayColor);
            Assert.False(enabled.Snapshot.AutoPlayColorResolution.IsKnown);

            ControlCenterApplyResult selected = runtime.Apply(ControlCenterIntent.SetAutoPlayColor(mode));
            Assert.Equal(ControlCenterApplyOutcome.Changed, selected.Outcome);
            Assert.Equal(color, selected.Snapshot.PlayColor);
            Assert.Equal(ControlCenterApplyOutcome.NoOp,
                runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true)).Outcome);
            Assert.Equal(color, runtime.Snapshot.PlayColor);

            runtime.Apply(disableTwoWay
                ? ControlCenterIntent.SetTwoWaySync(false)
                : ControlCenterIntent.SetAutoPlayEnabled(false));
            Assert.Null(runtime.Snapshot.PlayColor);
            if (disableTwoWay)
                runtime.Apply(ControlCenterIntent.SetTwoWaySync(true));
            runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true));
            Assert.Null(runtime.Snapshot.PlayColor);
            Assert.False(runtime.Snapshot.AutoPlayColorResolution.IsKnown);

            Assert.Equal(ControlCenterApplyOutcome.Changed,
                runtime.Apply(ControlCenterIntent.SetAutoPlayColor(mode)).Outcome);
            Assert.Equal(color, runtime.Snapshot.PlayColor);
            if (!disableTwoWay)
                Assert.Empty(persistence.Saved);
        }

        [Fact]
        public void AutoPlayColorAndMoveMode_ArePersistentPreferences()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncBoth = true;
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                config);
            ControlCenterSessionState sessionState = new ControlCenterSessionState
            {
                AutoPlayEnabled = true
            };
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, sessionState, new RuntimeTestEnvironment(), persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult color = runtime.Apply(
                ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.ManualWhite));
            ControlCenterApplyResult moveMode = runtime.Apply(
                ControlCenterIntent.SetAutoPlayMoveMode(AutoPlayMoveMode.GenmoveAnalyze));

            Assert.Equal(ControlCenterApplyOutcome.Changed, color.Outcome);
            Assert.Equal(AutoPlayColorMode.ManualWhite, color.Snapshot.AutoPlayColorMode);
            Assert.Equal("white", color.Snapshot.PlayColor);
            Assert.Equal(ControlCenterApplyOutcome.Changed, moveMode.Outcome);
            Assert.Equal(AutoPlayMoveMode.GenmoveAnalyze, moveMode.Snapshot.AutoPlayMoveMode);
            Assert.False(moveMode.Snapshot.FirstPolicyEnabled);
            Assert.Equal(2, persistence.Saved.Count);
            Assert.Equal(AutoPlayColorMode.ManualWhite, persistence.Saved[0].AutoPlayColorMode);
            Assert.Equal(AutoPlayMoveMode.GenmoveAnalyze, persistence.Saved[1].AutoPlayMoveMode);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void SameValueUnavailableIntent_IsRejectedAndPublishesAuthoritativeState(int kind)
        {
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                AppConfig.CreateDefault("220430", "TEST"));
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterIntent intent = kind == 0
                ? ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.ManualBlack)
                : kind == 1
                    ? ControlCenterIntent.SetAutoPlayMoveMode(AutoPlayMoveMode.FirstCandidate)
                    : ControlCenterIntent.SetAiTime(string.Empty);

            ControlCenterApplyResult result = runtime.Apply(intent);

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.True(result.ShouldPublishSnapshot);
            Assert.Empty(persistence.Saved);
        }


        [Fact]
        public void EngineConditionIntents_AreSessionOnlyAndRespectMoveModeEnablement()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncBoth = true;
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                config);
            ControlCenterSessionState sessionState = new ControlCenterSessionState
            {
                AutoPlayEnabled = true,
                AiTimeValue = "5",
                PlayoutsValue = string.Empty,
                FirstPolicyValue = "200"
            };
            RuntimeTestEnvironment session = new RuntimeTestEnvironment();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, sessionState, session, persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult aiTime = runtime.Apply(ControlCenterIntent.SetAiTime("7"));
            ControlCenterApplyResult playouts = runtime.Apply(ControlCenterIntent.SetPlayouts("1200"));
            ControlCenterApplyResult moveMode = runtime.Apply(
                ControlCenterIntent.SetAutoPlayMoveMode(AutoPlayMoveMode.GenmoveAnalyze));
            ControlCenterApplyResult firstPolicy = runtime.Apply(ControlCenterIntent.SetFirstPolicy("300"));

            Assert.Equal(ControlCenterApplyOutcome.Changed, aiTime.Outcome);
            Assert.Equal(ControlCenterApplyOutcome.Changed, playouts.Outcome);
            Assert.Equal(ControlCenterApplyOutcome.Changed, moveMode.Outcome);
            Assert.Equal(ControlCenterApplyOutcome.Rejected, firstPolicy.Outcome);
            Assert.Equal("7", runtime.Snapshot.AiTimeValue);
            Assert.Equal("1200", runtime.Snapshot.PlayoutsValue);
            Assert.Equal("200", runtime.Snapshot.FirstPolicyValue);
            Assert.False(runtime.Snapshot.FirstPolicyEnabled);
            Assert.Single(persistence.Saved);
        }

        [Fact]
        public void AutoPlayPreferencePersistenceFailure_LeavesChoiceActiveAndMarksNotSaved()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncBoth = true;
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(
                config);
            ControlCenterSessionState sessionState = new ControlCenterSessionState
            {
                AutoPlayEnabled = true
            };
            RecordingPersistence persistence = new RecordingPersistence
            {
                Failure = new IOException("disk full")
            };
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, sessionState, new RuntimeTestEnvironment(), persistence, new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetAutoPlayMoveMode(AutoPlayMoveMode.GenmoveAnalyze));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.Equal(AutoPlayMoveMode.GenmoveAnalyze, result.Snapshot.AutoPlayMoveMode);
            Assert.False(result.Snapshot.PreferencesSaved);
            Assert.Equal("disk full", result.Snapshot.PersistenceError);
            Assert.Single(persistence.Saved);
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(true, true, true)]
        public void Snapshot_CanSendAutoPlayCommandRequiresKeepSyncTwoWayAndAutoPlay(
            bool keepSync,
            bool twoWaySync,
            bool autoPlayEnabled)
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncBoth = twoWaySync;
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(
                ControlCenterPreferences.FromConfig(config),
                new ControlCenterSessionState { AutoPlayEnabled = autoPlayEnabled },
                new RuntimeTestEnvironment(),
                new RecordingPersistence(),
                new RejectingControlCenterActionAdapter());

            Assert.Equal(
                keepSync && twoWaySync && autoPlayEnabled,
                runtime.Snapshot.CanSendAutoPlayCommand(keepSync));
        }


        [Fact]
        public void ApplyResults_ExposeChangedRejectedAndInvalidPublicationSemantics()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncMode = SyncMode.Foreground;
            ControlCenterPreferences initial = ControlCenterPreferences.FromConfig(config);
            ControlCenterRuntime runtime = RuntimeTestFactory.Create(initial, new RuntimeTestEnvironment(), new RecordingPersistence(), new RejectingControlCenterActionAdapter());

            ControlCenterApplyResult changed = runtime.Apply(
                ControlCenterIntent.SetTwoWaySync(true));
            ControlCenterApplyResult noOp = runtime.Apply(
                ControlCenterIntent.SetTwoWaySync(true));
            ControlCenterApplyResult rejected = runtime.Apply(
                ControlCenterIntent.SetShowOnBoard(true));
            ControlCenterApplyResult invalid = runtime.Apply(
                ControlCenterIntent.SetPlatform((SyncMode)99));

            Assert.True(changed.ShouldPublishSnapshot);
            Assert.False(noOp.ShouldPublishSnapshot);
            Assert.True(rejected.ShouldPublishSnapshot);
            Assert.True(invalid.ShouldPublishSnapshot);
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

        [Theory]
        [InlineData("auto-play", "true", (int)ControlCenterIntentKind.SetAutoPlayEnabled)]
        [InlineData("color", "\"white\"", (int)ControlCenterIntentKind.SetAutoPlayColor)]
        [InlineData("placement", "\"engine\"", (int)ControlCenterIntentKind.SetAutoPlayMoveMode)]
        [InlineData("ai-time", "\"5\"", (int)ControlCenterIntentKind.SetAiTime)]
        [InlineData("playouts", "\"1000\"", (int)ControlCenterIntentKind.SetPlayouts)]
        [InlineData("first-policy", "\"200\"", (int)ControlCenterIntentKind.SetFirstPolicy)]
        public void WebViewAutoplayShape_IsConvertedToTypedIntent(
            string key,
            string jsonValue,
            int expectedKind)
        {
            string json = "{\"type\":\"control.update\",\"payload\":{\"key\":\""
                + key
                + "\",\"value\":"
                + jsonValue
                + "}}";

            Assert.True(MainForm.TryParseWebViewCommand(json, out ReadBoardUiCommand command));
            Assert.True(MainForm.TryCreateControlCenterIntent(command, out ControlCenterIntent intent));
            Assert.Equal((ControlCenterIntentKind)expectedKind, intent.Kind);
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
