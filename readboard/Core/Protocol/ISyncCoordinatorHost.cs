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
        void OnSyncCachesReset();
        void OnBoardSnapshotRecognized(BoardSnapshot snapshot);
        void ShowMissingSyncSourceMessage();
        void ShowRecognitionFailureMessage();
        void MinimizeWindow();
        bool TrySendPlaceProtocolError(string message);
    }
}
