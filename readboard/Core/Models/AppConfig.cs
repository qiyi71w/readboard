using System;

namespace readboard
{
    internal sealed class AppConfig
    {
        internal const int ClassicUiThemeMode = 0;
        internal const int OptimizedUiThemeMode = 1;

        internal const int ColorModeSystem = 0;
        internal const int ColorModeDark = 1;
        internal const int ColorModeLight = 2;

        internal const int MinMoveVerifyMaxAttempts = 1;
        internal const int MaxMoveVerifyMaxAttempts = 10;
        internal const int DefaultMoveVerifyMaxAttempts = 1;

        public string ProtocolVersion { get; set; }
        public string MachineKey { get; set; }
        public int BlackOffset { get; set; }
        public int WhiteOffset { get; set; }
        public int BlackPercent { get; set; }
        public int WhitePercent { get; set; }
        public bool UseMagnifier { get; set; }
        public bool VerifyMove { get; set; }
        public int MoveVerifyMaxAttempts { get; set; }
        public bool ShowScaleHint { get; set; }
        public bool ShowInBoard { get; set; }
        public bool ShowInBoardHint { get; set; }
        public bool AutoMinimize { get; set; }
        public int SyncIntervalMs { get; set; }
        public int GrayOffset { get; set; }
        public bool UseEnhanceScreen { get; set; }
        public bool PlayPonder { get; set; }
        public bool DisableShowInBoardShortcut { get; set; }
        public bool DebugDiagnosticsEnabled { get; set; }
        public int UiThemeMode { get; set; }
        public int ColorMode { get; set; }
        public SyncMode SyncMode { get; set; }
        public bool SyncBoth { get; set; }
        public int BoardWidth { get; set; }
        public int BoardHeight { get; set; }
        public int CustomBoardWidth { get; set; }
        public int CustomBoardHeight { get; set; }
        public int WindowPosX { get; set; }
        public int WindowPosY { get; set; }
        public AutoPlayColorMode AutoPlayColorMode { get; set; }
        public string FoxAutoPlayNickname { get; set; }
        public string FoxAutoPlayNicknameSignature { get; set; }

        public static AppConfig CreateDefault(string protocolVersion, string machineKey)
        {
            return new AppConfig
            {
                ProtocolVersion = protocolVersion,
                MachineKey = machineKey,
                BlackOffset = 96,
                WhiteOffset = 96,
                BlackPercent = 33,
                WhitePercent = 33,
                UseMagnifier = true,
                VerifyMove = true,
                MoveVerifyMaxAttempts = DefaultMoveVerifyMaxAttempts,
                ShowScaleHint = true,
                ShowInBoard = false,
                ShowInBoardHint = true,
                AutoMinimize = true,
                SyncIntervalMs = 200,
                GrayOffset = 50,
                UseEnhanceScreen = false,
                PlayPonder = true,
                DisableShowInBoardShortcut = false,
                DebugDiagnosticsEnabled = false,
                UiThemeMode = OptimizedUiThemeMode,
                ColorMode = ColorModeSystem,
                SyncMode = SyncMode.Fox,
                SyncBoth = false,
                BoardWidth = 19,
                BoardHeight = 19,
                CustomBoardWidth = -1,
                CustomBoardHeight = -1,
                WindowPosX = -1,
                WindowPosY = -1,
                AutoPlayColorMode = AutoPlayColorMode.ManualBlack,
                FoxAutoPlayNickname = string.Empty,
                FoxAutoPlayNicknameSignature = string.Empty
            };
        }

        public AppConfig Clone()
        {
            return (AppConfig)MemberwiseClone();
        }

        internal static int NormalizeMoveVerifyMaxAttempts(int value)
        {
            if (value < MinMoveVerifyMaxAttempts)
                return MinMoveVerifyMaxAttempts;
            if (value > MaxMoveVerifyMaxAttempts)
                return MaxMoveVerifyMaxAttempts;
            return value;
        }

        internal static int ResolveMoveVerifyTotalPlacementAttempts(int value)
        {
            // This setting is the maximum total placement attempts, including the initial click.
            // A value of 1 means no extra retry; 2 means the initial click plus one retry.
            return NormalizeMoveVerifyMaxAttempts(value);
        }

        internal static int ResolveMoveVerifyTotalPlacementAttempts(int? value)
        {
            return ResolveMoveVerifyTotalPlacementAttempts(value ?? DefaultMoveVerifyMaxAttempts);
        }
    }
}
