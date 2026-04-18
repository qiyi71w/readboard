namespace readboard
{
    internal static class SyncToolbarTextResolver
    {
        public static string ResolveFastSyncTextAfterContinuousStop(
            bool keepSyncActive,
            string stopSyncText,
            string fastSyncText)
        {
            return keepSyncActive ? stopSyncText : fastSyncText;
        }

        public static bool ShouldRestoreIdleUiAfterKeepSyncStop(bool continuousSyncActive)
        {
            return !continuousSyncActive;
        }
    }
}
