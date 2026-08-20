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

            if (ShouldRevokeFoxAutoPlayAuthorization(snapshot))
            {
                coordinator.RevokeAutoPlayIfAuthorized();
                return;
            }

            if (!snapshot.CanSendAutoPlayCommand(keepSync))
                return;

            if (snapshot.AutoPlayColorResolution == null || !snapshot.AutoPlayColorResolution.IsKnown)
                return;

            coordinator.SendPlay(
                snapshot.PlayColor,
                snapshot.AutoPlayColorMode,
                ToProtocolNumericValue(snapshot.AiTimeValue),
                ToProtocolNumericValue(snapshot.PlayoutsValue),
                ToProtocolNumericValue(snapshot.FirstPolicyValue),
                snapshot.AutoPlayMoveMode);
        }

        private static bool ShouldRevokeFoxAutoPlayAuthorization(ControlCenterRuntimeSnapshot snapshot)
        {
            if (snapshot.AutoPlayColorMode != AutoPlayColorMode.FoxAuto)
                return false;

            return !snapshot.AutoPlayEnabled || !snapshot.TwoWaySync;
        }

        internal static string ToProtocolNumericValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "0" : value;
        }
    }
}
