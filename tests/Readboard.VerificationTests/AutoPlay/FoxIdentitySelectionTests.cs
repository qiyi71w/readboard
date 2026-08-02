using System;
using System.Collections.Generic;
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

        private static FoxIdentityCandidate Candidate(string id, string signature)
        {
            return new FoxIdentityCandidate(id, id, signature, null);
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
