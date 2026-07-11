using System.Collections.Generic;

namespace readboard
{
    internal sealed class ReadBoardIdentityUiState
    {
        public bool Open { get; set; }
        public string SelectedId { get; set; }
        public string SavedId { get; set; }
        public bool HasSavedIdentity { get; set; }
        public IList<ReadBoardIdentityCandidateUiState> Candidates { get; set; } = new List<ReadBoardIdentityCandidateUiState>();
    }

    internal sealed class ReadBoardIdentityCandidateUiState
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string PreviewUrl { get; set; }
    }
}
