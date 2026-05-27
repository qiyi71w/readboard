namespace readboard
{
    internal static class FoxAutoPlayColorResolver
    {
        public static AutoPlayColorResolution Resolve(
            AutoPlayColorMode mode,
            SyncMode syncMode,
            string savedNicknameSignature,
            FoxWindowContext foxWindowContext,
            AutoPlayColorResolution detected)
        {
            if (mode == AutoPlayColorMode.ManualBlack)
                return AutoPlayColorResolution.Known("black", AutoPlayColorStatus.ManualBlack);
            if (mode == AutoPlayColorMode.ManualWhite)
                return AutoPlayColorResolution.Known("white", AutoPlayColorStatus.ManualWhite);
            if (!IsFoxMode(syncMode))
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.UnsupportedPlatform);
            if (IsSpectating(foxWindowContext))
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.Spectating);
            if (!IsPlaying(foxWindowContext))
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);
            if (string.IsNullOrWhiteSpace(savedNicknameSignature))
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.Unconfigured);

            return detected ?? AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);
        }

        private static bool IsFoxMode(SyncMode syncMode)
        {
            return syncMode == SyncMode.Fox || syncMode == SyncMode.FoxBackgroundPlace;
        }

        private static bool IsSpectating(FoxWindowContext context)
        {
            return context != null
                && context.Kind == FoxWindowKind.LiveRoom
                && context.LiveRoomState == FoxLiveRoomState.Watching;
        }

        private static bool IsPlaying(FoxWindowContext context)
        {
            return context != null
                && context.Kind == FoxWindowKind.LiveRoom
                && context.LiveRoomState == FoxLiveRoomState.Playing;
        }
    }
}
