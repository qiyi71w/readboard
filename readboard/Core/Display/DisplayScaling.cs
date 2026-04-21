using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace readboard
{
    internal static class DisplayScaling
    {
        internal const double DefaultScale = 1d;
        private const uint MonitorDefaultToNearest = 2;
        private const int EffectiveDpiType = 0;
        private const int BaseDpi = 96;

        internal static double NormalizeScale(double scale)
        {
            return scale > 0d ? scale : DefaultScale;
        }

        internal static int ScaleCoordinateDown(int value, double scale)
        {
            double safeScale = NormalizeScale(scale);
            if (safeScale <= DefaultScale)
                return value;

            return (int)(value / safeScale);
        }

        internal static int ScaleSizeDown(int value, double scale)
        {
            double safeScale = NormalizeScale(scale);
            if (safeScale <= DefaultScale)
                return value;

            return (int)Math.Round(value / safeScale);
        }

        internal static int ScaleCoordinateUp(int value, double scale)
        {
            double safeScale = NormalizeScale(scale);
            if (safeScale <= DefaultScale)
                return value;

            return (int)Math.Round(value * safeScale);
        }

        internal static int ScaleSizeUp(int value, double scale)
        {
            double safeScale = NormalizeScale(scale);
            if (safeScale <= DefaultScale)
                return value;

            return (int)Math.Round(value * safeScale);
        }

        internal static PixelRect ScreenToClientBounds(PixelRect screenBounds, PixelRect windowBounds, bool isDpiAware, double scale)
        {
            if (!IsUsable(screenBounds) || !IsUsable(windowBounds))
                return null;

            int offsetX = screenBounds.X - windowBounds.X;
            int offsetY = screenBounds.Y - windowBounds.Y;
            if (isDpiAware)
                return new PixelRect(offsetX, offsetY, screenBounds.Width, screenBounds.Height);

            return new PixelRect(
                ScaleCoordinateDown(offsetX, scale),
                ScaleCoordinateDown(offsetY, scale),
                ScaleSizeDown(screenBounds.Width, scale),
                ScaleSizeDown(screenBounds.Height, scale));
        }

        internal static PixelRect ClientToScreenBounds(PixelRect clientBounds, PixelRect windowBounds, bool isDpiAware, double scale)
        {
            if (!IsUsable(clientBounds) || !IsUsable(windowBounds))
                return null;

            if (isDpiAware)
            {
                return new PixelRect(
                    windowBounds.X + clientBounds.X,
                    windowBounds.Y + clientBounds.Y,
                    clientBounds.Width,
                    clientBounds.Height);
            }

            return new PixelRect(
                windowBounds.X + ScaleCoordinateUp(clientBounds.X, scale),
                windowBounds.Y + ScaleCoordinateUp(clientBounds.Y, scale),
                ScaleSizeUp(clientBounds.Width, scale),
                ScaleSizeUp(clientBounds.Height, scale));
        }

        internal static PixelRect ProtocolBoundsFromScreen(PixelRect screenBounds, double scale)
        {
            if (!IsUsable(screenBounds))
                return null;

            return new PixelRect(
                ScaleCoordinateDown(screenBounds.X, scale),
                ScaleCoordinateDown(screenBounds.Y, scale),
                ScaleSizeDown(screenBounds.Width, scale),
                ScaleSizeDown(screenBounds.Height, scale));
        }

        internal static PixelRect ProtocolBoundsFromWindow(PixelRect windowBounds, PixelRect sourceBounds, bool isDpiAware, double scale)
        {
            if (!IsUsable(windowBounds) || !IsUsable(sourceBounds))
                return null;

            if (isDpiAware)
            {
                return new PixelRect(
                    ScaleCoordinateDown(windowBounds.X + sourceBounds.X, scale),
                    ScaleCoordinateDown(windowBounds.Y + sourceBounds.Y, scale),
                    ScaleSizeDown(sourceBounds.Width, scale),
                    ScaleSizeDown(sourceBounds.Height, scale));
            }

            return new PixelRect(
                sourceBounds.X + ScaleCoordinateDown(windowBounds.X, scale),
                sourceBounds.Y + ScaleCoordinateDown(windowBounds.Y, scale),
                sourceBounds.Width,
                sourceBounds.Height);
        }

        internal static int ScaleClientCoordinateForPostMessage(int value, bool isDpiAware, double scale)
        {
            return isDpiAware ? value : ScaleCoordinateUp(value, scale);
        }

        internal static PixelRect GetVirtualScreenBounds()
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            return new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        internal static PixelRect GetScreenBoundsFromPoint(Point point)
        {
            Rectangle bounds = Screen.FromPoint(point).Bounds;
            return new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        internal static Rectangle GetScreenWorkingAreaFromPoint(Point point)
        {
            return Screen.FromPoint(point).WorkingArea;
        }

        internal static Point ResolveReferencePoint(
            bool useCurrentBoundsCenter,
            Rectangle currentBounds,
            Point currentLocation,
            Point? startupReferencePoint)
        {
            if (startupReferencePoint.HasValue)
                return startupReferencePoint.Value;

            if (useCurrentBoundsCenter)
                return new Point(currentBounds.Left + currentBounds.Width / 2, currentBounds.Top + currentBounds.Height / 2);

            return currentLocation;
        }

        internal static double GetScaleForPoint(Point point)
        {
            return GetScaleForMonitor(MonitorFromPoint(point, MonitorDefaultToNearest));
        }

        internal static double GetScaleForWindowBounds(PixelRect bounds)
        {
            if (!IsUsable(bounds))
                return GetFallbackScale();

            Point centerPoint = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            return GetScaleForPoint(centerPoint);
        }

        internal static double GetScaleForWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return DefaultScale;

            try
            {
                uint dpi = GetDpiForWindow(handle);
                if (dpi > 0)
                    return dpi / (double)BaseDpi;
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (DllNotFoundException)
            {
            }

            return GetFallbackScale();
        }

        internal static double ResolveWindowScale(bool isDpiAware, double dpiAwareScale, double monitorScale)
        {
            return NormalizeScale(isDpiAware ? dpiAwareScale : monitorScale);
        }

        private static double GetScaleForMonitor(IntPtr monitor)
        {
            if (monitor == IntPtr.Zero)
                return GetFallbackScale();

            try
            {
                uint dpiX;
                uint dpiY;
                if (GetDpiForMonitor(monitor, EffectiveDpiType, out dpiX, out dpiY) == 0 && dpiX > 0)
                    return dpiX / (double)BaseDpi;
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (DllNotFoundException)
            {
            }

            return GetFallbackScale();
        }

        private static double GetFallbackScale()
        {
            using (Bitmap bitmap = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                double scale = graphics.DpiX / BaseDpi;
                return NormalizeScale(scale);
            }
        }

        private static bool IsUsable(PixelRect bounds)
        {
            return bounds != null && !bounds.IsEmpty;
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(Point pt, uint dwFlags);

        [DllImport("Shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    }
}
