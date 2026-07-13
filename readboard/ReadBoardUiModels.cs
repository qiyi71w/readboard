using System.Text.Json;
using System.Collections.Generic;

namespace readboard
{
    internal sealed class ReadBoardUiState
    {
        public string Page { get; set; } = "controlCenter";
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
        public bool Connected { get; set; }
        public string SyncStatus { get; set; }
        public string LastSync { get; set; }
        public int StoneCount { get; set; }
        public string Duration { get; set; }
        public bool? TargetWindowValid { get; set; }
        public bool BoardRegionRecognized { get; set; }
        public bool PlacementRegionResolved { get; set; }
        public bool Maximized { get; set; }
    }

    internal sealed class ReadBoardControlCenterState
    {
        public string Platform { get; set; }
        public string Room { get; set; }
        public string Moves { get; set; }
        public string NextTurn { get; set; }
        public bool TitleBound { get; set; }
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
        public bool ShowOnBoard { get; set; }
        public bool QuickSyncActive { get; set; }
        public bool ContinuousSyncActive { get; set; }
        public bool QuickSyncEnabled { get; set; }
        public bool ContinuousSyncEnabled { get; set; }
        public int SyncInterval { get; set; }
        public bool AnalysisRunning { get; set; }
        public bool AnalysisStateAvailable { get; set; }
        public bool AnalysisToggleEnabled { get; set; }
        public bool ConfigurationEnabled { get; set; }
        public bool TwoWaySyncEnabled { get; set; }
        public bool AutoPlayToggleEnabled { get; set; }
        public bool AutoPlayControlsEnabled { get; set; }
        public bool CustomBoardDimensionsEnabled { get; set; }
        public bool IdentityEnabled { get; set; }
        public bool ShowOnBoardEnabled { get; set; }
    }

    internal sealed class ReadBoardUiLogEntry
    {
        public string Time { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
    }

    internal sealed class ReadBoardUiCommand
    {
        public string Type { get; set; }
        public JsonElement Payload { get; set; }
    }
}
