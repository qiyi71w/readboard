using System.Collections.Generic;
using System.Text.Json.Serialization;
namespace readboard
{
    internal sealed class ReadBoardUpdateUiState
    {
        public bool Open { get; set; }

        public string Status { get; set; }

        public string CurrentVersion { get; set; }

        public string LatestVersion { get; set; }

        public string ReleaseDate { get; set; }
        [JsonIgnore]
        public SemanticMessage ReleaseDateMessage { get; set; }

        public string ReleaseNotes { get; set; }
        [JsonIgnore]
        public SemanticMessage ReleaseNotesMessage { get; set; }

        public string Title { get; set; }
        public string DialogTitle { get; set; }
        [JsonIgnore]
        public SemanticMessage DialogTitleMessage { get; set; }

        public string CloseLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage CloseLabelMessage { get; set; }

        public string DoneLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage DoneLabelMessage { get; set; }

        public string CurrentVersionLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage CurrentVersionLabelMessage { get; set; }

        public string LatestVersionLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage LatestVersionLabelMessage { get; set; }

        public string ReleaseDateLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage ReleaseDateLabelMessage { get; set; }

        public string ReleaseNotesLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage ReleaseNotesLabelMessage { get; set; }

        public string DownloadLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage DownloadLabelMessage { get; set; }

        public string DownloadAndInstallLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage DownloadAndInstallLabelMessage { get; set; }

        public string ProcessingLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage ProcessingLabelMessage { get; set; }
        [JsonIgnore]
        public SemanticMessage TitleMessage { get; set; }

        [JsonIgnore]
        public SemanticMessage DetailMessage { get; set; }

        [JsonIgnore]
        public IReadOnlyList<SemanticMessage> DetailMessages { get; set; }

        [JsonIgnore]
        public SemanticMessage MessageMessage { get; set; }

        [JsonIgnore]
        public SemanticMessage ErrorTitleMessage { get; set; }

        [JsonIgnore]
        public SemanticMessage ErrorMessage { get; set; }

        [JsonIgnore]
        public IReadOnlyList<SemanticMessage> ReleaseNotesMessages { get; set; }

        public string Detail { get; set; }

        public string Message { get; set; }

        public string ErrorTitle { get; set; }

        public string Error { get; set; }

        public int? Progress { get; set; }

        public IReadOnlyList<ReadBoardUpdateStepUiState> Steps { get; set; }
    }

    internal sealed class ReadBoardUpdateStepUiState
    {
        [JsonIgnore]
        public SemanticMessage LabelMessage { get; set; }
        public string Label { get; set; }

        public string Status { get; set; }
    }
}
