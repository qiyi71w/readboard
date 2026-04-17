using System;

namespace readboard
{
    internal interface ISyncWindowLocator
    {
        IntPtr FindWindowHandle(SyncMode syncMode);
    }
}
