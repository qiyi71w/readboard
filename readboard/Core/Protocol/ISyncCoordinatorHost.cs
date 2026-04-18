using System;

namespace readboard
{
    internal interface ISyncCoordinatorHost
    {
        SyncCoordinatorHostSnapshot CaptureSnapshot();
        void UpdateSelectedWindowHandle(IntPtr handle);
        void OnKeepSyncStarted();
        void OnKeepSyncStopped(bool continuousSyncActive);
        void OnContinuousSyncStarted();
        void OnContinuousSyncStopped();
        void ShowMissingSyncSourceMessage();
        void ShowRecognitionFailureMessage();
        void MinimizeWindow();
        void ReleasePlacementBinding(IntPtr handle);
        bool TrySendPlaceProtocolError(string message);
    }
}
