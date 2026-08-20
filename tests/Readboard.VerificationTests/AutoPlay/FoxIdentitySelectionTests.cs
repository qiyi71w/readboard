using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.AutoPlay
{
    public sealed class FoxIdentitySelectionTests
    {
        [Fact]
        public void Cancel_FirstAutomaticSelection_RestoresPreviousManualModeWithoutPlayReevaluation()
        {
            RecordingPersistence persistence = new RecordingPersistence();
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(Array.Empty<FoxIdentityCandidate>(), true, AutoPlayColorMode.ManualWhite);
            FoxIdentitySelectionResult result = selection.Cancel();

            Assert.True(result.RestorePreviousManualMode);
            Assert.Equal(AutoPlayColorMode.ManualWhite, result.RestoredManualMode);
            Assert.False(result.RequiresAutomaticColorReevaluation);
            Assert.False(result.CurrentProcessIdentityChanged);
            Assert.Equal(string.Empty, selection.EffectiveIdentitySignature);
            Assert.Empty(persistence.SavedSignatures);
        }

        [Fact]
        public void Cancel_ManuallyOpenedSelection_LeavesExistingIdentityAndModeUnchanged()
        {
            RecordingPersistence persistence = new RecordingPersistence("saved-signature");
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(
                new[] { Candidate("saved", "saved-signature") },
                false,
                AutoPlayColorMode.ManualBlack);
            FoxIdentitySelectionResult result = selection.Cancel();

            Assert.False(result.RestorePreviousManualMode);
            Assert.False(result.RequiresAutomaticColorReevaluation);
            Assert.Equal("saved-signature", selection.EffectiveIdentitySignature);
            Assert.Equal("saved-signature", persistence.CurrentSignature);
            Assert.False(result.CurrentProcessIdentityChanged);
        }

        [Fact]
        public void UseOnce_InstallsProcessIdentityAcrossFoxRoomsWithoutPersistence()
        {
            RecordingPersistence persistence = new RecordingPersistence("saved-signature");
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(new[] { Candidate("first", "temporary-signature") }, true, AutoPlayColorMode.ManualBlack);
            selection.Select("first");
            FoxIdentitySelectionResult result = selection.UseOnce();

            Assert.Equal(FoxIdentitySelectionActionOutcome.Applied, result.Outcome);
            Assert.Equal("temporary-signature", selection.CurrentProcessIdentitySignature);
            Assert.Equal("temporary-signature", selection.EffectiveIdentitySignature);
            Assert.Empty(persistence.SavedSignatures);
            Assert.True(result.RequiresAutomaticColorReevaluation);

            FoxIdentitySelectionSnapshot roomTwo = selection.Open(
                new[] { Candidate("room-two", "temporary-signature") },
                false,
                AutoPlayColorMode.ManualBlack);

            Assert.Equal("room-two", roomTwo.SelectedCandidateId);
            Assert.Equal("temporary-signature", selection.EffectiveIdentitySignature);
        }

        [Fact]
        public void SaveAndUse_PersistsFingerprintAndInstallsCurrentIdentity()
        {
            RecordingPersistence persistence = new RecordingPersistence("old-signature");
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(new[] { Candidate("new", "new-signature") }, false, AutoPlayColorMode.ManualBlack);
            selection.Select("new");
            FoxIdentitySelectionResult result = selection.SaveAndUse();

            Assert.Equal(FoxIdentitySelectionActionOutcome.Applied, result.Outcome);
            Assert.True(result.PersistedIdentityChanged);
            Assert.Equal("new-signature", selection.CurrentProcessIdentitySignature);
            Assert.Equal("new-signature", selection.EffectiveIdentitySignature);
            Assert.Equal("new-signature", persistence.CurrentSignature);
            Assert.Single(persistence.SavedSignatures);
        }

        [Fact]
        public void SaveAndUse_RestartReadsSameFoxNickname()
        {
            AppConfig config = AppConfig.CreateDefault("p", "machine");
            ConfigBackedPersistence persistence = new ConfigBackedPersistence(config);
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(new[] { Candidate("me", "叶落メ让子") }, true, AutoPlayColorMode.ManualBlack);
            selection.Select("me");
            FoxIdentitySelectionResult result = selection.SaveAndUse();

            Assert.Equal(FoxIdentitySelectionActionOutcome.Applied, result.Outcome);
            Assert.Equal("叶落メ让子", config.FoxAutoPlayNickname);
            Assert.Equal(string.Empty, config.FoxAutoPlayNicknameSignature);

            FoxIdentitySelection restarted = new FoxIdentitySelection(new ConfigBackedPersistence(config));

            Assert.Equal("叶落メ让子", restarted.SavedIdentitySignature);
            Assert.Equal("叶落メ让子", restarted.EffectiveIdentitySignature);
            Assert.True(restarted.Snapshot.HasSavedIdentity);
        }

        [Fact]
        public void Load_LegacyGlyphOnly_LeavesIdentityUnconfigured()
        {
            string glyph = CreateLegacyGlyphIdentity();
            AppConfig config = AppConfig.CreateDefault("p", "machine");
            config.FoxAutoPlayNicknameSignature = glyph;
            config.FoxAutoPlayNickname = string.Empty;

            FoxIdentitySelection selection = new FoxIdentitySelection(new ConfigBackedPersistence(config));

            Assert.Equal(string.Empty, selection.SavedIdentitySignature);
            Assert.Equal(string.Empty, selection.EffectiveIdentitySignature);
            Assert.False(selection.Snapshot.HasSavedIdentity);

            AutoPlayColorResolution resolution = FoxAutoPlayColorResolver.Resolve(
                AutoPlayColorMode.FoxAuto,
                SyncMode.Fox,
                selection.EffectiveIdentitySignature,
                PlayingRoom("room-1"),
                AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack));

            Assert.False(resolution.IsKnown);
            Assert.Equal(AutoPlayColorStatus.Unconfigured, resolution.Status);
        }

        [Fact]
        public void WriteSavedFoxNickname_LegacyGlyph_DoesNotConfigureIdentity()
        {
            string glyph = CreateLegacyGlyphIdentity();
            AppConfig config = AppConfig.CreateDefault("p", "machine");
            config.FoxAutoPlayNicknameSignature = glyph;

            AppConfigFoxIdentityPersistence.WriteSavedFoxNickname(config, glyph);

            Assert.Equal(string.Empty, config.FoxAutoPlayNickname);
            Assert.Equal(string.Empty, config.FoxAutoPlayNicknameSignature);
            Assert.Equal(string.Empty, AppConfigFoxIdentityPersistence.ReadSavedFoxNickname(config));
        }

        [Fact]
        public void ClearSaved_DropsDiskFoxNicknameOnly()
        {
            AppConfig config = AppConfig.CreateDefault("p", "machine");
            config.AutoPlayColorMode = AutoPlayColorMode.FoxAuto;
            ConfigBackedPersistence persistence = new ConfigBackedPersistence(config);
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(new[] { Candidate("me", "叶落メ让子") }, true, AutoPlayColorMode.ManualBlack);
            selection.Select("me");
            selection.SaveAndUse();
            selection.Open(new[] { Candidate("me", "叶落メ让子") }, false, AutoPlayColorMode.ManualBlack);

            FoxIdentitySelectionResult result = selection.ClearSaved();

            Assert.Equal(FoxIdentitySelectionActionOutcome.Applied, result.Outcome);
            Assert.Equal(string.Empty, config.FoxAutoPlayNickname);
            Assert.Equal(string.Empty, config.FoxAutoPlayNicknameSignature);
            Assert.Equal(AutoPlayColorMode.FoxAuto, config.AutoPlayColorMode);
            Assert.Equal("叶落メ让子", selection.CurrentProcessIdentitySignature);
            Assert.Equal("叶落メ让子", selection.EffectiveIdentitySignature);
            Assert.False(result.RequiresAutomaticColorReevaluation);
        }

        [Fact]
        public void CurrentProcessIdentity_TakesPriorityOverStartupSavedIdentity()
        {
            RecordingPersistence persistence = new RecordingPersistence("saved-signature");
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(new[] { Candidate("temporary", "temporary-signature") }, true, AutoPlayColorMode.ManualBlack);
            selection.Select("temporary");
            selection.UseOnce();

            FoxIdentitySelectionSnapshot snapshot = selection.Open(
                new[]
                {
                    Candidate("saved", "saved-signature"),
                    Candidate("temporary", "temporary-signature")
                },
                false,
                AutoPlayColorMode.ManualBlack);

            Assert.Equal("temporary", snapshot.SelectedCandidateId);
            Assert.Equal("saved", snapshot.SavedCandidateId);
            Assert.Equal("temporary-signature", selection.EffectiveIdentitySignature);
        }

        [Fact]
        public void ClearSaved_ChangesOnlyPersistedIdentityAndRetainsCurrentProcessEvidence()
        {
            RecordingPersistence persistence = new RecordingPersistence("saved-signature");
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(new[] { Candidate("temporary", "temporary-signature") }, true, AutoPlayColorMode.ManualBlack);
            selection.Select("temporary");
            selection.UseOnce();
            selection.Open(
                new[] { Candidate("temporary", "temporary-signature") },
                false,
                AutoPlayColorMode.ManualBlack);

            FoxIdentitySelectionResult result = selection.ClearSaved();

            Assert.Equal(FoxIdentitySelectionActionOutcome.Applied, result.Outcome);
            Assert.True(result.PersistedIdentityChanged);
            Assert.Equal(string.Empty, persistence.CurrentSignature);
            Assert.Equal("temporary-signature", selection.CurrentProcessIdentitySignature);
            Assert.Equal("temporary-signature", selection.EffectiveIdentitySignature);
            Assert.False(result.RequiresAutomaticColorReevaluation);
            Assert.Empty(selection.Snapshot.SavedCandidateId ?? string.Empty);
            Assert.Equal("temporary", selection.Snapshot.SelectedCandidateId);
        }

        [Fact]
        public void SaveFailure_StillInstallsCurrentIdentityAndRetainsPreviousSavedIdentity()
        {
            RecordingPersistence persistence = new RecordingPersistence("old-signature")
            {
                SaveFailure = new IOException("disk full")
            };
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(new[] { Candidate("new", "new-signature") }, false, AutoPlayColorMode.ManualBlack);
            selection.Select("new");
            FoxIdentitySelectionResult result = selection.SaveAndUse();

            Assert.Equal(FoxIdentitySelectionActionOutcome.PersistenceFailed, result.Outcome);
            Assert.Same(persistence.SaveFailure, result.PersistenceError);
            Assert.Equal("new-signature", selection.CurrentProcessIdentitySignature);
            Assert.Equal("new-signature", selection.EffectiveIdentitySignature);
            Assert.Equal("old-signature", persistence.CurrentSignature);
            Assert.False(result.PersistedIdentityChanged);
        
            Assert.True(result.PersistenceAttempted);
            Assert.False(result.Snapshot.Open);
        }

        [Fact]
        public void ClearFailure_RetainsSavedIdentityAndCurrentSelectionState()
        {
            RecordingPersistence persistence = new RecordingPersistence("saved-signature")
            {
                ClearFailure = new IOException("read-only")
            };
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(new[] { Candidate("saved", "saved-signature") }, false, AutoPlayColorMode.ManualBlack);
            FoxIdentitySelectionResult result = selection.ClearSaved();

            Assert.Equal(FoxIdentitySelectionActionOutcome.PersistenceFailed, result.Outcome);
            Assert.Same(persistence.ClearFailure, result.PersistenceError);
            Assert.True(result.PersistenceAttempted);
            Assert.Equal("saved-signature", persistence.CurrentSignature);
            Assert.Equal("saved-signature", selection.EffectiveIdentitySignature);
            Assert.True(result.Snapshot.Open);
            Assert.Equal("saved", result.Snapshot.SavedCandidateId);
        }

        [Fact]
        public void RepeatedMismatch_DoesNotDeleteSavedIdentity()
        {
            RecordingPersistence persistence = new RecordingPersistence("saved-signature");
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(new[] { Candidate("other", "other-signature") }, false, AutoPlayColorMode.ManualBlack);
            FoxIdentitySelectionResult result = selection.Cancel();

            Assert.Equal(FoxIdentitySelectionActionOutcome.Applied, result.Outcome);
            Assert.Equal("saved-signature", persistence.CurrentSignature);
            Assert.Equal("saved-signature", selection.EffectiveIdentitySignature);
            Assert.Empty(persistence.SavedSignatures);
        }

        [Fact]
        public void RoomChange_ClearsRoomDerivedStateButRetainsIdentityEvidence()
        {
            RecordingPersistence persistence = new RecordingPersistence("saved-signature");
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(new[] { Candidate("current", "process-signature") }, true, AutoPlayColorMode.ManualBlack);
            selection.Select("current");
            selection.UseOnce();

            FoxWindowContext roomOne = PlayingRoom("room-1");
            FoxIdentityRoomSnapshot roomOneSnapshot = selection.BeginRoomContext(roomOne);
            FoxIdentityRecognitionResult recognition = selection.ApplyRoomRecognition(
                roomOneSnapshot.OperationGeneration,
                roomOne,
                SyncMode.Fox,
                true,
                AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack));
            Assert.Equal(FoxIdentityRecognitionApplyOutcome.Applied, recognition.Outcome);
            Assert.True(recognition.Snapshot.DerivedAuthorization.IsKnown);

            FoxIdentityRoomSnapshot roomTwoSnapshot = selection.BeginRoomContext(PlayingRoom("room-2"));

            Assert.Equal("process-signature", selection.EffectiveIdentitySignature);
            Assert.Equal("saved-signature", persistence.CurrentSignature);
            Assert.False(roomTwoSnapshot.PlayerRowMatched);
            Assert.False(roomTwoSnapshot.ColorResult.IsKnown);
            Assert.False(roomTwoSnapshot.DerivedAuthorization.IsKnown);
        }

        [Fact]
        public void LateRecognitionFromPreviousRoom_IsRejectedWithoutRestoringAuthorization()
        {
            RecordingPersistence persistence = new RecordingPersistence("saved-signature");
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);
            FoxWindowContext roomOne = PlayingRoom("room-1");
            long roomOneGeneration = selection.BeginRoomContext(roomOne).OperationGeneration;

            selection.BeginRoomContext(PlayingRoom("room-2"));
            FoxIdentityRecognitionResult late = selection.ApplyRoomRecognition(
                roomOneGeneration,
                roomOne,
                SyncMode.Fox,
                true,
                AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack));

            Assert.Equal(FoxIdentityRecognitionApplyOutcome.Stale, late.Outcome);
            Assert.False(selection.RoomSnapshot.DerivedAuthorization.IsKnown);
        }

        [Fact]
        public void ReinvalidatedSameRoom_RejectsOlderGeneration()
        {
            FoxIdentitySelection selection = new FoxIdentitySelection(
                new RecordingPersistence("saved-signature"));
            FoxWindowContext room = PlayingRoom("room-1");
            long oldGeneration = selection.BeginRoomContext(room).OperationGeneration;

            selection.ClearRoomRecognition();
            FoxIdentityRecognitionResult late = selection.ApplyRoomRecognition(
                oldGeneration,
                room,
                SyncMode.Fox,
                true,
                AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack));

            Assert.Equal(FoxIdentityRecognitionApplyOutcome.Stale, late.Outcome);
            Assert.False(selection.RoomSnapshot.DerivedAuthorization.IsKnown);
        }

        [Fact]
        public void CurrentRoomUniqueMatchAndColor_RebuildsDerivedAuthorization()
        {
            FoxIdentitySelection selection = new FoxIdentitySelection(
                new RecordingPersistence("saved-signature"));
            FoxWindowContext room = PlayingRoom("room-1");
            FoxIdentityRoomSnapshot roomSnapshot = selection.BeginRoomContext(room);

            FoxIdentityRecognitionResult result = selection.ApplyRoomRecognition(
                roomSnapshot.OperationGeneration,
                room,
                SyncMode.Fox,
                true,
                AutoPlayColorResolution.Known("white", AutoPlayColorStatus.RecognizedWhite));

            Assert.Equal(FoxIdentityRecognitionApplyOutcome.Applied, result.Outcome);
            Assert.True(result.Snapshot.PlayerRowMatched);
            Assert.Equal("white", result.Snapshot.ColorResult.PlayColor);
            Assert.Equal("white", result.Snapshot.DerivedAuthorization.PlayColor);
        }

        [Fact]
        public void AmbiguousPlayerMatch_RemainsFailClosedEvenWithDetectedColor()
        {
            FoxIdentitySelection selection = new FoxIdentitySelection(
                new RecordingPersistence("saved-signature"));
            FoxWindowContext room = PlayingRoom("room-1");
            FoxIdentityRoomSnapshot roomSnapshot = selection.BeginRoomContext(room);

            FoxIdentityRecognitionResult result = selection.ApplyRoomRecognition(
                roomSnapshot.OperationGeneration,
                room,
                SyncMode.Fox,
                false,
                AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack));

            Assert.Equal(FoxIdentityRecognitionApplyOutcome.Applied, result.Outcome);
            Assert.True(result.Snapshot.ColorResult.IsKnown);
            Assert.False(result.Snapshot.DerivedAuthorization.IsKnown);
            Assert.Null(result.Snapshot.DerivedAuthorization.PlayColor);
        }

        [Theory]
        [InlineData((int)FoxWindowKind.LiveRoom, (int)FoxLiveRoomState.Watching, (int)SyncMode.Fox)]
        [InlineData((int)FoxWindowKind.RecordView, (int)FoxLiveRoomState.Unknown, (int)SyncMode.Fox)]
        [InlineData((int)FoxWindowKind.LiveRoom, (int)FoxLiveRoomState.Playing, (int)SyncMode.Yike)]
        [InlineData((int)FoxWindowKind.LiveRoom, (int)FoxLiveRoomState.Playing, (int)SyncMode.Foreground)]
        public void UnsupportedRoomOrPlatform_RemainsFailClosed(
            int kindValue,
            int stateValue,
            int syncModeValue)
        {
            FoxWindowKind kind = (FoxWindowKind)kindValue;
            FoxLiveRoomState state = (FoxLiveRoomState)stateValue;
            SyncMode syncMode = (SyncMode)syncModeValue;
            FoxIdentitySelection selection = new FoxIdentitySelection(
                new RecordingPersistence("saved-signature"));
            FoxWindowContext context = new FoxWindowContext
            {
                Kind = kind,
                LiveRoomState = state,
                RoomToken = "room-1"
            };
            FoxIdentityRoomSnapshot roomSnapshot = selection.BeginRoomContext(context);

            FoxIdentityRecognitionResult result = selection.ApplyRoomRecognition(
                roomSnapshot.OperationGeneration,
                context,
                syncMode,
                true,
                AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack));

            Assert.Equal(FoxIdentityRecognitionApplyOutcome.Applied, result.Outcome);
            Assert.False(result.Snapshot.DerivedAuthorization.IsKnown);
            Assert.Null(result.Snapshot.DerivedAuthorization.PlayColor);
        }

        [Fact]
        public void UnknownColor_RemainsFailClosedAfterUniquePlayerMatch()
        {
            FoxIdentitySelection selection = new FoxIdentitySelection(
                new RecordingPersistence("saved-signature"));
            FoxWindowContext room = PlayingRoom("room-1");
            FoxIdentityRoomSnapshot roomSnapshot = selection.BeginRoomContext(room);

            FoxIdentityRecognitionResult result = selection.ApplyRoomRecognition(
                roomSnapshot.OperationGeneration,
                room,
                SyncMode.Fox,
                true,
                AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown));

            Assert.Equal(FoxIdentityRecognitionApplyOutcome.Applied, result.Outcome);
            Assert.True(result.Snapshot.PlayerRowMatched);
            Assert.False(result.Snapshot.ColorResult.IsKnown);
            Assert.False(result.Snapshot.DerivedAuthorization.IsKnown);
            Assert.Null(result.Snapshot.DerivedAuthorization.PlayColor);
        }

        private static FoxWindowContext PlayingRoom(string roomToken)
        {
            return new FoxWindowContext
            {
                Kind = FoxWindowKind.LiveRoom,
                LiveRoomState = FoxLiveRoomState.Playing,
                RoomToken = roomToken
            };
        }

        private static FoxIdentityCandidate Candidate(string id, string signature)
        {
            return new FoxIdentityCandidate(id, SemanticMessage.Create("WebView_candidateRowNumber", id), signature, null);
        }

        private static string CreateLegacyGlyphIdentity()
        {
            using (Bitmap bitmap = new Bitmap(96, 24))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(Color.FromArgb(20, 20, 20), 2))
            {
                graphics.Clear(Color.FromArgb(245, 245, 240));
                graphics.DrawLine(pen, 8, 6, 72, 6);
                graphics.DrawLine(pen, 8, 6, 8, 18);
                graphics.DrawLine(pen, 20, 18, 76, 18);
                graphics.DrawLine(pen, 42, 4, 54, 20);
                string glyph = FoxPlayerNicknameSignature.FromBitmap(bitmap).Serialize();
                Assert.False(string.IsNullOrWhiteSpace(glyph));
                Assert.True(FoxPlayerNicknameSignature.FromString(glyph).IsValid);
                return glyph;
            }
        }

        private sealed class ConfigBackedPersistence : IFoxIdentityPersistence
        {
            private readonly AppConfig config;

            public ConfigBackedPersistence(AppConfig config)
            {
                this.config = config;
            }

            public string LoadSavedIdentitySignature()
            {
                return AppConfigFoxIdentityPersistence.ReadSavedFoxNickname(config);
            }

            public void SaveIdentitySignature(string signature)
            {
                AppConfigFoxIdentityPersistence.WriteSavedFoxNickname(config, signature);
            }

            public void ClearSavedIdentity()
            {
                AppConfigFoxIdentityPersistence.ClearSavedFoxNickname(config);
            }
        }

        private sealed class RecordingPersistence : IFoxIdentityPersistence
        {
            public RecordingPersistence(string signature = "")
            {
                CurrentSignature = signature;
            }

            public string CurrentSignature { get; private set; }
            public Exception SaveFailure { get; set; }
            public Exception ClearFailure { get; set; }
            public List<string> SavedSignatures { get; } = new List<string>();
            public string LoadSavedIdentitySignature()
            {
                return CurrentSignature;
            }

            public void SaveIdentitySignature(string signature)
            {
                if (SaveFailure != null)
                    throw SaveFailure;
                CurrentSignature = signature;
                SavedSignatures.Add(signature);
            }

            public void ClearSavedIdentity()
            {
                if (ClearFailure != null)
                    throw ClearFailure;
                CurrentSignature = string.Empty;
            }
        }
    }
}
