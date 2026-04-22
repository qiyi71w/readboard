using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace readboard
{
    internal sealed class GlobalMouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;

        private readonly LowLevelMouseProc callback;
        private IntPtr hookId = IntPtr.Zero;
        private bool enabled;

        public event MouseEventHandler MouseMove;
        public event MouseEventHandler MouseClick;

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                if (value) Start();
                else Stop();
            }
        }

        public GlobalMouseHook()
        {
            callback = HookCallback;
        }

        public void Start()
        {
            if (hookId != IntPtr.Zero)
                return;
            using (Process process = Process.GetCurrentProcess())
            using (ProcessModule module = process.MainModule)
            {
                hookId = SetWindowsHookEx(WH_MOUSE_LL, callback,
                    GetModuleHandle(module.ModuleName), 0);
            }
        }

        public void Stop()
        {
            if (hookId == IntPtr.Zero)
                return;
            UnhookWindowsHookEx(hookId);
            hookId = IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && enabled)
            {
                MSLLHOOKSTRUCT hookData = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int message = (int)wParam;
                MouseEventArgs args = new MouseEventArgs(
                    message == WM_LBUTTONDOWN ? MouseButtons.Left :
                    message == WM_RBUTTONDOWN ? MouseButtons.Right : MouseButtons.None,
                    1, hookData.pt.X, hookData.pt.Y, 0);

                if (message == WM_MOUSEMOVE)
                    MouseMove?.Invoke(this, args);
                else if (message == WM_LBUTTONDOWN || message == WM_RBUTTONDOWN)
                    MouseClick?.Invoke(this, args);
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
