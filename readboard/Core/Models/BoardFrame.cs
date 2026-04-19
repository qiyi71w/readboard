using System.Drawing;

namespace readboard
{
    internal sealed class BoardFrame
    {
        public WindowDescriptor Window { get; set; }
        public SyncMode SyncMode { get; set; }
        public BoardDimensions BoardSize { get; set; }
        public BoardViewport Viewport { get; set; }
        public Bitmap Image { get; set; }
        public PixelBuffer PixelBuffer { get; set; }
        public bool PreferPrintWindow { get; set; }
        public bool UsedPrintWindow { get; set; }
        public ulong ContentSignature { get; set; }
        public bool HasSameContentAsPreviousCapture { get; set; }
    }
}
