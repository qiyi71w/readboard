using System;
using System.Collections.Generic;

namespace readboard
{
    internal static class FoxWindowBindingResolver
    {
        public static bool TryResolve(
            IntPtr boardHandle,
            Func<IntPtr, string> getWindowTitle,
            Func<IntPtr, IntPtr> getParentHandle,
            out FoxWindowBinding binding,
            out FoxWindowContext context)
        {
            binding = null;
            context = FoxWindowContext.Unknown();
            if (boardHandle == IntPtr.Zero || getWindowTitle == null || getParentHandle == null)
                return false;

            HashSet<IntPtr> visitedHandles = new HashSet<IntPtr>();
            IntPtr currentHandle = boardHandle;
            while (currentHandle != IntPtr.Zero && visitedHandles.Add(currentHandle))
            {
                FoxWindowContext currentContext = FoxWindowContextParser.Parse(getWindowTitle(currentHandle));
                if (currentContext.Kind != FoxWindowKind.Unknown)
                {
                    binding = new FoxWindowBinding
                    {
                        BoardHandle = boardHandle,
                        TitleSourceHandle = currentHandle,
                        WindowKind = currentContext.Kind
                    };
                    context = currentContext;
                    return true;
                }

                currentHandle = getParentHandle(currentHandle);
            }

            return false;
        }
    }
}
