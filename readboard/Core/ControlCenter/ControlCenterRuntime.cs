using System;

namespace readboard
{
    internal enum ControlCenterIntentKind
    {
        SetPlatform = 0,
        SetBoardSize = 1,
        SetCustomBoardWidth = 2,
        SetCustomBoardHeight = 3
    }

    internal enum ControlCenterBoardSizeKind
    {
        Preset19 = 0,
        Preset13 = 1,
        Preset9 = 2,
        Custom = 3
    }

    internal enum ControlCenterApplyOutcome
    {
        Changed = 0,
        NoOp = 1,
        Rejected = 2
    }

    internal sealed class ControlCenterIntent
    {
        private ControlCenterIntent(ControlCenterIntentKind kind)
        {
            Kind = kind;
        }

        public ControlCenterIntentKind Kind { get; private set; }
        public SyncMode Platform { get; private set; }
        public ControlCenterBoardSizeKind BoardSizeKind { get; private set; }
        public int Dimension { get; private set; }

        public static ControlCenterIntent SetPlatform(SyncMode platform)
        {
            return new ControlCenterIntent(ControlCenterIntentKind.SetPlatform)
            {
                Platform = platform
            };
        }

        public static ControlCenterIntent SetBoardSize(ControlCenterBoardSizeKind boardSizeKind)
        {
            return new ControlCenterIntent(ControlCenterIntentKind.SetBoardSize)
            {
                BoardSizeKind = boardSizeKind
            };
        }

        public static ControlCenterIntent SetCustomBoardWidth(int width)
        {
            return new ControlCenterIntent(ControlCenterIntentKind.SetCustomBoardWidth)
            {
                Dimension = width
            };
        }

        public static ControlCenterIntent SetCustomBoardHeight(int height)
        {
            return new ControlCenterIntent(ControlCenterIntentKind.SetCustomBoardHeight)
            {
                Dimension = height
            };
        }
    }

    internal sealed class ControlCenterPreferences
    {
        public SyncMode Platform { get; set; }
        public ControlCenterBoardSizeKind BoardSizeKind { get; set; }
        public int BoardWidth { get; set; }
        public int BoardHeight { get; set; }
        public int CustomBoardWidth { get; set; }
        public int CustomBoardHeight { get; set; }

        public static ControlCenterPreferences FromConfig(AppConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            string platformToken;
            SyncMode platform = TryFormatPlatform(config.SyncMode, out platformToken)
                ? config.SyncMode
                : SyncMode.Fox;
            ControlCenterPreferences preferences = new ControlCenterPreferences
            {
                Platform = platform,
                BoardSizeKind = ResolveBoardSizeKind(config.BoardWidth, config.BoardHeight),
                BoardWidth = NormalizeDimension(config.BoardWidth, 19),
                BoardHeight = NormalizeDimension(config.BoardHeight, 19),
                CustomBoardWidth = config.CustomBoardWidth,
                CustomBoardHeight = config.CustomBoardHeight
            };
            if (!UsesManualSelection(platform)
                && preferences.BoardSizeKind == ControlCenterBoardSizeKind.Custom)
            {
                preferences.BoardSizeKind = ControlCenterBoardSizeKind.Preset19;
                preferences.BoardWidth = 19;
                preferences.BoardHeight = 19;
            }
            return preferences;
        }

        public ControlCenterPreferences Clone()
        {
            return (ControlCenterPreferences)MemberwiseClone();
        }

        public int ResolveCustomBoardWidth()
        {
            return NormalizeDimension(CustomBoardWidth, BoardWidth);
        }

        public int ResolveCustomBoardHeight()
        {
            return NormalizeDimension(CustomBoardHeight, BoardHeight);
        }

        public void ApplyTo(AppConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            config.SyncMode = Platform;
            config.BoardWidth = BoardWidth;
            config.BoardHeight = BoardHeight;
            config.CustomBoardWidth = CustomBoardWidth;
            config.CustomBoardHeight = CustomBoardHeight;
        }

        internal static ControlCenterBoardSizeKind ResolveBoardSizeKind(int width, int height)
        {
            if (width == 19 && height == 19)
                return ControlCenterBoardSizeKind.Preset19;
            if (width == 13 && height == 13)
                return ControlCenterBoardSizeKind.Preset13;
            if (width == 9 && height == 9)
                return ControlCenterBoardSizeKind.Preset9;
            return ControlCenterBoardSizeKind.Custom;
        }

        internal static bool IsValidDimension(int dimension)
        {
            return dimension >= 2 && dimension <= 25;
        }

        internal static bool TryParsePlatform(string value, out SyncMode platform)
        {
            switch (value)
            {
                case "fox": platform = SyncMode.Fox; return true;
                case "foxBackground": platform = SyncMode.FoxBackgroundPlace; return true;
                case "yike": platform = SyncMode.Yike; return true;
                case "yicheng": platform = SyncMode.Tygem; return true;
                case "sina": platform = SyncMode.Sina; return true;
                case "otherBackground": platform = SyncMode.Background; return true;
                case "otherForeground": platform = SyncMode.Foreground; return true;
                default:
                    platform = default(SyncMode);
                    return false;
            }
        }

        internal static bool TryFormatPlatform(SyncMode platform, out string token)
        {
            switch (platform)
            {
                case SyncMode.Fox: token = "fox"; return true;
                case SyncMode.FoxBackgroundPlace: token = "foxBackground"; return true;
                case SyncMode.Yike: token = "yike"; return true;
                case SyncMode.Tygem: token = "yicheng"; return true;
                case SyncMode.Sina: token = "sina"; return true;
                case SyncMode.Background: token = "otherBackground"; return true;
                case SyncMode.Foreground: token = "otherForeground"; return true;
                default:
                    token = null;
                    return false;
            }
        }

        internal static string ToPlatformToken(SyncMode platform)
        {
            string token;
            if (!TryFormatPlatform(platform, out token))
                throw new ArgumentOutOfRangeException("platform");
            return token;
        }

        internal static bool TryParseBoardSize(string value, out ControlCenterBoardSizeKind boardSizeKind)
        {
            switch (value)
            {
                case "19": boardSizeKind = ControlCenterBoardSizeKind.Preset19; return true;
                case "13": boardSizeKind = ControlCenterBoardSizeKind.Preset13; return true;
                case "9": boardSizeKind = ControlCenterBoardSizeKind.Preset9; return true;
                case "custom": boardSizeKind = ControlCenterBoardSizeKind.Custom; return true;
                default:
                    boardSizeKind = default(ControlCenterBoardSizeKind);
                    return false;
            }
        }

        internal static bool TryFormatBoardSize(ControlCenterBoardSizeKind boardSizeKind, out string token)
        {
            switch (boardSizeKind)
            {
                case ControlCenterBoardSizeKind.Preset19: token = "19"; return true;
                case ControlCenterBoardSizeKind.Preset13: token = "13"; return true;
                case ControlCenterBoardSizeKind.Preset9: token = "9"; return true;
                case ControlCenterBoardSizeKind.Custom: token = "custom"; return true;
                default:
                    token = null;
                    return false;
            }
        }

        internal static string ToBoardSizeToken(ControlCenterBoardSizeKind boardSizeKind)
        {
            string token;
            if (!TryFormatBoardSize(boardSizeKind, out token))
                throw new ArgumentOutOfRangeException("boardSizeKind");
            return token;
        }

        private static int NormalizeDimension(int value, int fallback)
        {
            if (IsValidDimension(value))
                return value;
            return IsValidDimension(fallback) ? fallback : 19;
        }

        internal static bool UsesManualSelection(SyncMode platform)
        {
            return platform == SyncMode.Background || platform == SyncMode.Foreground;
        }

        internal static bool Equals(
            ControlCenterPreferences left,
            ControlCenterPreferences right)
        {
            if (left == null || right == null)
                return left == right;

            return left.Platform == right.Platform
                && left.BoardSizeKind == right.BoardSizeKind
                && left.BoardWidth == right.BoardWidth
                && left.BoardHeight == right.BoardHeight
                && left.CustomBoardWidth == right.CustomBoardWidth
                && left.CustomBoardHeight == right.CustomBoardHeight;
        }
    }

    internal sealed class ControlCenterRuntimeSnapshot
    {
        public SyncMode Platform { get; set; }
        public ControlCenterBoardSizeKind BoardSizeKind { get; set; }
        public int BoardWidth { get; set; }
        public int BoardHeight { get; set; }
        public int CustomBoardWidth { get; set; }
        public int CustomBoardHeight { get; set; }
        public bool ConfigurationEnabled { get; set; }
        public bool CustomBoardSizeEnabled { get; set; }
        public bool CustomBoardDimensionsEnabled { get; set; }
        public bool PreferencesSaved { get; set; }
        public string PersistenceError { get; set; }
    }

    internal sealed class ControlCenterApplyResult
    {
        internal ControlCenterApplyResult(
            ControlCenterApplyOutcome outcome,
            ControlCenterRuntimeSnapshot snapshot)
        {
            Outcome = outcome;
            Snapshot = snapshot;
        }

        public ControlCenterApplyOutcome Outcome { get; private set; }
        public ControlCenterRuntimeSnapshot Snapshot { get; private set; }
        public bool ShouldPublishSnapshot
        {
            get { return Outcome != ControlCenterApplyOutcome.NoOp; }
        }
    }

    internal interface IControlCenterSessionAdapter
    {
        bool HasActiveSyncOperation { get; }
        void Apply(ControlCenterPreferences preferences);
    }

    internal interface IControlCenterPreferencePersistence
    {
        void Save(ControlCenterPreferences preferences);
    }

    internal sealed class AppConfigControlCenterPreferencePersistence : IControlCenterPreferencePersistence
    {
        private readonly Func<AppConfig> getCurrentConfig;
        private readonly Action<AppConfig> saveConfig;

        public AppConfigControlCenterPreferencePersistence(
            Func<AppConfig> getCurrentConfig,
            Action<AppConfig> saveConfig)
        {
            this.getCurrentConfig = getCurrentConfig ?? throw new ArgumentNullException("getCurrentConfig");
            this.saveConfig = saveConfig ?? throw new ArgumentNullException("saveConfig");
        }

        public void Save(ControlCenterPreferences preferences)
        {
            if (preferences == null)
                throw new ArgumentNullException("preferences");

            AppConfig config = getCurrentConfig().Clone();
            preferences.ApplyTo(config);
            saveConfig(config);
        }
    }

    internal sealed class ControlCenterRuntime
    {
        private readonly IControlCenterSessionAdapter sessionAdapter;
        private readonly IControlCenterPreferencePersistence persistence;
        private ControlCenterPreferences preferences;
        private bool preferencesSaved;
        private string persistenceError;

        public ControlCenterRuntime(
            ControlCenterPreferences initialPreferences,
            IControlCenterSessionAdapter sessionAdapter,
            IControlCenterPreferencePersistence persistence)
        {
            preferences = initialPreferences == null
                ? throw new ArgumentNullException("initialPreferences")
                : initialPreferences.Clone();
            this.sessionAdapter = sessionAdapter ?? throw new ArgumentNullException("sessionAdapter");
            this.persistence = persistence ?? throw new ArgumentNullException("persistence");
            preferencesSaved = true;
        }

        public ControlCenterPreferences CurrentPreferences
        {
            get { return preferences.Clone(); }
        }

        public ControlCenterRuntimeSnapshot Snapshot
        {
            get { return BuildSnapshot(); }
        }

        public void ProjectCurrentState()
        {
            sessionAdapter.Apply(preferences.Clone());
        }

        public ControlCenterApplyResult Apply(ControlCenterIntent intent)
        {
            if (intent == null)
                throw new ArgumentNullException("intent");

            ControlCenterPreferences candidate;
            if (!TryBuildCandidate(intent, out candidate))
                return new ControlCenterApplyResult(
                    ControlCenterApplyOutcome.Rejected,
                    BuildSnapshot());

            if (ControlCenterPreferences.Equals(preferences, candidate))
                return new ControlCenterApplyResult(
                    ControlCenterApplyOutcome.NoOp,
                    BuildSnapshot());

            if (!CanApply(intent, candidate))
                return new ControlCenterApplyResult(
                    ControlCenterApplyOutcome.Rejected,
                    BuildSnapshot());

            preferences = candidate;
            sessionAdapter.Apply(candidate.Clone());
            TryPersist(candidate);
            return new ControlCenterApplyResult(
                ControlCenterApplyOutcome.Changed,
                BuildSnapshot());
        }

        public void MarkPersistenceSucceeded()
        {
            preferencesSaved = true;
            persistenceError = null;
        }

        public void MarkPersistenceFailed(Exception exception)
        {
            preferencesSaved = false;
            persistenceError = exception == null
                ? "Configuration persistence failed."
                : exception.Message;
        }

        private bool TryBuildCandidate(
            ControlCenterIntent intent,
            out ControlCenterPreferences candidate)
        {
            candidate = preferences.Clone();
            switch (intent.Kind)
            {
                case ControlCenterIntentKind.SetPlatform:
                    if (!IsDefinedPlatform(intent.Platform))
                        return false;
                    candidate.Platform = intent.Platform;
                    if (!ControlCenterPreferences.UsesManualSelection(intent.Platform)
                        && candidate.BoardSizeKind == ControlCenterBoardSizeKind.Custom)
                    {
                        candidate.BoardSizeKind = ControlCenterBoardSizeKind.Preset19;
                        candidate.BoardWidth = 19;
                        candidate.BoardHeight = 19;
                    }
                    return true;
                case ControlCenterIntentKind.SetBoardSize:
                    if (!IsDefinedBoardSize(intent.BoardSizeKind))
                        return false;
                    candidate.BoardSizeKind = intent.BoardSizeKind;
                    if (intent.BoardSizeKind == ControlCenterBoardSizeKind.Preset19)
                    {
                        candidate.BoardWidth = 19;
                        candidate.BoardHeight = 19;
                    }
                    else if (intent.BoardSizeKind == ControlCenterBoardSizeKind.Preset13)
                    {
                        candidate.BoardWidth = 13;
                        candidate.BoardHeight = 13;
                    }
                    else if (intent.BoardSizeKind == ControlCenterBoardSizeKind.Preset9)
                    {
                        candidate.BoardWidth = 9;
                        candidate.BoardHeight = 9;
                    }
                    else
                    {
                        candidate.BoardWidth = candidate.ResolveCustomBoardWidth();
                        candidate.BoardHeight = candidate.ResolveCustomBoardHeight();
                        candidate.CustomBoardWidth = candidate.BoardWidth;
                        candidate.CustomBoardHeight = candidate.BoardHeight;
                    }
                    return true;
                case ControlCenterIntentKind.SetCustomBoardWidth:
                    if (!ControlCenterPreferences.IsValidDimension(intent.Dimension))
                        return false;
                    if (candidate.BoardSizeKind != ControlCenterBoardSizeKind.Custom)
                        return false;
                    candidate.CustomBoardWidth = intent.Dimension;
                    candidate.BoardWidth = intent.Dimension;
                    return true;
                case ControlCenterIntentKind.SetCustomBoardHeight:
                    if (!ControlCenterPreferences.IsValidDimension(intent.Dimension))
                        return false;
                    if (candidate.BoardSizeKind != ControlCenterBoardSizeKind.Custom)
                        return false;
                    candidate.CustomBoardHeight = intent.Dimension;
                    candidate.BoardHeight = intent.Dimension;
                    return true;
                default:
                    return false;
            }
        }

        private bool CanApply(
            ControlCenterIntent intent,
            ControlCenterPreferences candidate)
        {
            if (sessionAdapter.HasActiveSyncOperation)
                return false;

            if ((intent.Kind == ControlCenterIntentKind.SetBoardSize
                    && candidate.BoardSizeKind == ControlCenterBoardSizeKind.Custom)
                || intent.Kind == ControlCenterIntentKind.SetCustomBoardWidth
                || intent.Kind == ControlCenterIntentKind.SetCustomBoardHeight)
                return ControlCenterPreferences.UsesManualSelection(candidate.Platform);

            return true;
        }

        private void TryPersist(ControlCenterPreferences candidate)
        {
            try
            {
                persistence.Save(candidate.Clone());
                MarkPersistenceSucceeded();
            }
            catch (Exception exception)
            {
                MarkPersistenceFailed(exception);
            }
        }

        private ControlCenterRuntimeSnapshot BuildSnapshot()
        {
            bool configurationEnabled = !sessionAdapter.HasActiveSyncOperation;
            return new ControlCenterRuntimeSnapshot
            {
                Platform = preferences.Platform,
                BoardSizeKind = preferences.BoardSizeKind,
                BoardWidth = preferences.BoardWidth,
                BoardHeight = preferences.BoardHeight,
                CustomBoardWidth = preferences.CustomBoardWidth,
                CustomBoardHeight = preferences.CustomBoardHeight,
                ConfigurationEnabled = configurationEnabled,
                CustomBoardSizeEnabled = configurationEnabled
                    && ControlCenterPreferences.UsesManualSelection(preferences.Platform),
                CustomBoardDimensionsEnabled = configurationEnabled
                    && ControlCenterPreferences.UsesManualSelection(preferences.Platform)
                    && preferences.BoardSizeKind == ControlCenterBoardSizeKind.Custom,
                PreferencesSaved = preferencesSaved,
                PersistenceError = persistenceError
            };
        }

        private static bool IsDefinedPlatform(SyncMode platform)
        {
            string token;
            return ControlCenterPreferences.TryFormatPlatform(platform, out token);
        }

        private static bool IsDefinedBoardSize(ControlCenterBoardSizeKind boardSizeKind)
        {
            string token;
            return ControlCenterPreferences.TryFormatBoardSize(boardSizeKind, out token);
        }

    }
}
