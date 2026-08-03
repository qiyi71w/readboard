using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace readboard
{
    internal sealed class ReadBoardUiState
    {
        public string Page { get; set; } = "controlCenter";
        public string Language { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IDictionary<string, string> Text { get; set; } = new Dictionary<string, string>();
        public ReadBoardShellState Shell { get; set; } = new ReadBoardShellState();
        public ReadBoardControlCenterState ControlCenter { get; set; } = new ReadBoardControlCenterState();
        public ReadBoardSettingsUiState Settings { get; set; }
        public ReadBoardUpdateUiState Update { get; set; }
        public ReadBoardIdentityUiState Identity { get; set; }
        public ReadBoardDialogUiState Dialog { get; set; }
        public IList<ReadBoardUiLogEntry> Logs { get; set; } = new List<ReadBoardUiLogEntry>();
    }

    internal sealed class ReadBoardShellState
    {
        public string Version { get; set; }
        public string Theme { get; set; } = "system";
        public bool Connected { get; set; }
        public string SyncStatus { get; set; }
        public string HostStatus { get; set; }
        public string TargetStatus { get; set; }
        public string BoardStatus { get; set; }
        public string PlacementStatus { get; set; }
        public string LastSync { get; set; }
        public int StoneCount { get; set; }
        public string Duration { get; set; }
        public bool? TargetWindowValid { get; set; }
        public bool BoardRegionRecognized { get; set; }
        public bool PlacementRegionResolved { get; set; }
        public bool Maximized { get; set; }
        public string MaximizeLabel { get; set; }
    }

    internal sealed class ReadBoardControlCenterState
    {
        public string Platform { get; set; }
        public string PlatformLabel { get; set; }
        public string Room { get; set; }
        public string Moves { get; set; }
        public string NextTurn { get; set; }
        public bool TitleBound { get; set; }
        public string BindingStatus { get; set; }
        public string BoardSize { get; set; }
        public int BoardWidth { get; set; }
        public int BoardHeight { get; set; }
        public bool TwoWaySync { get; set; }
        public bool AutoPlay { get; set; }
        public string Color { get; set; }
        public string Placement { get; set; }
        public string AiTime { get; set; }
        public string Playouts { get; set; }
        public string FirstPolicy { get; set; }
        public bool FirstPolicyEnabled { get; set; }
        public bool ColorEnabled { get; set; }
        public bool AutoColorEnabled { get; set; }
        public bool PlacementEnabled { get; set; }
        public bool AiTimeEnabled { get; set; }
        public bool PlayoutsEnabled { get; set; }
        public string AutoPlayColorStatus { get; set; }
        public bool PlayColorKnown { get; set; }
        public bool ShowOnBoard { get; set; }
        public bool QuickSyncActive { get; set; }
        public bool ContinuousSyncActive { get; set; }
        public string QuickSyncLabel { get; set; }
        public string ContinuousSyncLabel { get; set; }
        public bool QuickSyncEnabled { get; set; }
        public bool ContinuousSyncEnabled { get; set; }
        public bool OneTimeSyncEnabled { get; set; }
        public int SyncInterval { get; set; }
        public bool AnalysisRunning { get; set; }
        public string AnalysisLabel { get; set; }
        public bool AnalysisStateAvailable { get; set; }
        public bool AnalysisToggleEnabled { get; set; }
        public bool SwapOrderEnabled { get; set; }
        public bool ForceRebuildEnabled { get; set; }
        public bool ClearBoardEnabled { get; set; }
        public bool BoardSelectionInsideEnabled { get; set; }
        public bool BoardSelectionRectangleEnabled { get; set; }
        public bool BoardSelectionLine1Enabled { get; set; }
        public bool ConfigurationEnabled { get; set; }
        public bool TwoWaySyncEnabled { get; set; }
        public bool AutoPlayToggleEnabled { get; set; }
        public bool AutoPlayControlsEnabled { get; set; }
        public bool CustomBoardSizeEnabled { get; set; }
        public bool CustomBoardDimensionsEnabled { get; set; }
        public bool PreferencesSaved { get; set; }
        public string PreferencesStatus { get; set; }
        public string PersistenceError { get; set; }
        public bool IdentityEnabled { get; set; }
        public bool ShowOnBoardEnabled { get; set; }
    }

    internal sealed class ReadBoardUiLogEntry
    {
        public string Time { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public string MessageKey { get; set; }
        public string DiagnosticDetail { get; set; }
        [JsonIgnore]
        public IReadOnlyList<object> Arguments { get; set; }
    }

    internal sealed class ReadBoardUiCommand
    {
        public string Type { get; set; }
        public JsonElement Payload { get; set; }
    }
    internal enum WebViewPage
    {
        ControlCenter,
        Settings,
        Rules,
        About
    }

    internal static class WebViewPageNames
    {
        public static bool TryParse(string value, out WebViewPage page)
        {
            switch (value)
            {
                case "controlCenter":
                    page = WebViewPage.ControlCenter;
                    return true;
                case "settings":
                    page = WebViewPage.Settings;
                    return true;
                case "rules":
                    page = WebViewPage.Rules;
                    return true;
                case "about":
                    page = WebViewPage.About;
                    return true;
                default:
                    page = default(WebViewPage);
                    return false;
            }
        }

        public static string ToWireName(WebViewPage page)
        {
            switch (page)
            {
                case WebViewPage.ControlCenter:
                    return "controlCenter";
                case WebViewPage.Settings:
                    return "settings";
                case WebViewPage.Rules:
                    return "rules";
                case WebViewPage.About:
                    return "about";
                default:
                    throw new System.ArgumentOutOfRangeException("page");
            }
        }
    }

    internal sealed class WebViewNavigationIntent
    {
        public WebViewNavigationIntent(WebViewPage page)
        {
            Page = page;
        }

        public WebViewPage Page { get; private set; }
    }
}
