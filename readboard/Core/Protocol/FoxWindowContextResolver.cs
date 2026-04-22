using System;
using System.Collections.Generic;

namespace readboard
{
    internal static class FoxWindowContextResolver
    {
        public static FoxWindowContext Resolve(
            IntPtr handle,
            IWindowDescriptorFactory descriptorFactory,
            Func<IntPtr, IntPtr> getParentHandle)
        {
            if (handle == IntPtr.Zero || descriptorFactory == null || getParentHandle == null)
                return FoxWindowContext.Unknown();

            HashSet<IntPtr> visitedHandles = new HashSet<IntPtr>();
            IntPtr currentHandle = handle;
            while (currentHandle != IntPtr.Zero && visitedHandles.Add(currentHandle))
            {
                WindowDescriptor descriptor;
                if (descriptorFactory.TryCreate(currentHandle, out descriptor))
                {
                    FoxWindowContext context = FoxWindowContextParser.Parse(descriptor == null ? null : descriptor.Title);
                    if (context.Kind != FoxWindowKind.Unknown)
                        return context;
                }

                currentHandle = getParentHandle(currentHandle);
            }

            return FoxWindowContext.Unknown();
        }
    }
}
