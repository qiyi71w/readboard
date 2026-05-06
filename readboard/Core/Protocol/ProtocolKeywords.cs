namespace readboard
{
    internal static class ProtocolKeywords
    {
        internal const string Place = "place";
        internal const string Loss = "loss";
        internal const string NotInBoard = "notinboard";
        internal const string Version = "version";
        internal const string Quit = "quit";
        internal const string Ready = "ready";
        internal const string Clear = "clear";
        internal const string BoardEnd = "end";
        internal const string PlayPonderOn = "playponder on";
        internal const string PlayPonderOff = "playponder off";
        internal const string VersionResponsePrefix = "version: ";
        internal const string Sync = "sync";
        internal const string StopSync = "stopsync";
        internal const string EndSync = "endsync";
        internal const string BothSync = "bothSync";
        internal const string NoBothSync = "nobothSync";
        internal const string ForegroundFoxWithInBoard = "foreFoxWithInBoard";
        internal const string NotForegroundFoxWithInBoard = "notForeFoxWithInBoard";
        internal const string SyncPlatformPrefix = "syncPlatform ";
        internal const string GenericSyncPlatform = "generic";
        internal const string RoomTokenPrefix = "roomToken ";
        internal const string LiveTitleMovePrefix = "liveTitleMove ";
        internal const string RecordCurrentMovePrefix = "recordCurrentMove ";
        internal const string RecordTotalMovePrefix = "recordTotalMove ";
        internal const string RecordAtEndTrue = "recordAtEnd 1";
        internal const string RecordAtEndFalse = "recordAtEnd 0";
        internal const string RecordTitleFingerprintPrefix = "recordTitleFingerprint ";
        internal const string ForceRebuild = "forceRebuild";
        internal const string FoxMoveNumberPrefix = "foxMoveNumber ";
        internal const string StartPrefix = "start ";
        internal const string PlayPrefix = "play>";
        internal const string PlaySeparator = ">";
        internal const string NoInBoard = "noinboard";
        internal const string PlaceComplete = "placeComplete";
        internal const string PlacementFailed = "error place failed";
        internal const string TimeChangedPrefix = "timechanged ";
        internal const string PlayoutsChangedPrefix = "playoutschanged ";
        internal const string FirstPolicyChangedPrefix = "firstchanged ";
        internal const string NoPonder = "noponder";
        internal const string StopAutoPlay = "stopAutoPlay";
        internal const string Pass = "pass";
        internal const string DefaultNumericValue = "0";
        internal const string ReadboardUpdateSupported = "readboardUpdateSupported";
        internal const string ReadboardUpdateReadyPrefix = "readboardUpdateReady\t";
        internal const string ReadboardUpdateInstalling = "readboardUpdateInstalling";
        internal const string ReadboardUpdateCancelled = "readboardUpdateCancelled";
        internal const string ReadboardUpdateFailedPrefix = "readboardUpdateFailed\t";
        internal const string Yike = "yike";
        internal const string YikeRoomTokenPrefix = "yikeRoomToken ";
        internal const string YikeMoveNumberPrefix = "yikeMoveNumber ";
    }
}
