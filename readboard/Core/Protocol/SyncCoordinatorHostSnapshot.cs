using System;

namespace readboard
{
    internal sealed class SyncCoordinatorHostSnapshot
    {
        public SyncMode SyncMode { get; set; }
        public int BoardWidth { get; set; }
        public int BoardHeight { get; set; }
        public PixelRect SelectionBounds { get; set; }
        public IntPtr SelectedWindowHandle { get; set; }
        public float DpiScale { get; set; }
        public string LegacyTypeToken { get; set; }
        public bool ShowInBoard { get; set; }
        public bool SupportsForegroundFoxInBoardProtocol { get; set; }
        public bool CanUseLightweightInterop { get; set; }
        public bool AutoMinimize { get; set; }
        public int SampleIntervalMs { get; set; }
        public bool UseEnhancedCapture { get; set; }
        public string PlayColor { get; set; }
        public string AiTimeValue { get; set; }
        public string PlayoutsValue { get; set; }
        public string FirstPolicyValue { get; set; }
    }
}
