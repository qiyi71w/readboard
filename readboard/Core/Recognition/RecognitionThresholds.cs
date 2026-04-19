namespace readboard
{
    internal sealed class RecognitionThresholds
    {
        public const int DefaultBlackPercent = 33;
        public const int DefaultWhitePercent = 33;
        public const int DefaultBlackOffset = 96;
        public const int DefaultWhiteOffset = 96;
        public const int DefaultGrayOffset = 50;
        public const int DefaultRedBlueMarkerThreshold = 1;
        private const int NativeBlackPercent = 37;
        private const int NativeWhitePercent = 30;
        private const int NativeBlackOffset = 96;
        private const int NativeWhiteOffset = 112;
        private const int NativeTygemWhiteOffset = 80;
        private const int NativeGrayOffset = 50;

        public RecognitionThresholds()
        {
            BlackPercent = DefaultBlackPercent;
            WhitePercent = DefaultWhitePercent;
            BlackOffset = DefaultBlackOffset;
            WhiteOffset = DefaultWhiteOffset;
            GrayOffset = DefaultGrayOffset;
            RedBlueMarkerThreshold = DefaultRedBlueMarkerThreshold;
        }

        public int BlackPercent { get; set; }
        public int WhitePercent { get; set; }
        public int BlackOffset { get; set; }
        public int WhiteOffset { get; set; }
        public int GrayOffset { get; set; }
        public int RedBlueMarkerThreshold { get; set; }

        public RecognitionThresholds Clone()
        {
            return (RecognitionThresholds)MemberwiseClone();
        }

        public static RecognitionThresholds CreateDefault()
        {
            return new RecognitionThresholds();
        }

        public static RecognitionThresholds GetEffective(RecognitionThresholds thresholds, SyncMode syncMode)
        {
            RecognitionThresholds effective = thresholds != null
                ? thresholds.Clone()
                : CreateDefault();

            effective.BlackPercent = NormalizePositive(effective.BlackPercent, DefaultBlackPercent);
            effective.WhitePercent = NormalizePositive(effective.WhitePercent, DefaultWhitePercent);
            effective.BlackOffset = NormalizePositive(effective.BlackOffset, DefaultBlackOffset);
            effective.WhiteOffset = NormalizePositive(effective.WhiteOffset, DefaultWhiteOffset);
            effective.GrayOffset = NormalizePositive(effective.GrayOffset, DefaultGrayOffset);
            effective.RedBlueMarkerThreshold = NormalizePositive(
                effective.RedBlueMarkerThreshold,
                DefaultRedBlueMarkerThreshold);

            if (UsesNativeThresholds(syncMode))
                ApplyNativeDefaults(effective, syncMode);

            return effective;
        }

        private static void ApplyNativeDefaults(RecognitionThresholds thresholds, SyncMode syncMode)
        {
            thresholds.BlackPercent = NativeBlackPercent;
            thresholds.WhitePercent = NativeWhitePercent;
            thresholds.BlackOffset = NativeBlackOffset;
            thresholds.WhiteOffset = syncMode == SyncMode.Tygem
                ? NativeTygemWhiteOffset
                : NativeWhiteOffset;
            thresholds.GrayOffset = NativeGrayOffset;
        }

        private static bool UsesNativeThresholds(SyncMode syncMode)
        {
            return syncMode != SyncMode.Background && syncMode != SyncMode.Foreground;
        }

        private static int NormalizePositive(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }
    }
}
