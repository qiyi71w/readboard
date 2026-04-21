using System;

namespace readboard
{
    internal sealed partial class SyncSessionCoordinator
    {
        internal bool WaitForPendingMoveAvailability(TimeSpan timeout)
        {
            return pendingMoveAvailableEvent.Wait(timeout);
        }
    }
}
