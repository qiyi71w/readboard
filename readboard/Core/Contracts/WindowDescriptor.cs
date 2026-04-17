using System;

namespace readboard
{
    internal sealed class WindowDescriptor
    {
        public IntPtr Handle { get; set; }
        public string ClassName { get; set; }
        public string Title { get; set; }
        public PixelRect Bounds { get; set; }
        public bool IsDpiAware { get; set; }
        public double DpiScale { get; set; }
        public bool IsJavaWindow { get; set; }
    }
}
