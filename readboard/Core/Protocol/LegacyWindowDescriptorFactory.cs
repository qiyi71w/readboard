using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace readboard
{
    internal sealed class LegacyWindowDescriptorFactory : IWindowDescriptorFactory
    {
        public bool TryCreate(IntPtr handle, float dpiScale, out WindowDescriptor descriptor)
        {
            descriptor = null;
            RECT rect;
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out rect))
                return false;
            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                return false;

            string className = GetClassName(handle);
            descriptor = new WindowDescriptor
            {
                Handle = handle,
                ClassName = className,
                Title = GetWindowTitle(handle),
                Bounds = new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
                IsDpiAware = GetSupportDpiState(handle),
                DpiScale = dpiScale,
                IsJavaWindow = string.Equals(className, "SunAwtFrame", StringComparison.Ordinal)
            };
            return true;
        }

        private static string GetClassName(IntPtr handle)
        {
            StringBuilder builder = new StringBuilder(256);
            GetClassName(handle, builder, builder.Capacity);
            return builder.ToString();
        }

        private static string GetWindowTitle(IntPtr handle)
        {
            StringBuilder builder = new StringBuilder(256);
            GetWindowText(handle, builder, builder.Capacity);
            return builder.ToString();
        }

        private static bool GetSupportDpiState(IntPtr handle)
        {
            try
            {
                int processId;
                GetWindowThreadProcessId(handle, out processId);
                using (Process process = Process.GetProcessById(processId))
                {
                    PROCESS_DPI_AWARENESS awareness;
                    if (GetProcessDpiAwareness(process.Handle, out awareness) != 0)
                        return false;
                    return awareness != PROCESS_DPI_AWARENESS.PROCESS_DPI_UNAWARE;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr handle, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr handle, out RECT rect);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr handle, StringBuilder title, int maxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr handle, out int processId);

        [DllImport("Shcore.dll")]
        private static extern int GetProcessDpiAwareness(IntPtr processHandle, out PROCESS_DPI_AWARENESS awareness);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private enum PROCESS_DPI_AWARENESS
        {
            PROCESS_DPI_UNAWARE = 0,
            PROCESS_SYSTEM_DPI_AWARE = 1,
            PROCESS_PER_MONITOR_DPI_AWARE = 2
        }
    }

    internal static class FoxMoveNumberParser
    {
        private static readonly Regex MoveNumberPattern = new Regex(@"第\s*(\d+)\s*手", RegexOptions.Compiled);

        public static int? Parse(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return null;

            Match match = MoveNumberPattern.Match(title);
            if (!match.Success)
                return null;

            int moveNumber;
            if (!int.TryParse(match.Groups[1].Value, out moveNumber))
                return null;

            return moveNumber;
        }
    }
}
