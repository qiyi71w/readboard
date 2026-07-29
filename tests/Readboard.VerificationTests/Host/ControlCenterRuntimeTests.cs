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
