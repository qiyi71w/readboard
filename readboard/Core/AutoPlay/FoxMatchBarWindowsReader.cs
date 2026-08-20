using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace readboard
{
    internal static class FoxMatchBarWindowsReader
    {
        private const int MaxUiaNodes = 200;
        private const string PlayerListPanelTitle = "CRoomPlayerListPanel";

        public static FoxMatchBarReading TryRead(IntPtr boardHandle)
        {
            try
            {
                uint processId = 0;
                if (boardHandle != IntPtr.Zero)
                    GetWindowThreadProcessId(boardHandle, out processId);

                IntPtr searchRoot = IntPtr.Zero;
                if (boardHandle != IntPtr.Zero && IsWindow(boardHandle))
                    searchRoot = ResolveSearchRoot(boardHandle);

                IntPtr listHandle = FindNamedOnScreenChild(searchRoot, PlayerListPanelTitle);
                if (listHandle == IntPtr.Zero)
                {
                    IntPtr visibleRoot = FindVisibleFoxSearchRoot(processId);
                    if (visibleRoot != IntPtr.Zero)
                    {
                        searchRoot = visibleRoot;
                        listHandle = FindNamedOnScreenChild(searchRoot, PlayerListPanelTitle);
                    }
                }

                IList<FoxPlayerListEntry> players = ReadPlayers(listHandle);
                string diagnostic = "hwnd=" + boardHandle.ToInt64().ToString("X")
                    + " live=" + (boardHandle != IntPtr.Zero && IsWindow(boardHandle) ? "1" : "0")
                    + " root=" + searchRoot.ToInt64().ToString("X")
                    + " list=" + listHandle.ToInt64().ToString("X")
                    + " players=" + players.Count;
                return new FoxMatchBarReading(players, diagnostic);
            }
            catch (Exception ex)
            {
                return DiagnosedEmpty("ex=" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        private static IList<FoxPlayerListEntry> ReadPlayers(IntPtr listHandle)
        {
            List<FoxPlayerListEntry> players = new List<FoxPlayerListEntry>();
            if (listHandle == IntPtr.Zero)
                return players;

            AutomationElement root;
            try
            {
                root = AutomationElement.FromHandle(listHandle);
            }
            catch
            {
                return players;
            }

            if (root == null)
                return players;

            List<AutomationElement> named = new List<AutomationElement>();
            int count = 0;
            WalkNamed(root, 0, named, ref count);

            for (int i = 0; i < named.Count && players.Count < MaxUiaNodes; i++)
            {
                string name = Safe(() => named[i].Current.Name);
                if (!FoxMatchBarSeatResolver.IsPlayerNickname(name))
                    continue;
                string next = i + 1 < named.Count
                    ? Safe(() => named[i + 1].Current.Name)
                    : string.Empty;
                if (!FoxMatchBarSeatResolver.LooksLikeRankOrStat(next))
                    continue;

                AutoPlayColorResolution stone = AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);
                if (players.Count == 0)
                    stone = AutoPlayColorResolution.Known("white", AutoPlayColorStatus.RecognizedWhite);
                else if (players.Count == 1)
                    stone = AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack);

                players.Add(new FoxPlayerListEntry(name, stone));
            }

            return players;
        }

        private static void WalkNamed(
            AutomationElement element,
            int depth,
            List<AutomationElement> named,
            ref int count)
        {
            if (element == null || depth > 8 || count >= MaxUiaNodes)
                return;

            string name = Safe(() => element.Current.Name);
            if (!string.IsNullOrWhiteSpace(name)
                && !string.Equals(name, PlayerListPanelTitle, StringComparison.Ordinal))
            {
                named.Add(element);
                count++;
            }

            AutomationElement child;
            try
            {
                child = TreeWalker.ControlViewWalker.GetFirstChild(element);
            }
            catch
            {
                return;
            }

            while (child != null && count < MaxUiaNodes)
            {
                WalkNamed(child, depth + 1, named, ref count);
                try
                {
                    child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                }
                catch
                {
                    break;
                }
            }
        }





        private static string Safe(Func<string> read)
        {
            try
            {
                return read() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsMinimized(IntPtr handle)
        {
            if (handle == IntPtr.Zero || IsIconic(handle) || !IsWindowVisible(handle))
                return true;

            RECT native;
            if (!GetWindowRect(handle, out native))
                return true;

            int width = native.Right - native.Left;
            int height = native.Bottom - native.Top;
            return native.Left <= -10000
                || native.Top <= -10000
                || width <= 1
                || height <= 1;
        }

        private static IntPtr ResolveSearchRoot(IntPtr boardHandle)
        {
            IntPtr current = boardHandle;
            while (current != IntPtr.Zero)
            {
                if (FindNamedOnScreenChild(current, PlayerListPanelTitle) != IntPtr.Zero)
                    return current;
                current = GetParent(current);
            }

            return boardHandle;
        }

        private static IntPtr FindNamedOnScreenChild(IntPtr root, string title)
        {
            IntPtr found = IntPtr.Zero;
            if (root == IntPtr.Zero)
                return found;

            EnumChildWindows(root, delegate(IntPtr child, IntPtr parameter)
            {
                if (IsMinimized(child))
                    return true;
                if (!string.Equals(GetWindowTitle(child), title, StringComparison.Ordinal))
                    return true;
                found = child;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        private static IntPtr FindVisibleFoxSearchRoot(uint preferredProcessId)
        {
            IntPtr playing = IntPtr.Zero;
            IntPtr any = IntPtr.Zero;
            EnumWindows(delegate(IntPtr top, IntPtr parameter)
            {
                if (IsMinimized(top))
                    return true;

                uint processId;
                GetWindowThreadProcessId(top, out processId);
                if (preferredProcessId != 0)
                {
                    if (processId != preferredProcessId)
                        return true;
                }
                else if (!IsFoxProcess(processId))
                {
                    return true;
                }

                if (FindNamedOnScreenChild(top, PlayerListPanelTitle) == IntPtr.Zero)
                    return true;

                if (any == IntPtr.Zero)
                    any = top;
                if (playing == IntPtr.Zero
                    && GetWindowTitle(top).IndexOf("对弈中", StringComparison.Ordinal) >= 0)
                {
                    playing = top;
                }

                return true;
            }, IntPtr.Zero);

            return playing != IntPtr.Zero ? playing : any;
        }

        private static bool IsFoxProcess(uint processId)
        {
            if (processId == 0)
                return false;

            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    string name = process.ProcessName ?? string.Empty;
                    return name.IndexOf("foxwq", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static FoxMatchBarReading DiagnosedEmpty(string diagnostic)
        {
            return new FoxMatchBarReading(Array.Empty<FoxPlayerListEntry>(), diagnostic);
        }


        private static string GetWindowTitle(IntPtr handle)
        {
            StringBuilder builder = new StringBuilder(256);
            GetWindowText(handle, builder, builder.Capacity);
            return builder.ToString();
        }

        private delegate bool EnumProc(IntPtr handle, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);


        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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
