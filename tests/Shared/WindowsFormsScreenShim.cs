using System.Drawing;

namespace System.Windows.Forms
{
    public sealed class Screen
    {
        private Screen(Rectangle bounds)
        {
            Bounds = bounds;
            WorkingArea = bounds;
        }

        public Rectangle Bounds { get; private set; }
        public Rectangle WorkingArea { get; private set; }

        public static Screen PrimaryScreen
        {
            get { return new Screen(new Rectangle(0, 0, 1920, 1080)); }
        }

        public static Screen FromPoint(Point point)
        {
            return PrimaryScreen;
        }
    }

    public static class SystemInformation
    {
        public static Rectangle VirtualScreen
        {
            get { return new Rectangle(0, 0, 1920, 1080); }
        }
    }
}
