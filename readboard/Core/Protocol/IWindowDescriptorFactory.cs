using System;

namespace readboard
{
    internal interface IWindowDescriptorFactory
    {
        bool TryCreate(IntPtr handle, float dpiScale, out WindowDescriptor descriptor);
    }
}
