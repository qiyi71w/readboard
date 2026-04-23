using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace readboard
{
    internal sealed class GlobalKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private readonly LowLevelKeyboardProc callback;
        private IntPtr hookId = IntPtr.Zero;

        public event KeyEventHandler KeyDown;
        public event KeyEventHandler KeyUp;

        public GlobalKeyboardHook()
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
                hookId = SetWindowsHookEx(WH_KEYBOARD_LL, callback,
                    GetModuleHandle(module.ModuleName), 0);
            }
            if (hookId == IntPtr.Zero)
                Trace.WriteLine("GlobalKeyboardHook.Start: SetWindowsHookEx failed, hook is not active.");
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
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                int message = (int)wParam;
                if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                    KeyDown?.Invoke(this, new KeyEventArgs((Keys)vkCode));
                else if (message == WM_KEYUP || message == WM_SYSKEYUP)
                    KeyUp?.Invoke(this, new KeyEventArgs((Keys)vkCode));
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
