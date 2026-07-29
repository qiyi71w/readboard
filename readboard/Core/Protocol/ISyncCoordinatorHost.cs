using System;

namespace readboard
{
    internal interface ISyncCoordinatorHost
    {
        SyncCoordinatorHostSnapshot CaptureSnapshot();
        long AllocateSessionObservationGeneration();
        void UpdateSelectedWindowHandle(IntPtr handle, long observationGeneration);
        void OnKeepSyncStarted(long observationGeneration);
        void OnKeepSyncStopped(bool continuousSyncActive, long observationGeneration);
        void OnContinuousSyncStarted(long observationGeneration);
        void OnContinuousSyncStopped(long observationGeneration);
        void OnSyncCachesReset(long observationGeneration);
        void OnBoardSnapshotRecognized(
            BoardSnapshot snapshot,
            TimeSpan duration,
            long observationGeneration);
        void ShowMissingSyncSourceMessage();
        void ShowRecognitionFailureMessage();
        void MinimizeWindow();
        bool TrySendPlaceProtocolError(string message);
    }

    internal interface IWebViewSyncCoordinatorHost
    {
        void OnRuntimeFrameCleared(long observationGeneration);
        void OnBoardFrameRecognized(
            BoardFrame frame,
            int boardPixelWidth,
            int boardPixelHeight,
            bool placementRegionResolved,
            long observationGeneration);
        void OnBoardSnapshotSent(BoardSnapshot snapshot, long observationGeneration);
    }
}
