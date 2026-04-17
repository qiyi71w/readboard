using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace readboard
{
    internal sealed class LegacySyncWindowLocator : ISyncWindowLocator
    {
        public IntPtr FindWindowHandle(SyncMode syncMode)
        {
            if (syncMode == SyncMode.Tygem)
                return FindTygemWindow();
            if (syncMode == SyncMode.Sina)
                return FindSinaWindow();
            if (syncMode == SyncMode.Fox || syncMode == SyncMode.FoxBackgroundPlace)
                return FindFoxWindow();
            return IntPtr.Zero;
        }

        private static IntPtr FindFoxWindow()
        {
            IList<IntPtr> handles = EnumerateProcessWindows("foxwq", "#32770");
            for (int i = 0; i < handles.Count; i++)
            {
                if (string.Equals(GetWindowTitle(handles[i]), "CChessboardPanel", StringComparison.Ordinal))
                    return handles[i];
            }
            return IntPtr.Zero;
        }

        private static IntPtr FindTygemWindow()
        {
            IntPtr selected = IntPtr.Zero;
            int finalWidth = 0;
            IList<IntPtr> handles = EnumerateProcessWindows("Tygem", "AfxWnd140u");
            for (int i = 0; i < handles.Count; i++)
            {
                RECT rect;
                if (!GetWindowRect(handles[i], out rect))
                    continue;
                if (!string.Equals(GetWindowClass(handles[i]), "AfxWnd140u", StringComparison.Ordinal))
                    continue;
                if (GetWindowTitle(handles[i]).Length != 0)
                    continue;
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (!IsNearSquare(rect.Left, rect.Top, width, height))
                    continue;
                if (finalWidth == 0 || width < finalWidth)
                {
                    selected = handles[i];
                    finalWidth = width;
                }
            }
            return selected;
        }

        private static IntPtr FindSinaWindow()
        {
            IntPtr selected = IntPtr.Zero;
            int finalWidth = 0;
            IList<IntPtr> handles = EnumerateProcessWindows("Sina", "TLMDSimplePanel");
            for (int i = 0; i < handles.Count; i++)
            {
                RECT rect;
                if (!GetWindowRect(handles[i], out rect))
                    continue;
                if (!string.Equals(GetWindowClass(handles[i]), "TLMDSimplePanel", StringComparison.Ordinal))
                    continue;
                int width = rect.Right - rect.Left;
                if (!IsValidBounds(rect.Left, rect.Top, width, rect.Bottom - rect.Top))
                    continue;
                if (finalWidth == 0 || width > finalWidth)
                {
                    selected = handles[i];
                    finalWidth = width;
                }
            }
            return selected;
        }

        private static bool IsNearSquare(int x, int y, int width, int height)
        {
            if (!IsValidBounds(x, y, width, height))
                return false;
            double ratio = (double)width / (double)height;
            return ratio < 1.05d && ratio > 0.95d;
        }

        private static bool IsValidBounds(int x, int y, int width, int height)
        {
            return x >= -9999 && y >= -9999 && width > 0 && height > 0;
        }

        private static IList<IntPtr> EnumerateProcessWindows(string processName, string classNameLike)
        {
            Dictionary<IntPtr, string> roots = GetOpenWindows();
            List<IntPtr> matches = new List<IntPtr>();
            foreach (KeyValuePair<IntPtr, string> item in roots)
                AppendMatchesForProcess(item.Key, processName, classNameLike, matches);
            return matches;
        }

        private static void AppendMatchesForProcess(
            IntPtr rootHandle,
            string processName,
            string classNameLike,
            IList<IntPtr> matches)
        {
            try
            {
                int processId;
                GetWindowThreadProcessId(rootHandle, out processId);
                Process process = Process.GetProcessById(processId);
                if (!process.ProcessName.Contains(processName))
                    return;
                AppendHandleMatches(rootHandle, classNameLike, matches);
                AppendChildMatches(rootHandle, classNameLike, matches);
            }
            catch (Exception)
            {
            }
        }

        private static void AppendHandleMatches(IntPtr handle, string classNameLike, IList<IntPtr> matches)
        {
            if (!GetWindowClass(handle).Contains(classNameLike))
                return;
            matches.Add(handle);
        }

        private static void AppendChildMatches(IntPtr parent, string classNameLike, IList<IntPtr> matches)
        {
            IList<IntPtr> children = GetChildWindows(parent);
            for (int i = 0; i < children.Count; i++)
            {
                IntPtr child = children[i];
                if (!IsWindowVisible(child) || IsIconic(child))
                    continue;
                AppendHandleMatches(child, classNameLike, matches);
            }
        }

        private static Dictionary<IntPtr, string> GetOpenWindows()
        {
            IntPtr shellWindow = GetShellWindow();
            Dictionary<IntPtr, string> windows = new Dictionary<IntPtr, string>();
            EnumWindows(delegate(IntPtr handle, int lParam)
            {
                if (handle == shellWindow || !IsWindowVisible(handle) || IsIconic(handle))
                    return true;
                windows[handle] = string.Empty;
                return true;
            }, 0);
            return windows;
        }

        private static IList<IntPtr> GetChildWindows(IntPtr parent)
        {
            List<IntPtr> result = new List<IntPtr>();
            GCHandle handle = GCHandle.Alloc(result);
            try
            {
                EnumChildWindows(parent, EnumChildWindow, GCHandle.ToIntPtr(handle));
            }
            finally
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
            return result;
        }

        private static bool EnumChildWindow(IntPtr handle, IntPtr parameter)
        {
            GCHandle pointer = GCHandle.FromIntPtr(parameter);
            List<IntPtr> list = pointer.Target as List<IntPtr>;
            if (list == null)
                throw new InvalidCastException("GCHandle Target could not be cast as List<IntPtr>.");
            list.Add(handle);
            return true;
        }

        private static string GetWindowClass(IntPtr handle)
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

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr window, EnumWindowProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, int parameter);

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr handle, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr handle, StringBuilder title, int maxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr handle, out int processId);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr handle, out RECT rect);

        private delegate bool EnumWindowsProc(IntPtr handle, int parameter);

        private delegate bool EnumWindowProc(IntPtr handle, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
