using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace readboard
{
    internal static class FoxWindowTitleReader
    {
        public static bool TryRead(
            FoxWindowBinding binding,
            IntPtr boardHandle,
            Func<IntPtr, IntPtr> getParentHandle,
            out FoxWindowContext context)
        {
            return TryRead(binding, boardHandle, IsWindowHandle, ReadWindowTitle, getParentHandle, out context);
        }

        public static bool TryRead(
            FoxWindowBinding binding,
            IntPtr boardHandle,
            Func<IntPtr, bool> isWindowHandle,
            Func<IntPtr, string> getWindowTitle,
            Func<IntPtr, IntPtr> getParentHandle,
            out FoxWindowContext context)
        {
            context = FoxWindowContext.Unknown();
            if (binding == null
                || boardHandle == IntPtr.Zero
                || binding.BoardHandle == IntPtr.Zero
                || binding.TitleSourceHandle == IntPtr.Zero
                || getWindowTitle == null
                || getParentHandle == null)
            {
                return false;
            }

            if (binding.BoardHandle != boardHandle)
                return false;

            if (isWindowHandle != null)
            {
                if (!isWindowHandle(boardHandle) || !isWindowHandle(binding.TitleSourceHandle))
                    return false;
            }

            if (!BelongsToBoardParentChain(boardHandle, binding.TitleSourceHandle, getParentHandle))
                return false;

            context = FoxWindowContextParser.Parse(getWindowTitle(binding.TitleSourceHandle));
            if (context.Kind == FoxWindowKind.Unknown || context.Kind != binding.WindowKind)
            {
                context = FoxWindowContext.Unknown();
                return false;
            }

            return true;
        }

        internal static string ReadWindowTitle(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return string.Empty;

            StringBuilder builder = new StringBuilder(256);
            GetWindowText(handle, builder, builder.Capacity);
            return builder.ToString();
        }

        private static bool BelongsToBoardParentChain(
            IntPtr boardHandle,
            IntPtr titleSourceHandle,
            Func<IntPtr, IntPtr> getParentHandle)
        {
            HashSet<IntPtr> visitedHandles = new HashSet<IntPtr>();
            IntPtr currentHandle = boardHandle;
            while (currentHandle != IntPtr.Zero && visitedHandles.Add(currentHandle))
            {
                if (currentHandle == titleSourceHandle)
                    return true;
                currentHandle = getParentHandle(currentHandle);
            }

            return false;
        }

        private static bool IsWindowHandle(IntPtr handle)
        {
            return handle != IntPtr.Zero && IsWindow(handle);
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr handle, StringBuilder title, int maxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr handle);
    }
}
