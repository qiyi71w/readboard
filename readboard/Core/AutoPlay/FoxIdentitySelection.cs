using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace readboard
{
    internal interface IFoxIdentityPersistence
    {
        string LoadSavedIdentitySignature();

        void SaveIdentitySignature(string signature);

        void ClearSavedIdentity();
    }

    internal sealed class AppConfigFoxIdentityPersistence : IFoxIdentityPersistence
    {
        public string LoadSavedIdentitySignature()
        {
            AppConfig config = Program.CurrentConfig;
            return config == null ? string.Empty : config.FoxAutoPlayNicknameSignature ?? string.Empty;
        }

        public void SaveIdentitySignature(string signature)
        {
            AppConfig updatedConfig = RequireCurrentConfig().Clone();
            updatedConfig.FoxAutoPlayNickname = string.Empty;
            updatedConfig.FoxAutoPlayNicknameSignature = signature ?? string.Empty;
            Program.SaveAppConfig(updatedConfig);
        }

        public void ClearSavedIdentity()
        {
            AppConfig updatedConfig = RequireCurrentConfig().Clone();
            updatedConfig.FoxAutoPlayNickname = string.Empty;
            updatedConfig.FoxAutoPlayNicknameSignature = string.Empty;
            Program.SaveAppConfig(updatedConfig);
        }

        private static AppConfig RequireCurrentConfig()
        {
            AppConfig config = Program.CurrentConfig;
            if (config == null)
                throw new InvalidOperationException("ReadBoard configuration is not initialized.");
            return config;
        }
    }

    internal sealed class FoxIdentityCandidate
    {
        public FoxIdentityCandidate(
            string id,
            string label,
            string signature,
            string previewUrl)
        {
            Id = id ?? string.Empty;
            Label = label ?? string.Empty;
            Signature = signature ?? string.Empty;
            PreviewUrl = previewUrl;
        }

        public string Id { get; private set; }
        public string Label { get; private set; }
        public string Signature { get; private set; }
        public string PreviewUrl { get; private set; }

        public FoxIdentityCandidate Clone()
        {
            return new FoxIdentityCandidate(Id, Label, Signature, PreviewUrl);
        }
    }

    internal enum FoxIdentitySelectionActionOutcome
    {
        NoOp = 0,
        Applied = 1,
        Rejected = 2,
        PersistenceFailed = 3
    }

    internal sealed class FoxIdentitySelectionSnapshot
    {
        internal FoxIdentitySelectionSnapshot(
            bool open,
            IList<FoxIdentityCandidate> candidates,
            string selectedCandidateId,
            string savedCandidateId,
            string currentProcessIdentitySignature,
            string savedIdentitySignature)
        {
            List<FoxIdentityCandidate> copies = new List<FoxIdentityCandidate>();
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                    copies.Add(candidates[i].Clone());
            }

            Open = open;
            Candidates = new ReadOnlyCollection<FoxIdentityCandidate>(copies);
            SelectedCandidateId = selectedCandidateId;
            SavedCandidateId = savedCandidateId;
            CurrentProcessIdentitySignature = currentProcessIdentitySignature ?? string.Empty;
            SavedIdentitySignature = savedIdentitySignature ?? string.Empty;
        }

        public bool Open { get; private set; }
        public IReadOnlyList<FoxIdentityCandidate> Candidates { get; private set; }
        public string SelectedCandidateId { get; private set; }
        public string SavedCandidateId { get; private set; }
        public bool HasSavedIdentity { get { return !string.IsNullOrWhiteSpace(SavedIdentitySignature); } }
        public string CurrentProcessIdentitySignature { get; private set; }
        public string SavedIdentitySignature { get; private set; }
        public string EffectiveIdentitySignature
        {
            get
            {
                return !string.IsNullOrWhiteSpace(CurrentProcessIdentitySignature)
                    ? CurrentProcessIdentitySignature
                    : SavedIdentitySignature;
            }
        }
    }

    internal sealed class FoxIdentitySelectionResult
    {
        internal FoxIdentitySelectionResult(
            FoxIdentitySelectionActionOutcome outcome,
            FoxIdentitySelectionSnapshot snapshot,
            bool currentProcessIdentityChanged,
            bool persistedIdentityChanged,
            bool requiresAutomaticColorReevaluation,
            bool restorePreviousManualMode,
            AutoPlayColorMode restoredManualMode,
            bool persistenceAttempted,
            Exception persistenceError)
        {
            Outcome = outcome;
            Snapshot = snapshot;
            CurrentProcessIdentityChanged = currentProcessIdentityChanged;
            PersistedIdentityChanged = persistedIdentityChanged;
            RequiresAutomaticColorReevaluation = requiresAutomaticColorReevaluation;
            RestorePreviousManualMode = restorePreviousManualMode;
            RestoredManualMode = restoredManualMode;
            PersistenceAttempted = persistenceAttempted;
            PersistenceError = persistenceError;
        }

        public FoxIdentitySelectionActionOutcome Outcome { get; private set; }
        public FoxIdentitySelectionSnapshot Snapshot { get; private set; }
        public bool CurrentProcessIdentityChanged { get; private set; }
        public bool PersistedIdentityChanged { get; private set; }
        public bool RequiresAutomaticColorReevaluation { get; private set; }
        public bool RestorePreviousManualMode { get; private set; }
        public AutoPlayColorMode RestoredManualMode { get; private set; }
        public bool PersistenceAttempted { get; private set; }
        public Exception PersistenceError { get; private set; }

        public bool Accepted
        {
            get
            {
                return Outcome == FoxIdentitySelectionActionOutcome.Applied
                    || Outcome == FoxIdentitySelectionActionOutcome.PersistenceFailed;
            }
        }
    }

    internal sealed class FoxIdentitySelection
    {
        private readonly IFoxIdentityPersistence persistence;
        private readonly List<FoxIdentityCandidate> candidates = new List<FoxIdentityCandidate>();
        private string savedIdentitySignature;
        private string currentProcessIdentitySignature = string.Empty;
        private string selectedCandidateId;
        private bool open;
        private bool restorePreviousManualModeOnCancel;
        private AutoPlayColorMode previousManualMode = AutoPlayColorMode.ManualBlack;

        public FoxIdentitySelection(IFoxIdentityPersistence persistence)
        {
            this.persistence = persistence ?? throw new ArgumentNullException("persistence");
            savedIdentitySignature = NormalizeSignature(persistence.LoadSavedIdentitySignature());
        }

        public string CurrentProcessIdentitySignature
        {
            get { return currentProcessIdentitySignature; }
        }

        public string SavedIdentitySignature
        {
            get { return savedIdentitySignature; }
        }

        public string EffectiveIdentitySignature
        {
            get
            {
                return !string.IsNullOrWhiteSpace(currentProcessIdentitySignature)
                    ? currentProcessIdentitySignature
                    : savedIdentitySignature;
            }
        }

        public FoxIdentitySelectionSnapshot Snapshot
        {
            get { return CreateSnapshot(); }
        }

        public bool IsFirstAutomaticSelectionPending
        {
            get { return open && restorePreviousManualModeOnCancel; }
        }

        public FoxIdentitySelectionSnapshot Open(
            IEnumerable<FoxIdentityCandidate> availableCandidates,
            bool firstAutomaticSelection,
            AutoPlayColorMode previousManualMode)
        {
            candidates.Clear();
            if (availableCandidates != null)
            {
                foreach (FoxIdentityCandidate candidate in availableCandidates)
                {
                    if (candidate == null
                        || string.IsNullOrWhiteSpace(candidate.Id)
                        || string.IsNullOrWhiteSpace(candidate.Signature)
                        || ContainsCandidateId(candidate.Id))
                        continue;
                    candidates.Add(candidate.Clone());
                }
            }

            open = true;
            selectedCandidateId = FindPreferredCandidateId();
            restorePreviousManualModeOnCancel = firstAutomaticSelection
                && string.IsNullOrWhiteSpace(currentProcessIdentitySignature)
                && string.IsNullOrWhiteSpace(savedIdentitySignature);
            this.previousManualMode = NormalizeManualMode(previousManualMode);
            return CreateSnapshot();
        }

        public FoxIdentitySelectionResult Select(string candidateId)
        {
            if (!open || string.IsNullOrWhiteSpace(candidateId) || !ContainsCandidateId(candidateId))
                return CreateResult(FoxIdentitySelectionActionOutcome.Rejected);
            if (string.Equals(selectedCandidateId, candidateId, StringComparison.Ordinal))
                return CreateResult(FoxIdentitySelectionActionOutcome.NoOp);

            selectedCandidateId = candidateId;
            return CreateResult(FoxIdentitySelectionActionOutcome.Applied);
        }

        public FoxIdentitySelectionResult UseOnce()
        {
            return UseSelectedCandidate(false);
        }

        public FoxIdentitySelectionResult SaveAndUse()
        {
            return UseSelectedCandidate(true);
        }

        public FoxIdentitySelectionResult ClearSaved()
        {
            if (string.IsNullOrWhiteSpace(savedIdentitySignature))
                return CreateResult(FoxIdentitySelectionActionOutcome.NoOp);

            try
            {
                persistence.ClearSavedIdentity();
            }
            catch (Exception exception)
            {
                return CreateResult(
                    FoxIdentitySelectionActionOutcome.PersistenceFailed,
                    false,
                    false,
                    false,
                    false,
                    exception,
                    true);
            }

            savedIdentitySignature = string.Empty;
            return CreateResult(
                FoxIdentitySelectionActionOutcome.Applied,
                false,
                true,
                false,
                false,
                null,
                true);
        }

        public FoxIdentitySelectionResult Cancel()
        {
            if (!open)
                return CreateResult(FoxIdentitySelectionActionOutcome.NoOp);

            bool restore = restorePreviousManualModeOnCancel;
            AutoPlayColorMode restoreMode = previousManualMode;
            CloseSelection();
            return new FoxIdentitySelectionResult(
                FoxIdentitySelectionActionOutcome.Applied,
                CreateSnapshot(),
                false,
                false,
                false,
                restore,
                restoreMode,
                false,
                null);
        }

        private FoxIdentitySelectionResult UseSelectedCandidate(bool save)
        {
            FoxIdentityCandidate selected = FindCandidate(selectedCandidateId);
            if (!open || selected == null)
                return CreateResult(FoxIdentitySelectionActionOutcome.Rejected);

            string signature = NormalizeSignature(selected.Signature);
            bool currentChanged = !string.Equals(
                currentProcessIdentitySignature,
                signature,
                StringComparison.Ordinal);
            currentProcessIdentitySignature = signature;

            bool persistenceAttempted = false;
            bool persistedChanged = false;
            Exception persistenceError = null;
            FoxIdentitySelectionActionOutcome outcome = FoxIdentitySelectionActionOutcome.Applied;
            if (save)
            {
                persistenceAttempted = true;
                try
                {
                    persistence.SaveIdentitySignature(signature);
                    persistedChanged = !string.Equals(
                        savedIdentitySignature,
                        signature,
                        StringComparison.Ordinal);
                    savedIdentitySignature = signature;
                }
                catch (Exception exception)
                {
                    outcome = FoxIdentitySelectionActionOutcome.PersistenceFailed;
                    persistenceError = exception;
                }
            }

            CloseSelection();
            return new FoxIdentitySelectionResult(
                outcome,
                CreateSnapshot(),
                currentChanged,
                persistedChanged,
                true,
                false,
                previousManualMode,
                persistenceAttempted,
                persistenceError);
        }

        private FoxIdentitySelectionResult CreateResult(FoxIdentitySelectionActionOutcome outcome)
        {
            return CreateResult(outcome, false, false, false, false, null);
        }

        private FoxIdentitySelectionResult CreateResult(
            FoxIdentitySelectionActionOutcome outcome,
            bool currentChanged,
            bool persistedChanged,
            bool requiresAutomaticColorReevaluation,
            bool restorePreviousManualMode,
            Exception persistenceError,
            bool persistenceAttempted = false)
        {
            return new FoxIdentitySelectionResult(
                outcome,
                CreateSnapshot(),
                currentChanged,
                persistedChanged,
                requiresAutomaticColorReevaluation,
                restorePreviousManualMode,
                previousManualMode,
                persistenceAttempted,
                persistenceError);
        }

        private FoxIdentitySelectionSnapshot CreateSnapshot()
        {
            string savedCandidateId = FindCandidateId(savedIdentitySignature);
            return new FoxIdentitySelectionSnapshot(
                open,
                open ? candidates : new List<FoxIdentityCandidate>(),
                open ? selectedCandidateId : null,
                savedCandidateId,
                currentProcessIdentitySignature,
                savedIdentitySignature);
        }

        private string FindPreferredCandidateId()
        {
            if (!string.IsNullOrWhiteSpace(currentProcessIdentitySignature))
                return FindCandidateId(currentProcessIdentitySignature);
            return FindCandidateId(savedIdentitySignature);
        }

        private string FindCandidateId(string signature)
        {
            if (string.IsNullOrWhiteSpace(signature))
                return null;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (string.Equals(candidates[i].Signature, signature, StringComparison.Ordinal))
                    return candidates[i].Id;
            }
            return null;
        }

        private FoxIdentityCandidate FindCandidate(string candidateId)
        {
            if (string.IsNullOrWhiteSpace(candidateId))
                return null;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (string.Equals(candidates[i].Id, candidateId, StringComparison.Ordinal))
                    return candidates[i];
            }
            return null;
        }

        private bool ContainsCandidateId(string candidateId)
        {
            return FindCandidate(candidateId) != null;
        }

        private void CloseSelection()
        {
            open = false;
            candidates.Clear();
            selectedCandidateId = null;
            restorePreviousManualModeOnCancel = false;
        }

        private static string NormalizeSignature(string signature)
        {
            return string.IsNullOrWhiteSpace(signature) ? string.Empty : signature.Trim();
        }

        private static AutoPlayColorMode NormalizeManualMode(AutoPlayColorMode mode)
        {
            return mode == AutoPlayColorMode.ManualWhite
                ? AutoPlayColorMode.ManualWhite
                : AutoPlayColorMode.ManualBlack;
        }
    }
}
