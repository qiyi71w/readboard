using System;

namespace readboard
{
    internal sealed class WindowDescriptor
    {
        public IntPtr Handle { get; set; }
        public string ClassName { get; set; }
        public string ProcessName { get; set; }
        public string Title { get; set; }
        public PixelRect Bounds { get; set; }
        // Physical screen bounds of this HWND's client area, distinct from its outer frame.
        public PixelRect ClientBounds { get; set; }
        public bool IsDpiAware { get; set; }
        public double DpiScale { get; set; }
        public bool IsJavaWindow { get; set; }
    }
}
