using System;

namespace readboard
{
    internal interface IWindowDescriptorFactory
    {
        bool TryCreate(IntPtr handle, out WindowDescriptor descriptor);
    }
}
