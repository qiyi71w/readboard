using System.Collections.Generic;

namespace readboard
{
    internal sealed class ReadBoardUpdateUiState
    {
        public bool Open { get; set; }

        public string Status { get; set; }

        public string CurrentVersion { get; set; }

        public string LatestVersion { get; set; }

        public string ReleaseDate { get; set; }

        public string ReleaseNotes { get; set; }

        public string Title { get; set; }

        public string Detail { get; set; }

        public string Message { get; set; }

        public string ErrorTitle { get; set; }

        public string Error { get; set; }

        public int? Progress { get; set; }

        public IReadOnlyList<ReadBoardUpdateStepUiState> Steps { get; set; }
    }

    internal sealed class ReadBoardUpdateStepUiState
    {
        public string Label { get; set; }

        public string Status { get; set; }
    }
}
