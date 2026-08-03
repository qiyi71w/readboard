using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace readboard
{
    internal sealed class ReadBoardSettingsUiState
    {
        public bool AutoMinimize { get; set; }
        public bool BackgroundAnalysis { get; set; }
        public bool Magnifier { get; set; }
        public bool EnhancedCapture { get; set; }
        public bool PlacementValidation { get; set; }
        public string SyncInterval { get; set; }
        public string GrayOffset { get; set; }
        public string BlackOffset { get; set; }
        public string BlackPercent { get; set; }
        public string WhiteOffset { get; set; }
        public string WhitePercent { get; set; }
        public string Theme { get; set; }
        public string Language { get; set; }
        public bool Diagnostics { get; set; }
        public bool Dirty { get; set; }
        public string DirtyStatus { get; set; }
        public IDictionary<string, string> Errors { get; set; } = new Dictionary<string, string>();
        public string SaveError { get; set; }
    }

    internal sealed class ReadBoardDialogUiState
    {
        public bool Open { get; set; }
        public string Kind { get; set; }
        public string Title { get; set; }
        [JsonIgnore]
        public SemanticMessage TitleMessage { get; set; }

        public string Heading { get; set; }
        public string Message { get; set; }
        [JsonIgnore]
        public SemanticMessage MessageMessage { get; set; }

        public string Detail { get; set; }
        [JsonIgnore]
        public SemanticMessage DetailMessage { get; set; }

        public string ConfirmLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage ConfirmLabelMessage { get; set; }

        public string CancelLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage CancelLabelMessage { get; set; }

        public string DontShowAgainLabel { get; set; }
        [JsonIgnore]
        public SemanticMessage DontShowAgainLabelMessage { get; set; }
    }

}
