using System.Drawing;

namespace System.Windows.Forms
{
    public sealed class Screen
    {
        private Screen(Rectangle bounds)
        {
            Bounds = bounds;
        }

        public Rectangle Bounds { get; private set; }

        public static Screen PrimaryScreen
        {
            get { return new Screen(new Rectangle(0, 0, 1920, 1080)); }
        }
    }
}
