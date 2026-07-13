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

    internal interface IWebViewSyncCoordinatorHost
    {
        void OnRuntimeFrameCleared();
        void OnBoardFrameRecognized(
            BoardFrame frame,
            int boardPixelWidth,
            int boardPixelHeight,
            bool placementRegionResolved);
        void OnBoardSnapshotSent(BoardSnapshot snapshot);
    }
}
