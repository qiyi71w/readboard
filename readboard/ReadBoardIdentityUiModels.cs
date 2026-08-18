using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace readboard
{
    internal sealed class ReadBoardIdentityUiState
    {
        public bool Open { get; set; }
        public string SelectedId { get; set; }
        public string SavedId { get; set; }
        public bool HasSavedIdentity { get; set; }
        public bool CanUseOnce { get; set; }
        public bool CanSaveAndUse { get; set; }
        public string DialogTitle { get; set; }
        [JsonIgnore]
        public SemanticMessage DialogTitleMessage { get; set; }
        public string Prompt { get; set; }
        [JsonIgnore]
        public SemanticMessage PromptMessage { get; set; }
        public string DetectedNicknamesLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage DetectedNicknamesLabelMessage { get; set; }
        public string SelectedLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage SelectedLabelMessage { get; set; }
        public string EmptyTitle { get; set; }
        [JsonIgnore]
        public SemanticMessage EmptyTitleMessage { get; set; }
        public string WindowHint { get; set; }
        [JsonIgnore]
        public SemanticMessage WindowHintMessage { get; set; }
        public string ClearSavedLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage ClearSavedLabelMessage { get; set; }
        public string CancelLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage CancelLabelMessage { get; set; }
        public string UseOnceLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage UseOnceLabelMessage { get; set; }
        public string SaveAndUseLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage SaveAndUseLabelMessage { get; set; }
        public string UnnamedCandidateLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage UnnamedCandidateLabelMessage { get; set; }
        public string SavedLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage SavedLabelMessage { get; set; }
        public string CandidateRowLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage CandidateRowLabelMessage { get; set; }
        public string ScreenshotLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage ScreenshotLabelMessage { get; set; }
        public IList<ReadBoardIdentityCandidateUiState> Candidates { get; set; } = new List<ReadBoardIdentityCandidateUiState>();
    }

    internal sealed class ReadBoardIdentityCandidateUiState
    {
        public string Id { get; set; }
        public string Label { get; set; }
        [JsonIgnore]
        public SemanticMessage LabelMessage { get; set; }
        public string PreviewAlt { get; set; }
        public string PreviewUrl { get; set; }
    }
}
