using System;

namespace readboard
{
    internal static class SessionCoordinatorScope
    {
        internal static void Run(
            ISyncSessionCoordinator coordinator,
            Action<ISyncSessionCoordinator> setActiveCoordinator,
            Action<ISyncSessionCoordinator> action)
        {
            if (coordinator == null)
                throw new ArgumentNullException("coordinator");
            if (setActiveCoordinator == null)
                throw new ArgumentNullException("setActiveCoordinator");
            if (action == null)
                throw new ArgumentNullException("action");

            using (coordinator)
            {
                setActiveCoordinator(coordinator);
                try
                {
                    action(coordinator);
                }
                finally
                {
                    setActiveCoordinator(null);
                }
            }
        }
    }
}
