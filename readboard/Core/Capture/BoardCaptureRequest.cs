namespace readboard
{
    internal sealed class BoardCaptureRequest
    {
        public WindowDescriptor Window { get; set; }
        public SyncMode SyncMode { get; set; }
        public BoardDimensions BoardSize { get; set; }
        public PixelRect SelectionBounds { get; set; }
        public bool PreferPrintWindow { get; set; }
        public bool UseEnhancedCapture { get; set; }
        public bool IsInitialProbe { get; set; }
    }
}
