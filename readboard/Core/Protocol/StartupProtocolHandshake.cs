using System;

namespace readboard
{
    internal static class StartupProtocolHandshake
    {
        internal static bool Run(
            Func<bool> tryStartSession,
            Func<bool> isShutdownRequested,
            Action drainStartupCommands,
            Action notifyProtocolReady,
            Action replayStartupState)
        {
            if (tryStartSession == null)
                throw new ArgumentNullException(nameof(tryStartSession));
            if (isShutdownRequested == null)
                throw new ArgumentNullException(nameof(isShutdownRequested));
            if (drainStartupCommands == null)
                throw new ArgumentNullException(nameof(drainStartupCommands));
            if (notifyProtocolReady == null)
                throw new ArgumentNullException(nameof(notifyProtocolReady));
            if (replayStartupState == null)
                throw new ArgumentNullException(nameof(replayStartupState));

            if (!tryStartSession())
                return false;

            drainStartupCommands();
            if (isShutdownRequested())
                return false;

            notifyProtocolReady();
            drainStartupCommands();
            if (isShutdownRequested())
                return false;

            replayStartupState();
            drainStartupCommands();
            return !isShutdownRequested();
        }
    }
}
