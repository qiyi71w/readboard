using System;
using System.Collections.Generic;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.AutoPlay
{
    public sealed class FoxMatchBarIdentityCandidatesTests
    {
        [Fact]
        public void Build_LeftAndRightSeats_UseDirectoryCorrectedExactNamesNotOcrTypos()
        {
            IList<FoxIdentityCandidate> candidates = FoxMatchBarIdentityCandidates.Build(
                "叶落让子",
                "真的不懂啊",
                new[] { "叶落メ让子", "真的不懂啊", "观众甲" });

            Assert.Equal(2, candidates.Count);
            AssertSeat(candidates[0], "left", "WebView_candidateLeftSeat", "叶落メ让子");
            AssertSeat(candidates[1], "right", "WebView_candidateRightSeat", "真的不懂啊");
            Assert.DoesNotContain(candidates, candidate => string.Equals(candidate.Signature, "叶落让子", StringComparison.Ordinal));
        }

        [Fact]
        public void Build_FailedCorrectionSeat_CannotBeSavedAsFoxNickname()
        {
            IList<FoxIdentityCandidate> candidates = FoxMatchBarIdentityCandidates.Build(
                "叶落让子",
                "真的不懂啊",
                new[] { "叶落メ让子", "叶落让子", "真的不懂啊" });

            Assert.Single(candidates);
            AssertSeat(candidates[0], "right", "WebView_candidateRightSeat", "真的不懂啊");
        }

        [Fact]
        public void Build_BothSeatsFailCorrection_CannotSaveIdentityLikeMissingSelection()
        {
            RecordingPersistence persistence = new RecordingPersistence();
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            FoxIdentitySelectionSnapshot snapshot = selection.Open(
                FoxMatchBarIdentityCandidates.Build(
                    "叶落让子",
                    "对手甲",
                    new string[0]),
                true,
                AutoPlayColorMode.ManualWhite);

            Assert.Empty(snapshot.Candidates);
            Assert.True(string.IsNullOrWhiteSpace(snapshot.SelectedCandidateId));
            Assert.Equal(FoxIdentitySelectionActionOutcome.Rejected, selection.UseOnce().Outcome);
            Assert.Equal(FoxIdentitySelectionActionOutcome.Rejected, selection.SaveAndUse().Outcome);
            Assert.Empty(persistence.SavedSignatures);
            Assert.Equal(string.Empty, selection.EffectiveIdentitySignature);
        }

        [Fact]
        public void UseOnce_MatchBarSeat_KeepsFoxNicknameInProcessOnly()
        {
            RecordingPersistence persistence = new RecordingPersistence();
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(
                FoxMatchBarIdentityCandidates.Build(
                    "叶落让子",
                    "真的不懂啊",
                    new[] { "叶落メ让子", "真的不懂啊" }),
                true,
                AutoPlayColorMode.ManualBlack);
            selection.Select("left");
            FoxIdentitySelectionResult result = selection.UseOnce();

            Assert.Equal(FoxIdentitySelectionActionOutcome.Applied, result.Outcome);
            Assert.Equal("叶落メ让子", selection.CurrentProcessIdentitySignature);
            Assert.Equal("叶落メ让子", selection.EffectiveIdentitySignature);
            Assert.Empty(persistence.SavedSignatures);
            Assert.True(result.RequiresAutomaticColorReevaluation);
        }

        [Fact]
        public void SaveAndUse_MatchBarSeat_WritesDirectoryCorrectedFoxNickname()
        {
            AppConfig config = AppConfig.CreateDefault("p", "machine");
            ConfigBackedPersistence persistence = new ConfigBackedPersistence(config);
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(
                FoxMatchBarIdentityCandidates.Build(
                    "对手乙",
                    "叶落让子",
                    new[] { "对手乙", "叶落メ让子" }),
                false,
                AutoPlayColorMode.ManualBlack);
            selection.Select("right");
            FoxIdentitySelectionResult result = selection.SaveAndUse();

            Assert.Equal(FoxIdentitySelectionActionOutcome.Applied, result.Outcome);
            Assert.True(result.PersistedIdentityChanged);
            Assert.Equal("叶落メ让子", config.FoxAutoPlayNickname);
            Assert.Equal(string.Empty, config.FoxAutoPlayNicknameSignature);
            Assert.Equal("叶落メ让子", selection.EffectiveIdentitySignature);
        }

        [Fact]
        public void ClearSaved_MatchBarSeat_ClearsDiskIdentityOnly()
        {
            AppConfig config = AppConfig.CreateDefault("p", "machine");
            config.AutoPlayColorMode = AutoPlayColorMode.FoxAuto;
            ConfigBackedPersistence persistence = new ConfigBackedPersistence(config);
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(
                FoxMatchBarIdentityCandidates.Build(
                    "叶落メ让子",
                    "对手乙",
                    new[] { "叶落メ让子", "对手乙" }),
                true,
                AutoPlayColorMode.ManualBlack);
            selection.Select("left");
            selection.SaveAndUse();
            selection.Open(
                FoxMatchBarIdentityCandidates.Build(
                    "叶落メ让子",
                    "对手乙",
                    new[] { "叶落メ让子", "对手乙" }),
                false,
                AutoPlayColorMode.ManualBlack);

            FoxIdentitySelectionResult result = selection.ClearSaved();

            Assert.Equal(FoxIdentitySelectionActionOutcome.Applied, result.Outcome);
            Assert.Equal(string.Empty, config.FoxAutoPlayNickname);
            Assert.Equal(string.Empty, config.FoxAutoPlayNicknameSignature);
            Assert.Equal(AutoPlayColorMode.FoxAuto, config.AutoPlayColorMode);
            Assert.Equal("叶落メ让子", selection.CurrentProcessIdentitySignature);
            Assert.False(result.RequiresAutomaticColorReevaluation);
        }

        [Fact]
        public void Cancel_FirstAutomaticMatchBarSelection_RestoresPreviousManualMode()
        {
            FoxIdentitySelection selection = new FoxIdentitySelection(new RecordingPersistence());

            selection.Open(
                FoxMatchBarIdentityCandidates.Build(
                    "叶落メ让子",
                    "对手乙",
                    new[] { "叶落メ让子", "对手乙" }),
                true,
                AutoPlayColorMode.ManualWhite);
            FoxIdentitySelectionResult result = selection.Cancel();

            Assert.True(result.RestorePreviousManualMode);
            Assert.Equal(AutoPlayColorMode.ManualWhite, result.RestoredManualMode);
            Assert.False(result.RequiresAutomaticColorReevaluation);
            Assert.Equal(string.Empty, selection.EffectiveIdentitySignature);
        }

        [Fact]
        public void Cancel_ManuallyOpenedMatchBarSelection_LeavesIdentityAndModeUnchanged()
        {
            RecordingPersistence persistence = new RecordingPersistence("叶落メ让子");
            FoxIdentitySelection selection = new FoxIdentitySelection(persistence);

            selection.Open(
                FoxMatchBarIdentityCandidates.Build(
                    "叶落メ让子",
                    "对手乙",
                    new[] { "叶落メ让子", "对手乙" }),
                false,
                AutoPlayColorMode.ManualBlack);
            FoxIdentitySelectionResult result = selection.Cancel();

            Assert.False(result.RestorePreviousManualMode);
            Assert.False(result.RequiresAutomaticColorReevaluation);
            Assert.Equal("叶落メ让子", selection.EffectiveIdentitySignature);
            Assert.Equal("叶落メ让子", persistence.CurrentSignature);
            Assert.False(result.CurrentProcessIdentityChanged);
        }

        private static void AssertSeat(
            FoxIdentityCandidate candidate,
            string id,
            string labelKey,
            string exactName)
        {
            Assert.Equal(id, candidate.Id);
            Assert.Equal(labelKey, candidate.LabelMessage.Key);
            Assert.Equal(exactName, Assert.Single(candidate.LabelMessage.Arguments));
            Assert.Equal(exactName, candidate.Signature);
            Assert.Null(candidate.PreviewUrl);
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
            public List<string> SavedSignatures { get; } = new List<string>();

            public string LoadSavedIdentitySignature()
            {
                return CurrentSignature;
            }

            public void SaveIdentitySignature(string signature)
            {
                CurrentSignature = signature;
                SavedSignatures.Add(signature);
            }

            public void ClearSavedIdentity()
            {
                CurrentSignature = string.Empty;
            }
        }
    }
}
