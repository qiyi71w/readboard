using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class ControlCenterAuthorizationTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NewCycle_RequiresManualChoice_WhileRepeatedEnableRetainsIt(bool endWithTwoWay)
        {
            var h = new Harness(platform: SyncMode.Foreground);
            h.EnableManual();
            Assert.Equal(new[] { "play>black>0 0 0" }, h.Transport.Lines);
            Assert.Equal(ControlCenterApplyOutcome.NoOp,
                h.Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true)).Outcome);
            Assert.Equal("black", h.Runtime.Snapshot.PlayColor);
            h.Runtime.RequestAutoPlay();
            Assert.Equal(2, h.Transport.Lines.Count(line => line == "play>black>0 0 0"));

            h.Runtime.Apply(endWithTwoWay
                ? ControlCenterIntent.SetTwoWaySync(false)
                : ControlCenterIntent.SetAutoPlayEnabled(false));
            Assert.Null(h.Runtime.Snapshot.PlayColor);
            Assert.False(h.Runtime.Snapshot.AutoPlayEnabled);
            Assert.Contains("stopAutoPlay", h.Transport.Lines);
            if (endWithTwoWay)
            {
                Assert.Equal(ControlCenterApplyOutcome.Rejected,
                    h.Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true)).Outcome);
                h.Runtime.Apply(ControlCenterIntent.SetTwoWaySync(true));
            }
            h.Transport.Lines.Clear();
            h.Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true));
            h.Runtime.RequestAutoPlay();
            Assert.Empty(h.Transport.Lines);
            h.Runtime.Apply(ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.ManualWhite));
            Assert.Equal(new[] { "play>white>0 0 0" }, h.Transport.Lines);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(false)]
        [InlineData(true)]
        public void PlatformChange_ProjectsFreshTargetValidityBeforeSaving(bool? targetValid)
        {
            var h = new Harness(keepSync: false);
            h.Runtime.ApplyObservation(new ControlCenterSessionObservation(
                h.Runtime.CaptureSessionObservationGeneration()).WithTargetWindowValid(targetValid != true));
            h.Environment.TargetWindowValid = targetValid;
            h.Environment.OnProject = () => Assert.Equal(targetValid, h.Runtime.Snapshot.TargetWindowValid);
            h.OnSave = () => Assert.Equal(targetValid, h.Runtime.Snapshot.TargetWindowValid);

            h.Runtime.Apply(ControlCenterIntent.SetPlatform(SyncMode.Foreground));

            Assert.Equal(targetValid, h.Runtime.Snapshot.TargetWindowValid);
            Assert.Empty(h.Transport.Lines);
        }

        [Theory]
        [InlineData("")]
        [InlineData("self")]
        public void UnsupportedFoxAuto_IsRejectedWithoutOpeningIdentityOrAuthorizing(string saved)
        {
            var h = new Harness(platform: SyncMode.Foreground, saved: saved);
            h.Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true));
            ControlCenterApplyResult result = h.Runtime.Apply(ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.FoxAuto));
            Assert.Equal(ControlCenterApplyOutcome.Rejected, result.Outcome);
            Assert.False(h.Runtime.IdentitySnapshot.Open);
            h.Runtime.RequestAutoPlay();
            Assert.Null(h.Runtime.Snapshot.PlayColor);
            Assert.Empty(h.Transport.Lines);
            Assert.Equal(0, h.Environment.PlayerReads);
        }

        [Fact]
        public void AutomaticModeOnUnsupportedPlatform_DoesNotFallBackToHistoricalManualChoice()
        {
            var h = new Harness(saved: "self");
            h.EnableManual();
            h.Runtime.Apply(ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.FoxAuto));
            h.Runtime.Apply(ControlCenterIntent.SetPlatform(SyncMode.Foreground));
            h.Transport.Lines.Clear();
            h.Runtime.RequestAutoPlay();
            Assert.Equal(AutoPlayColorMode.FoxAuto, h.Runtime.CurrentSessionState.SelectedAutoPlayColorMode);
            Assert.Equal(AutoPlayColorStatus.UnsupportedPlatform, h.Runtime.Snapshot.AutoPlayColorResolution.Status);
            Assert.Null(h.Runtime.Snapshot.PlayColor);
            Assert.Empty(h.Transport.Lines);
        }

        [Fact]
        public void FirstFoxAuto_OpensIdentityWithoutReplacingChoice_AndCancelPreservesIt()
        {
            var h = new Harness();
            h.EnableManual();
            h.Transport.Lines.Clear();
            ControlCenterApplyResult result = h.Runtime.Apply(ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.FoxAuto));
            Assert.Equal(ControlCenterApplyOutcome.IdentitySelectionOpened, result.Outcome);
            Assert.True(result.ShouldPublishSnapshot);
            Assert.True(h.Runtime.IdentitySnapshot.Open);
            Assert.Equal("black", h.Runtime.Snapshot.PlayColor);
            Assert.Equal(AutoPlayColorMode.ManualBlack, h.Runtime.CurrentSessionState.SelectedAutoPlayColorMode);
            h.Runtime.SelectIdentity(h.Candidate());
            h.Runtime.CancelIdentity();
            Assert.False(h.Runtime.IdentitySnapshot.Open);
            Assert.Equal(string.Empty, h.Runtime.IdentitySnapshot.EffectiveIdentitySignature);
            Assert.Equal("black", h.Runtime.Snapshot.PlayColor);
            Assert.Empty(h.Transport.Lines);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ConfirmIdentity_IssuesExactlyOnce_ForFirstAndExistingAutomaticSelection(bool alreadyAutomatic)
        {
            var h = new Harness(saved: alreadyAutomatic ? "other" : "");
            h.Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true));
            if (alreadyAutomatic)
            {
                h.Runtime.Apply(ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.FoxAuto));
                h.Runtime.OpenIdentity();
            }
            else
                h.Runtime.Apply(ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.FoxAuto));
            h.Transport.Lines.Clear();
            FoxIdentitySelectionResult result = h.Runtime.ConfirmIdentity(h.Candidate(), false);
            Assert.True(result.Accepted);
            Assert.False(h.Runtime.IdentitySnapshot.Open);
            Assert.Equal("self", h.Runtime.IdentitySnapshot.EffectiveIdentitySignature);
            Assert.Equal(AutoPlayColorMode.FoxAuto, h.Runtime.CurrentSessionState.SelectedAutoPlayColorMode);
            Assert.Equal("black", h.Runtime.Snapshot.PlayColor);
            Assert.Equal(new[] { "play>black>0 0 0" }, h.Transport.Lines);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FirstConfirmation_DoesNotSupplementModeRequest_WhenItsGateEmitsNoWire(bool initiallySynchronizing)
        {
            var h = new Harness(keepSync: initiallySynchronizing);
            h.Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true));
            h.Runtime.Apply(ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.FoxAuto));
            string candidate = h.Candidate();
            h.Environment.Players = FoxMatchBarReading.Empty;
            h.OnSave = () =>
            {
                h.Environment.Players = Players("black");
                h.Environment.UtcNow = h.Environment.UtcNow.AddMilliseconds(1000);
                if (!initiallySynchronizing)
                    h.Coordinator.BeginKeepSync();
            };
            h.Transport.Lines.Clear();

            h.Runtime.ConfirmIdentity(candidate, false);

            Assert.Equal("black", h.Runtime.Snapshot.PlayColor);
            Assert.Empty(h.Transport.Lines);
            h.Runtime.RequestAutoPlay();
            Assert.Equal(new[] { "play>black>0 0 0" }, h.Transport.Lines);
        }

        [Fact]
        public void IdentitySaveFailure_UsesNewProcessIdentity_WhileClearFailureRetainsSavedIdentity()
        {
            var h = new Harness(saved: "other");
            h.Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true));
            h.Runtime.Apply(ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.FoxAuto));
            h.Runtime.OpenIdentity();
            h.IdentityPersistence.SaveFailure = new IOException("identity save failed");
            h.Transport.Lines.Clear();
            FoxIdentitySelectionResult save = h.Runtime.ConfirmIdentity(h.Candidate(), true);
            Assert.Equal(FoxIdentitySelectionActionOutcome.PersistenceFailed, save.Outcome);
            Assert.Same(h.IdentityPersistence.SaveFailure, save.PersistenceError);
            Assert.False(h.Runtime.IdentitySnapshot.Open);
            Assert.Equal("self", h.Runtime.IdentitySnapshot.CurrentProcessIdentitySignature);
            Assert.Equal("other", h.Runtime.IdentitySnapshot.SavedIdentitySignature);
            Assert.Equal(new[] { "play>black>0 0 0" }, h.Transport.Lines);

            h.IdentityPersistence.ClearFailure = new IOException("identity clear failed");
            h.Transport.Lines.Clear();
            FoxIdentitySelectionResult clear = h.Runtime.ClearSavedIdentity();
            Assert.Same(h.IdentityPersistence.ClearFailure, clear.PersistenceError);
            Assert.Equal("other", h.Runtime.IdentitySnapshot.SavedIdentitySignature);
            Assert.Equal("self", h.Runtime.IdentitySnapshot.EffectiveIdentitySignature);
            Assert.Equal("black", h.Runtime.Snapshot.PlayColor);
            Assert.Empty(h.Transport.Lines);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ClearSavedIdentity_RevokesOnlyWhenThereIsNoProcessIdentity(bool hasProcessIdentity)
        {
            var h = new Harness(saved: "self");
            h.EnableAutomatic();
            if (hasProcessIdentity)
            {
                h.Runtime.OpenIdentity();
                h.Runtime.ConfirmIdentity(h.Candidate(), false);
            }
            h.Transport.Lines.Clear();
            h.Runtime.ClearSavedIdentity();
            Assert.False(h.Runtime.IdentitySnapshot.HasSavedIdentity);
            Assert.Equal(hasProcessIdentity ? "self" : string.Empty, h.Runtime.IdentitySnapshot.EffectiveIdentitySignature);
            Assert.Equal(hasProcessIdentity ? "black" : null, h.Runtime.Snapshot.PlayColor);
            h.Runtime.RequestAutoPlay();
            Assert.Equal(hasProcessIdentity ? new[] { "play>black>0 0 0" } : Array.Empty<string>(), h.Transport.Lines);
        }

        [Fact]
        public void PreferenceFailure_RetainsCommittedChoiceAndWire_AndMarksUnsaved()
        {
            var h = new Harness();
            h.Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true));
            h.Events.Clear();
            h.SaveFailure = new IOException("preferences failed");
            ControlCenterApplyResult result = h.Runtime.Apply(ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.ManualWhite));
            Assert.Equal(ControlCenterApplyOutcome.Changed, result.Outcome);
            Assert.Equal("white", h.Runtime.Snapshot.PlayColor);
            Assert.False(h.Runtime.Snapshot.PreferencesSaved);
            Assert.Equal(new[] { "project", "play>white>0 0 0", "save" }, h.Events);
        }

        [Theory]
        [InlineData("room")]
        [InlineData("handle")]
        [InlineData("binding")]
        [InlineData("state")]
        [InlineData("explicit")]
        public void WindowEvidenceChange_RevokesOldColorWithoutEndingCycle_AndRequiresFreshPlayers(string change)
        {
            var h = new Harness(saved: "self");
            h.EnableAutomatic();
            Assert.Equal("black", h.Runtime.Snapshot.PlayColor);
            int reads = h.Environment.PlayerReads;
            h.Transport.Lines.Clear();
            h.Environment.Players = Players("white");
            if (change == "room") h.Environment.Context.RoomToken = "room-2";
            if (change == "handle") h.Environment.Handle = new IntPtr(84);
            if (change == "binding") h.Environment.BindingInvalidated = true;
            if (change == "state") h.Environment.Context.LiveRoomState = FoxLiveRoomState.Watching;
            if (change == "explicit") h.Runtime.InvalidateWindowEvidence();
            h.Runtime.RefreshWindowContext();
            Assert.Null(h.Runtime.Snapshot.PlayColor);
            Assert.True(h.Runtime.Snapshot.AutoPlayEnabled);
            Assert.Equal(AutoPlayColorMode.FoxAuto, h.Runtime.CurrentSessionState.SelectedAutoPlayColorMode);
            h.Runtime.RefreshAutoPlayColor();
            Assert.Equal(reads + 1, h.Environment.PlayerReads);
            Assert.Equal(change == "state" ? null : "white", h.Runtime.Snapshot.PlayColor);
            Assert.Empty(h.Transport.Lines);
            h.Runtime.RequestAutoPlay();
            Assert.Equal(change == "state" ? Array.Empty<string>() : new[] { "play>white>0 0 0" }, h.Transport.Lines);
        }

        [Theory]
        [InlineData("current")]
        [InlineData("total")]
        [InlineData("end")]
        [InlineData("title")]
        public void EveryRecordContextField_InvalidatesCachedPlayers(string field)
        {
            var h = new Harness(saved: "self");
            h.Environment.Context = new FoxWindowContext
            {
                Kind = FoxWindowKind.RecordView, RecordCurrentMove = 10, RecordTotalMove = 20,
                RecordAtEnd = false, TitleFingerprint = "record-A"
            };
            h.EnableAutomatic();
            int reads = h.Environment.PlayerReads;
            h.Environment.Players = Players("white");
            if (field == "current") h.Environment.Context.RecordCurrentMove = 11;
            if (field == "total") h.Environment.Context.RecordTotalMove = 21;
            if (field == "end") h.Environment.Context.RecordAtEnd = true;
            if (field == "title") h.Environment.Context.TitleFingerprint = "record-B";
            h.Runtime.RefreshAutoPlayColor();
            Assert.Equal(reads + 1, h.Environment.PlayerReads);
            Assert.Null(h.Runtime.Snapshot.PlayColor);
            h.Runtime.RequestAutoPlay();
            Assert.Empty(h.Transport.Lines);
        }

        [Fact]
        public void UnknownPlayers_RetryAt1000Milliseconds_WhileKnownPlayersRemainCached()
        {
            var h = new Harness(saved: "self");
            h.Environment.Players = FoxMatchBarReading.Empty;
            h.EnableAutomatic();
            Assert.Null(h.Runtime.Snapshot.PlayColor);
            int reads = h.Environment.PlayerReads;
            h.Environment.Players = Players("white");
            h.Environment.UtcNow = h.Environment.UtcNow.AddMilliseconds(999);
            h.Runtime.RefreshAutoPlayColor();
            Assert.Equal(reads, h.Environment.PlayerReads);
            Assert.Null(h.Runtime.Snapshot.PlayColor);
            h.Environment.UtcNow = h.Environment.UtcNow.AddMilliseconds(1);
            h.Runtime.RefreshAutoPlayColor();
            Assert.Equal(reads + 1, h.Environment.PlayerReads);
            Assert.Equal("white", h.Runtime.Snapshot.PlayColor);
            h.Environment.Players = Players("black");
            h.Environment.UtcNow = h.Environment.UtcNow.AddDays(1);
            h.Runtime.RefreshAutoPlayColor();
            Assert.Equal(reads + 1, h.Environment.PlayerReads);
            Assert.Equal("white", h.Runtime.Snapshot.PlayColor);
            Assert.Empty(h.Transport.Lines);
        }

        [Fact]
        public void SameRoomNewCycle_AndPlatformRoundTrip_RequireNewEvidence()
        {
            var h = new Harness(saved: "self");
            h.EnableAutomatic();
            h.Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(false));
            h.Environment.Players = Players("white");
            h.Transport.Lines.Clear();
            h.Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true));
            Assert.Equal(new[] { "play>white>0 0 0" }, h.Transport.Lines);
            h.Environment.Players = Players("black");
            h.Runtime.Apply(ControlCenterIntent.SetPlatform(SyncMode.Foreground));
            h.Transport.Lines.Clear();
            h.Runtime.RequestAutoPlay();
            Assert.Null(h.Runtime.Snapshot.PlayColor);
            Assert.Empty(h.Transport.Lines);
            h.Runtime.Apply(ControlCenterIntent.SetPlatform(SyncMode.Fox));
            h.Runtime.RequestAutoPlay();
            Assert.Equal("black", h.Runtime.Snapshot.PlayColor);
            Assert.Equal(new[] { "play>black>0 0 0" }, h.Transport.Lines);
        }

        [Fact]
        public void NewRoomObservation_RevokesAcceptedColor_AndOldGenerationCannotRestoreIt()
        {
            var h = new Harness(saved: "self");
            h.EnableAutomatic();
            FoxWindowContext previous = h.Runtime.CurrentSessionState.FoxWindowContext;
            long generation = h.Runtime.BeginSessionObservationGeneration();
            h.Environment.Context.RoomToken = "room-2";
            h.Environment.Players = FoxMatchBarReading.Empty;
            ControlCenterSessionObservationApplyResult current = h.Runtime.ApplyObservation(
                new ControlCenterSessionObservation(generation).WithFoxWindowContext(h.Environment.Context));
            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Applied, current.Outcome);
            Assert.Null(h.Runtime.Snapshot.PlayColor);
            ControlCenterSessionObservationApplyResult stale = h.Runtime.ApplyObservation(
                new ControlCenterSessionObservation(0).WithFoxWindowContext(previous));
            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Stale, stale.Outcome);
            Assert.Equal("room-2", h.Runtime.Snapshot.FoxWindowContext.RoomToken);
            h.Transport.Lines.Clear();
            h.Runtime.RequestAutoPlay();
            Assert.Null(h.Runtime.Snapshot.PlayColor);
            Assert.Empty(h.Transport.Lines);
        }

        [Fact]
        public void ZeroHandleAndForcedIdentityRefresh_DoNotReuseBoundWindowPlayers()
        {
            var h = new Harness(saved: "self");
            h.EnableAutomatic();
            int reads = h.Environment.PlayerReads;
            h.Environment.Players = Players("white");
            h.Runtime.OpenIdentity();
            Assert.Equal(reads + 1, h.Environment.PlayerReads);
            h.Runtime.CancelIdentity();
            h.Environment.Handle = IntPtr.Zero;
            h.Transport.Lines.Clear();
            h.Runtime.RequestAutoPlay();
            Assert.Null(h.Runtime.Snapshot.PlayColor);
            Assert.Equal(reads + 1, h.Environment.PlayerReads);
            Assert.Empty(h.Transport.Lines);
        }

        [Fact]
        public void Initialization_ProjectsWithoutWire_AndCompletionDoesNotIssue()
        {
            var h = new Harness(completeInitialization: false);
            h.Runtime.Apply(ControlCenterIntent.SetShowOnBoard(true));
            h.EnableManual();
            h.Runtime.Apply(ControlCenterIntent.SetAiTime("7"));
            h.Runtime.RequestAutoPlay();
            h.Runtime.ReplayStartupProtocolState();
            h.Runtime.ResendSyncSessionState();
            Assert.Empty(h.Transport.Lines);
            Assert.Equal("black", h.Runtime.Snapshot.PlayColor);
            Assert.True(h.Runtime.Snapshot.ShowOnBoard);
            Assert.Equal(new[] { "project", "save", "project", "project", "project" }, h.Events);
            h.Runtime.CompleteInitialization();
            Assert.Empty(h.Transport.Lines);
            h.Events.Clear();
            h.Runtime.RequestAutoPlay();
            Assert.Equal(new[] { "play>black>7 0 0" }, h.Transport.Lines);
        }

        [Fact]
        public void StartupReplay_AndResend_PreserveProtocolOrder()
        {
            var h = new Harness(completeInitialization: false);
            h.Runtime.Apply(ControlCenterIntent.SetShowOnBoard(true));
            h.EnableManual();
            h.Runtime.Apply(ControlCenterIntent.SetAiTime("7"));
            h.Runtime.Apply(ControlCenterIntent.SetPlayouts("1200"));
            h.Runtime.Apply(ControlCenterIntent.SetFirstPolicy("300"));
            h.Runtime.CompleteInitialization();
            h.Runtime.ReplayStartupProtocolState();
            Assert.Equal(new[]
            {
                "bothSync", "foreFoxWithInBoard", "timechanged 7", "playoutschanged 1200",
                "firstchanged 300", "play>black>7 1200 300"
            }, h.Transport.Lines);
            h.Transport.Lines.Clear();
            h.Runtime.ResendSyncSessionState();
            Assert.Equal(new[] { "sync", "play>black>7 1200 300" }, h.Transport.Lines);
        }

        [Fact]
        public void ResendWhileStopped_IsEntirelyInert_AndReplaySkipsEmptyParameters()
        {
            var h = new Harness(keepSync: false);
            h.EnableManual();
            int reads = h.Environment.WindowReads;
            h.Runtime.ResendSyncSessionState();
            Assert.Empty(h.Transport.Lines);
            Assert.Equal(reads, h.Environment.WindowReads);
            h.Runtime.ReplayStartupProtocolState();
            Assert.Equal(new[] { "bothSync" }, h.Transport.Lines);
        }

        [Fact]
        public void TwoWayDisplayAndEngineChanges_ExecuteEffectsBeforePreferenceSave()
        {
            var h = new Harness();
            h.EnableManual();
            h.Environment.ShowInBoardHint = true;
            h.Events.Clear();
            h.Runtime.Apply(ControlCenterIntent.SetShowOnBoard(true));
            Assert.Equal(new[] { "project", "foreFoxWithInBoard", "hint", "save" }, h.Events);
            h.Events.Clear();
            h.Runtime.Apply(ControlCenterIntent.SetTwoWaySync(false));
            Assert.Equal(new[] { "project", "nobothSync", "notForeFoxWithInBoard", "sync", "stopAutoPlay", "save" }, h.Events);
            h.Events.Clear();
            h.Runtime.Apply(ControlCenterIntent.SetTwoWaySync(true));
            Assert.Equal(new[] { "project", "bothSync", "foreFoxWithInBoard", "sync", "save" }, h.Events);
            h.Events.Clear();
            h.Runtime.Apply(ControlCenterIntent.SetShowOnBoard(false));
            Assert.Equal(new[] { "project", "notForeFoxWithInBoard", "notinboard", "save" }, h.Events);
            h.Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true));
            h.Events.Clear();
            h.Runtime.Apply(ControlCenterIntent.SetAiTime("7"));
            h.Runtime.Apply(ControlCenterIntent.SetPlayouts("1200"));
            h.Runtime.Apply(ControlCenterIntent.SetFirstPolicy("300"));
            Assert.Equal(new[] { "project", "timechanged 7", "project", "playoutschanged 1200", "project", "firstchanged 300" }, h.Events);
        }

        [Fact]
        public void MoveModeChange_ReissuesCurrentAuthorizationBeforeSavingPreference()
        {
            var h = new Harness();
            h.EnableManual();
            h.Events.Clear();
            h.Runtime.Apply(ControlCenterIntent.SetAutoPlayMoveMode(AutoPlayMoveMode.GenmoveAnalyze));
            Assert.Equal(new[] { "project", "play>black>0 0 0 gma", "save" }, h.Events);
            Assert.Equal("black", h.Runtime.Snapshot.PlayColor);
            Assert.False(h.Runtime.Snapshot.FirstPolicyEnabled);
        }

        [Fact]
        public void UnexpectedProjectionFailure_PropagatesAfterCommit_WithoutPersisting()
        {
            var h = new Harness();
            var error = new InvalidOperationException("native projection failed");
            h.Environment.OnProject = () => throw error;
            h.Events.Clear();
            Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
                h.Runtime.Apply(ControlCenterIntent.SetShowOnBoard(true))));
            Assert.True(h.Runtime.Snapshot.ShowOnBoard);
            Assert.DoesNotContain("save", h.Events);
            Assert.Empty(h.Transport.Lines);
        }

        private static FoxMatchBarReading Players(string selfColor)
        {
            return new FoxMatchBarReading(new[]
            {
                new FoxPlayerListEntry(selfColor == "white" ? "self" : "other", null),
                new FoxPlayerListEntry(selfColor == "black" ? "self" : "other", null)
            });
        }

        private sealed class Harness : IControlCenterPreferencePersistence
        {
            public Harness(SyncMode platform = SyncMode.Fox, string saved = "", bool keepSync = true, bool completeInitialization = true)
            {
                AppConfig config = AppConfig.CreateDefault("220430", "TEST");
                config.SyncMode = platform;
                config.SyncBoth = true;
                config.ShowInBoard = false;
                config.AutoPlayColorMode = AutoPlayColorMode.ManualBlack;
                config.AutoPlayMoveMode = AutoPlayMoveMode.FirstCandidate;
                Environment.Players = Players("black");
                Environment.OnProject = () => Events.Add("project");
                Environment.OnHint = () => Events.Add("hint");
                Transport.OnSend = line => Events.Add(line);
                IdentityPersistence.Saved = saved;
                Coordinator = new SyncSessionCoordinator(Transport, new LegacyProtocolAdapter());
                Runtime = new ControlCenterRuntime(ControlCenterPreferences.FromConfig(config), new ControlCenterSessionState(),
                    Environment, this, new RejectingControlCenterActionAdapter(), Coordinator,
                    new FoxIdentitySelection(IdentityPersistence));
                Runtime.ProjectCurrentState();
                if (keepSync) Coordinator.BeginKeepSync();
                if (completeInitialization) Runtime.CompleteInitialization();
                Events.Clear();
            }

            public RuntimeTestEnvironment Environment { get; } = new RuntimeTestEnvironment();
            public RuntimeIdentityPersistence IdentityPersistence { get; } = new RuntimeIdentityPersistence();
            public RuntimeRecordingTransport Transport { get; } = new RuntimeRecordingTransport();
            public List<string> Events { get; } = new List<string>();
            public SyncSessionCoordinator Coordinator { get; }
            public ControlCenterRuntime Runtime { get; }
            public Exception SaveFailure { get; set; }
            public Action OnSave { get; set; }
            public string Candidate() { return Runtime.IdentitySnapshot.Candidates.Single(candidate => candidate.Signature == "self").Id; }
            public void EnableManual()
            {
                Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true));
                Runtime.Apply(ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.ManualBlack));
            }
            public void EnableAutomatic()
            {
                Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(true));
                Runtime.Apply(ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.FoxAuto));
            }
            public void Save(ControlCenterPreferences preferences)
            {
                Events.Add("save");
                OnSave?.Invoke();
                if (SaveFailure != null) throw SaveFailure;
            }
        }
    }
}
