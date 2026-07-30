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
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                ControlCenterPreferences.FromConfig(config),
                new RecordingSessionAdapter(),
                new RecordingPersistence());

            Assert.Equal(expectedEnabled, runtime.Snapshot.IdentityEnabled);
        }

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
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                initial,
                sessionState,
                session,
                persistence);

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
            Assert.True(session.AppliedSessions[0].AutoPlayEnabled);
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
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                initial,
                sessionState,
                new RecordingSessionAdapter(),
                persistence);

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
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(initial, session, persistence);

            ControlCenterIntent intent = kind == 0
                ? ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.ManualBlack)
                : kind == 1
                    ? ControlCenterIntent.SetAutoPlayMoveMode(AutoPlayMoveMode.FirstCandidate)
                    : ControlCenterIntent.SetAiTime(string.Empty);

            ControlCenterApplyResult result = runtime.Apply(intent);

            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.True(result.ShouldPublishSnapshot);
            Assert.Empty(session.Applied);
            Assert.Empty(persistence.Saved);
        }

        [Fact]
        public void FoxAutoPlayObservation_IsKnownOnlyForFoxAndCurrentRecognition()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncBoth = true;
            config.AutoPlayColorMode = AutoPlayColorMode.FoxAuto;
            ControlCenterSessionState sessionState = new ControlCenterSessionState
            {
                AutoPlayEnabled = true,
                FoxAutoPlayNicknameSignature = "sig",
                FoxWindowContext = new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    LiveRoomState = FoxLiveRoomState.Playing
                },
                DetectedAutoPlayColor = AutoPlayColorResolution.Known(
                    "black",
                    AutoPlayColorStatus.RecognizedBlack)
            };
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                ControlCenterPreferences.FromConfig(config),
                sessionState,
                new RecordingSessionAdapter(),
                new RecordingPersistence());

            Assert.Equal("black", runtime.Snapshot.PlayColor);
            Assert.Equal(AutoPlayColorStatus.RecognizedBlack, runtime.Snapshot.AutoPlayColorStatus);

            runtime.UpdateAutoPlayObservation(
                "sig",
                sessionState.FoxWindowContext,
                AutoPlayColorResolution.Unknown(AutoPlayColorStatus.NicknameNotMatched));
            Assert.Null(runtime.Snapshot.PlayColor);
            Assert.Equal(AutoPlayColorStatus.NicknameNotMatched, runtime.Snapshot.AutoPlayColorStatus);

            ControlCenterApplyResult platform = runtime.Apply(
                ControlCenterIntent.SetPlatform(SyncMode.Yike));
            Assert.Equal(ControlCenterApplyOutcome.Changed, platform.Outcome);
            Assert.Null(platform.Snapshot.PlayColor);
            Assert.Equal(AutoPlayColorStatus.UnsupportedPlatform, platform.Snapshot.AutoPlayColorStatus);
        }

        [Fact]
        public void DisablingAutoPlay_ClearsRecognitionBeforeNextEnable()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.SyncBoth = true;
            config.AutoPlayColorMode = AutoPlayColorMode.FoxAuto;
            ControlCenterSessionState sessionState = new ControlCenterSessionState
            {
                AutoPlayEnabled = true,
                FoxAutoPlayNicknameSignature = "sig",
                FoxWindowContext = new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    LiveRoomState = FoxLiveRoomState.Playing
                },
                DetectedAutoPlayColor = AutoPlayColorResolution.Known(
                    "white",
                    AutoPlayColorStatus.RecognizedWhite)
            };
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                ControlCenterPreferences.FromConfig(config),
                sessionState,
                new RecordingSessionAdapter(),
                new RecordingPersistence());

            ControlCenterApplyResult disabled = runtime.Apply(
                ControlCenterIntent.SetAutoPlayEnabled(false));
            ControlCenterApplyResult enabled = runtime.Apply(
                ControlCenterIntent.SetAutoPlayEnabled(true));

            Assert.False(disabled.Snapshot.AutoPlayEnabled);
            Assert.False(disabled.Snapshot.ManualColorEnabled);
            Assert.True(enabled.Snapshot.AutoPlayEnabled);
            Assert.Null(enabled.Snapshot.PlayColor);
            Assert.Equal(AutoPlayColorStatus.ColorUnknown, enabled.Snapshot.AutoPlayColorStatus);
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
            RecordingSessionAdapter session = new RecordingSessionAdapter();
            RecordingPersistence persistence = new RecordingPersistence();
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                initial,
                sessionState,
                session,
                persistence);

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
            Assert.Equal(3, session.Applied.Count);
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
            ControlCenterRuntime runtime = new ControlCenterRuntime(
                initial,
                sessionState,
                new RecordingSessionAdapter(),
                persistence);

            ControlCenterApplyResult result = runtime.Apply(
                ControlCenterIntent.SetAutoPlayMoveMode(AutoPlayMoveMode.GenmoveAnalyze));

            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.Equal(AutoPlayMoveMode.GenmoveAnalyze, result.Snapshot.AutoPlayMoveMode);
            Assert.False(result.Snapshot.PreferencesSaved);
            Assert.Equal("disk full", result.Snapshot.PersistenceError);
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

        private sealed class RecordingSessionAdapter : IControlCenterSessionAdapter
        {
            public bool HasActiveSyncOperation { get; set; }
            public List<ControlCenterPreferences> Applied { get; } = new List<ControlCenterPreferences>();
            public List<ControlCenterSessionState> AppliedSessions { get; } = new List<ControlCenterSessionState>();

            public void Apply(
                ControlCenterPreferences preferences,
                ControlCenterSessionState sessionState)
            {
                Applied.Add(preferences.Clone());
                AppliedSessions.Add(sessionState.Clone());
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
