using System;

namespace readboard
{
    internal static class AutoPlayWireIssuer
    {
        public static void IssueIfAuthorized(
            ControlCenterRuntimeSnapshot snapshot,
            bool keepSync,
            ISyncSessionCoordinator coordinator)
        {
            if (snapshot == null)
                throw new ArgumentNullException("snapshot");
            if (coordinator == null)
                throw new ArgumentNullException("coordinator");

            if (!snapshot.CanSendAutoPlayCommand(keepSync))
            {
                if (snapshot.AutoPlayColorMode == AutoPlayColorMode.FoxAuto)
                    coordinator.RevokeAutoPlayIfAuthorized();
                return;
            }

            if (snapshot.AutoPlayColorResolution == null || !snapshot.AutoPlayColorResolution.IsKnown)
            {
                coordinator.RevokeAutoPlayIfAuthorized();
                return;
            }

            coordinator.SendPlay(
                snapshot.PlayColor,
                snapshot.AutoPlayColorMode,
                ToProtocolNumericValue(snapshot.AiTimeValue),
                ToProtocolNumericValue(snapshot.PlayoutsValue),
                ToProtocolNumericValue(snapshot.FirstPolicyValue),
                snapshot.AutoPlayMoveMode);
        }

        internal static string ToProtocolNumericValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "0" : value;
        }
    }
}
