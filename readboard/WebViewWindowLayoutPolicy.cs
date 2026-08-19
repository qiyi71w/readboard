using System;
using System.Drawing;

namespace readboard
{
    internal static class WebViewWindowLayoutPolicy
    {
        internal static readonly Size BaseLogicalClientSize = new Size(1100, 680);
        internal static readonly Size MinimumLogicalClientSize = new Size(700, 433);

        internal static double ResolveScale(Size logicalClientSize)
        {
            return 1d;
        }

        internal static int ResolveTitleControlExtent(Size logicalClientSize, int dpi)
        {
            bool dense = logicalClientSize.Width < BaseLogicalClientSize.Width
                || logicalClientSize.Height < BaseLogicalClientSize.Height;
            double logicalExtent = dense ? 40d : 48d;
            return (int)Math.Round(logicalExtent * Math.Max(96, dpi) / 96d);
        }

        internal static Size ScaleLogicalSize(Size logicalSize, int dpi)
        {
            double scale = Math.Max(96, dpi) / 96d;
            return new Size(
                (int)Math.Round(logicalSize.Width * scale),
                (int)Math.Round(logicalSize.Height * scale));
        }

        internal static Size UnscalePhysicalSize(Size physicalSize, int dpi)
        {
            double scale = Math.Max(96, dpi) / 96d;
            return new Size(
                (int)Math.Round(physicalSize.Width / scale),
                (int)Math.Round(physicalSize.Height / scale));
        }

        internal static Rectangle ClampBoundsToWorkingArea(
            Rectangle desiredBounds,
            Rectangle workingArea,
            Size minimumSize)
        {
            if (workingArea.Width <= 0 || workingArea.Height <= 0)
                return desiredBounds;

            int width = Math.Min(Math.Max(desiredBounds.Width, minimumSize.Width), workingArea.Width);
            int height = Math.Min(Math.Max(desiredBounds.Height, minimumSize.Height), workingArea.Height);
            int x = Math.Min(Math.Max(desiredBounds.X, workingArea.Left), workingArea.Right - width);
            int y = Math.Min(Math.Max(desiredBounds.Y, workingArea.Top), workingArea.Bottom - height);
            return new Rectangle(x, y, width, height);
        }
    }
}
