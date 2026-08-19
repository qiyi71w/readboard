using System;
using System.Drawing;

namespace readboard
{
    internal static class WebViewWindowLayoutPolicy
    {
        internal static readonly Size BaseLogicalClientSize = new Size(1100, 680);
        internal static readonly Size MinimumLogicalClientSize = new Size(700, 433);
        internal const int SidebarComfortLogicalWidth = 230;
        internal const int SidebarWideLogicalWidth = 264;
        internal const int SidebarIconLogicalWidth = 48;
        internal const int SidebarLabelMinLogicalWidth = 140;
        internal const int SidebarWideMinWindowWidth = 1281;
        internal const int SidebarLabelHysteresis = 24;
        internal const int MainComfortLogicalWidth = 870;
        internal const int PageDenseMaxLogicalWidth = 918;
        internal const int SidebarLabelHideWindowWidth = 1010;
        internal const int SidebarLabelShowWindowWidth = 1034;

        internal static double ResolveScale(Size logicalClientSize)
        {
            return 1d;
        }

        internal static int ResolveSidebarWidth(int logicalWidth)
        {
            if (logicalWidth >= SidebarWideMinWindowWidth)
                return SidebarWideLogicalWidth;
            if (logicalWidth >= BaseLogicalClientSize.Width)
                return SidebarComfortLogicalWidth;
            if (logicalWidth <= PageDenseMaxLogicalWidth)
                return SidebarIconLogicalWidth;
            return logicalWidth - MainComfortLogicalWidth;
        }

        internal const int TitleComfortLogicalExtent = 48;
        internal const int TitleCompactLogicalExtent = 40;

        internal static double ResolveVerticalT(int logicalHeight)
        {
            int span = BaseLogicalClientSize.Height - MinimumLogicalClientSize.Height;
            if (span <= 0)
                return 0d;
            double t = (BaseLogicalClientSize.Height - logicalHeight) / (double)span;
            if (t <= 0d)
                return 0d;
            if (t >= 1d)
                return 1d;
            return t;
        }

        internal static int ResolveTitleLogicalExtent(int logicalHeight)
        {
            return (int)Math.Round(
                TitleComfortLogicalExtent
                    + (TitleCompactLogicalExtent - TitleComfortLogicalExtent)
                        * ResolveVerticalT(logicalHeight),
                MidpointRounding.AwayFromZero);
        }

        internal static int ResolveTitleControlExtent(Size logicalClientSize, int dpi)
        {
            return (int)Math.Round(
                ResolveTitleLogicalExtent(logicalClientSize.Height) * Math.Max(96, dpi) / 96d,
                MidpointRounding.AwayFromZero);
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
