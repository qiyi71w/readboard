using System;
using System.Collections.Generic;

namespace readboard
{
    internal enum ControlCenterIntentKind
    {
        SetPlatform = 0,
        SetBoardSize = 1,
        SetCustomBoardWidth = 2,
        SetCustomBoardHeight = 3,
        SetTwoWaySync = 4,
        SetShowOnBoard = 5,
        SetAutoPlayEnabled = 6,
        SetAutoPlayColor = 7,
        SetAutoPlayMoveMode = 8,
        SetAiTime = 9,
        SetPlayouts = 10,
        SetFirstPolicy = 11
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
        public bool Enabled { get; private set; }
        public AutoPlayColorMode AutoPlayColorMode { get; private set; }
        public AutoPlayMoveMode AutoPlayMoveMode { get; private set; }
        public string Value { get; private set; }

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

        public static ControlCenterIntent SetTwoWaySync(bool enabled)
        {
            return new ControlCenterIntent(ControlCenterIntentKind.SetTwoWaySync)
            {
                Enabled = enabled
            };
        }

        public static ControlCenterIntent SetShowOnBoard(bool enabled)
        {
            return new ControlCenterIntent(ControlCenterIntentKind.SetShowOnBoard)
            {
                Enabled = enabled
            };
        }

        public static ControlCenterIntent SetAutoPlayEnabled(bool enabled)
        {
            return new ControlCenterIntent(ControlCenterIntentKind.SetAutoPlayEnabled)
            {
                Enabled = enabled
            };
        }

        public static ControlCenterIntent SetAutoPlayColor(AutoPlayColorMode mode)
        {
            return new ControlCenterIntent(ControlCenterIntentKind.SetAutoPlayColor)
            {
                AutoPlayColorMode = mode
            };
        }

        public static ControlCenterIntent SetAutoPlayMoveMode(AutoPlayMoveMode mode)
        {
            return new ControlCenterIntent(ControlCenterIntentKind.SetAutoPlayMoveMode)
            {
                AutoPlayMoveMode = mode
            };
        }

        public static ControlCenterIntent SetAiTime(string value)
        {
            return new ControlCenterIntent(ControlCenterIntentKind.SetAiTime)
            {
                Value = value
            };
        }

        public static ControlCenterIntent SetPlayouts(string value)
        {
            return new ControlCenterIntent(ControlCenterIntentKind.SetPlayouts)
            {
                Value = value
            };
        }

        public static ControlCenterIntent SetFirstPolicy(string value)
        {
            return new ControlCenterIntent(ControlCenterIntentKind.SetFirstPolicy)
            {
                Value = value
            };
        }
    }

    internal sealed class ControlCenterSessionState
    {
        public bool AutoPlayEnabled { get; set; }
        public string AiTimeValue { get; set; }
        public string PlayoutsValue { get; set; }
        public string FirstPolicyValue { get; set; }
        public string FoxAutoPlayNicknameSignature { get; set; }
        public FoxWindowContext FoxWindowContext { get; set; }
        public YikeWindowContext YikeWindowContext { get; set; }
        public AutoPlayColorResolution DetectedAutoPlayColor { get; set; }
        public bool? TargetWindowValid { get; set; }
        public bool BoardRegionRecognized { get; set; }
        public bool PlacementRegionResolved { get; set; }
        public bool QuickSyncActive { get; set; }
        public bool ContinuousSyncActive { get; set; }
        public bool AnalysisRunning { get; set; }
        public bool AnalysisStateAvailable { get; set; }
        public string LastSync { get; set; }
        public int StoneCount { get; set; }
        public string Duration { get; set; }
        public MainWindowTitleTurn TitleTurn { get; set; }
        public bool HostConnected { get; set; }

        public ControlCenterSessionState()
        {
            AiTimeValue = string.Empty;
            PlayoutsValue = string.Empty;
            FirstPolicyValue = string.Empty;
            FoxAutoPlayNicknameSignature = string.Empty;
            FoxWindowContext = FoxWindowContext.Unknown();
            YikeWindowContext = YikeWindowContext.Unknown();
            LastSync = null;
            Duration = null;
        }

        public ControlCenterSessionState Clone()
        {
            return new ControlCenterSessionState
            {
                AutoPlayEnabled = AutoPlayEnabled,
                AiTimeValue = AiTimeValue,
                PlayoutsValue = PlayoutsValue,
                FirstPolicyValue = FirstPolicyValue,
                FoxAutoPlayNicknameSignature = FoxAutoPlayNicknameSignature,
                FoxWindowContext = global::readboard.FoxWindowContext.CopyOf(this.FoxWindowContext),
                YikeWindowContext = global::readboard.YikeWindowContext.CopyOf(this.YikeWindowContext),
                DetectedAutoPlayColor = CopyOf(DetectedAutoPlayColor),
                TargetWindowValid = TargetWindowValid,
                BoardRegionRecognized = BoardRegionRecognized,
                PlacementRegionResolved = PlacementRegionResolved,
                QuickSyncActive = QuickSyncActive,
                ContinuousSyncActive = ContinuousSyncActive,
                AnalysisRunning = AnalysisRunning,
                AnalysisStateAvailable = AnalysisStateAvailable,
                LastSync = LastSync,
                StoneCount = StoneCount,
                Duration = Duration,
                TitleTurn = TitleTurn,
                HostConnected = HostConnected
            };
        }

        private static AutoPlayColorResolution CopyOf(AutoPlayColorResolution resolution)
        {
            if (resolution == null)
                return null;
            return resolution.IsKnown
                ? AutoPlayColorResolution.Known(resolution.PlayColor, resolution.Status)
                : AutoPlayColorResolution.Unknown(resolution.Status);
        }

        internal static ControlCenterSessionState FromLaunchOptions(LaunchOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            return new ControlCenterSessionState
            {
                AiTimeValue = NormalizeLaunchValue(options.AiTime),
                PlayoutsValue = NormalizeLaunchValue(options.Playouts),
                FirstPolicyValue = NormalizeLaunchValue(options.FirstPolicy)
            };
        }

        private static string NormalizeLaunchValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) || value == " "
                ? string.Empty
                : value.Trim();
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
        public bool TwoWaySync { get; set; }
        public bool ShowOnBoard { get; set; }
        public AutoPlayColorMode AutoPlayColorMode { get; set; }
        public AutoPlayMoveMode AutoPlayMoveMode { get; set; }

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
                CustomBoardHeight = config.CustomBoardHeight,
                TwoWaySync = config.SyncBoth,
                ShowOnBoard = config.ShowInBoard,
                AutoPlayColorMode = AppConfig.NormalizeAutoPlayColorMode(config.AutoPlayColorMode),
                AutoPlayMoveMode = AppConfig.NormalizeAutoPlayMoveMode(config.AutoPlayMoveMode)
            };
            if (!UsesManualSelection(platform)
                && preferences.BoardSizeKind == ControlCenterBoardSizeKind.Custom)
            {
                preferences.BoardSizeKind = ControlCenterBoardSizeKind.Preset19;
                preferences.BoardWidth = 19;
                preferences.BoardHeight = 19;
            }
            if (!SupportsShowOnBoard(platform))
                preferences.ShowOnBoard = false;
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
            config.SyncBoth = TwoWaySync;
            config.ShowInBoard = ShowOnBoard;
            config.AutoPlayColorMode = AutoPlayColorMode;
            config.AutoPlayMoveMode = AutoPlayMoveMode;
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

        internal static bool SupportsShowOnBoard(SyncMode platform)
        {
            return platform != SyncMode.Foreground;
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
                && left.CustomBoardHeight == right.CustomBoardHeight
                && left.TwoWaySync == right.TwoWaySync
                && left.ShowOnBoard == right.ShowOnBoard
                && left.AutoPlayColorMode == right.AutoPlayColorMode
                && left.AutoPlayMoveMode == right.AutoPlayMoveMode;
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
        public bool TwoWaySync { get; set; }
        public bool ShowOnBoard { get; set; }
        public bool AutoPlayEnabled { get; set; }
        public AutoPlayColorMode AutoPlayColorMode { get; set; }
        public AutoPlayMoveMode AutoPlayMoveMode { get; set; }
        public AutoPlayColorResolution AutoPlayColorResolution { get; set; }
        public string PlayColor { get; set; }
        public AutoPlayColorStatus AutoPlayColorStatus { get; set; }
        public string AiTimeValue { get; set; }
        public string PlayoutsValue { get; set; }
        public string FirstPolicyValue { get; set; }
        public bool? TargetWindowValid { get; set; }
        public FoxWindowContext FoxWindowContext { get; set; }
        public YikeWindowContext YikeWindowContext { get; set; }
        public bool BoardRegionRecognized { get; set; }
        public bool PlacementRegionResolved { get; set; }
        public bool QuickSyncActive { get; set; }
        public bool ContinuousSyncActive { get; set; }
        public bool QuickSyncEnabled { get; set; }
        public bool ContinuousSyncEnabled { get; set; }
        public bool OneTimeSyncEnabled { get; set; }
        public bool AnalysisRunning { get; set; }
        public bool AnalysisStateAvailable { get; set; }
        public bool AnalysisToggleEnabled { get; set; }
        public bool SwapOrderEnabled { get; set; }
        public bool ForceRebuildEnabled { get; set; }
        public bool ClearBoardEnabled { get; set; }
        public bool BoardSelectionInsideEnabled { get; set; }
        public bool BoardSelectionRectangleEnabled { get; set; }
        public bool BoardSelectionLine1Enabled { get; set; }
        public string LastSync { get; set; }
        public int StoneCount { get; set; }
        public string Duration { get; set; }
        public MainWindowTitleTurn TitleTurn { get; set; }
        public bool HostConnected { get; set; }
        public long SessionObservationGeneration { get; set; }
        public bool ConfigurationEnabled { get; set; }
        public bool CustomBoardSizeEnabled { get; set; }
        public bool CustomBoardDimensionsEnabled { get; set; }
        public bool TwoWaySyncEnabled { get; set; }
        public bool ShowOnBoardEnabled { get; set; }
        public bool AutoPlayToggleEnabled { get; set; }
        public bool AutoPlayControlsEnabled { get; set; }
        public bool ManualColorEnabled { get; set; }
        public bool FoxAutoColorEnabled { get; set; }
        public bool MoveModeEnabled { get; set; }
        public bool AiTimeEnabled { get; set; }
        public bool PlayoutsEnabled { get; set; }
        public bool FirstPolicyEnabled { get; set; }
        public bool IdentityEnabled { get; set; }
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

    internal enum ControlCenterSessionEffectKind
    {
        SendBothSync = 0,
        SendForegroundFoxInBoard = 1,
        SendNotInBoard = 2,
        ShowOnBoardHint = 3,
        ResendSyncSessionState = 4
    }

    internal sealed class ControlCenterSessionEffect
    {
        private ControlCenterSessionEffect(
            ControlCenterSessionEffectKind kind,
            bool enabled)
        {
            Kind = kind;
            Enabled = enabled;
        }

        public ControlCenterSessionEffectKind Kind { get; private set; }
        public bool Enabled { get; private set; }

        public static ControlCenterSessionEffect SendBothSync(bool enabled)
        {
            return new ControlCenterSessionEffect(
                ControlCenterSessionEffectKind.SendBothSync,
                enabled);
        }

        public static ControlCenterSessionEffect SendForegroundFoxInBoard(bool enabled)
        {
            return new ControlCenterSessionEffect(
                ControlCenterSessionEffectKind.SendForegroundFoxInBoard,
                enabled);
        }

        public static ControlCenterSessionEffect SendNotInBoard()
        {
            return new ControlCenterSessionEffect(
                ControlCenterSessionEffectKind.SendNotInBoard,
                false);
        }

        public static ControlCenterSessionEffect ShowOnBoardHint()
        {
            return new ControlCenterSessionEffect(
                ControlCenterSessionEffectKind.ShowOnBoardHint,
                false);
        }

        public static ControlCenterSessionEffect ResendSyncSessionState()
        {
            return new ControlCenterSessionEffect(
                ControlCenterSessionEffectKind.ResendSyncSessionState,
                false);
        }
    }

    internal static class ControlCenterSessionEffectPlanner
    {
        public static IList<ControlCenterSessionEffect> PlanTwoWaySync(
            ControlCenterPreferences preferences,
            bool canUseForegroundFoxInBoardProtocol)
        {
            if (preferences == null)
                throw new ArgumentNullException("preferences");

            List<ControlCenterSessionEffect> effects = new List<ControlCenterSessionEffect>
            {
                ControlCenterSessionEffect.SendBothSync(preferences.TwoWaySync)
            };
            if (preferences.ShowOnBoard && canUseForegroundFoxInBoardProtocol)
                effects.Add(ControlCenterSessionEffect.SendForegroundFoxInBoard(preferences.TwoWaySync));
            effects.Add(ControlCenterSessionEffect.ResendSyncSessionState());
            return effects;
        }

        public static IList<ControlCenterSessionEffect> PlanShowOnBoard(
            bool enabled,
            bool twoWaySync,
            bool canUseForegroundFoxInBoardProtocol,
            bool showInBoardHint)
        {
            List<ControlCenterSessionEffect> effects = new List<ControlCenterSessionEffect>();
            if (canUseForegroundFoxInBoardProtocol)
                effects.Add(ControlCenterSessionEffect.SendForegroundFoxInBoard(enabled && twoWaySync));
            if (enabled)
            {
                if (showInBoardHint)
                    effects.Add(ControlCenterSessionEffect.ShowOnBoardHint());
            }
            else
            {
                effects.Add(ControlCenterSessionEffect.SendNotInBoard());
            }
            return effects;
        }
    }


    internal interface IControlCenterSessionAdapter
    {
        bool HasActiveSyncOperation { get; }
        void Apply(
            ControlCenterPreferences preferences,
            ControlCenterSessionState sessionState);
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
        private readonly IControlCenterActionAdapter actionAdapter;
        private readonly object observationSyncRoot = new object();
        private ControlCenterPreferences preferences;
        private ControlCenterSessionState sessionState;
        private bool preferencesSaved;
        private string persistenceError;
        private long sessionObservationGeneration;
        private string lastAppliedObservationFingerprint;

        public ControlCenterRuntime(
            ControlCenterPreferences initialPreferences,
            IControlCenterSessionAdapter sessionAdapter,
            IControlCenterPreferencePersistence persistence,
            IControlCenterActionAdapter actionAdapter)
            : this(
                initialPreferences,
                new ControlCenterSessionState(),
                sessionAdapter,
                persistence,
                actionAdapter)
        {
        }

        public ControlCenterRuntime(
            ControlCenterPreferences initialPreferences,
            ControlCenterSessionState initialSessionState,
            IControlCenterSessionAdapter sessionAdapter,
            IControlCenterPreferencePersistence persistence,
            IControlCenterActionAdapter actionAdapter)
        {
            preferences = initialPreferences == null
                ? throw new ArgumentNullException("initialPreferences")
                : initialPreferences.Clone();
            sessionState = initialSessionState == null
                ? throw new ArgumentNullException("initialSessionState")
                : initialSessionState.Clone();
            if (!ControlCenterPreferences.SupportsShowOnBoard(preferences.Platform))
                preferences.ShowOnBoard = false;
            if (!preferences.TwoWaySync)
            {
                sessionState.AutoPlayEnabled = false;
                sessionState.FoxWindowContext = FoxWindowContext.Unknown();
                sessionState.DetectedAutoPlayColor = null;
            }
            preferences.AutoPlayColorMode = AppConfig.NormalizeAutoPlayColorMode(preferences.AutoPlayColorMode);
            preferences.AutoPlayMoveMode = AppConfig.NormalizeAutoPlayMoveMode(preferences.AutoPlayMoveMode);
            this.sessionAdapter = sessionAdapter ?? throw new ArgumentNullException("sessionAdapter");
            this.persistence = persistence ?? throw new ArgumentNullException("persistence");
            this.actionAdapter = actionAdapter ?? throw new ArgumentNullException("actionAdapter");
            preferencesSaved = true;
        }

        public ControlCenterPreferences CurrentPreferences
        {
            get { return preferences.Clone(); }
        }

        public ControlCenterSessionState CurrentSessionState
        {
            get { return sessionState.Clone(); }
        }

        public ControlCenterRuntimeSnapshot Snapshot
        {
            get { return BuildSnapshot(); }
        }

        public long SessionObservationGeneration
        {
            get
            {
                lock (observationSyncRoot)
                    return sessionObservationGeneration;
            }
        }

        public long BeginSessionObservationGeneration()
        {
            lock (observationSyncRoot)
                return ++sessionObservationGeneration;
        }

        public long CaptureSessionObservationGeneration()
        {
            lock (observationSyncRoot)
                return sessionObservationGeneration;
        }

        public ControlCenterSessionObservationApplyResult ApplyObservation(
            ControlCenterSessionObservation observation)
        {
            if (observation == null)
                throw new ArgumentNullException("observation");

            lock (observationSyncRoot)
            {
                if (observation.Generation < sessionObservationGeneration)
                    return new ControlCenterSessionObservationApplyResult(
                        ControlCenterSessionObservationApplyOutcome.Stale,
                        BuildSnapshot(),
                        new List<SemanticMessage>());

                if (observation.Generation > sessionObservationGeneration)
                    sessionObservationGeneration = observation.Generation;

                if (string.Equals(
                    lastAppliedObservationFingerprint,
                    observation.Fingerprint,
                    StringComparison.Ordinal)
                    && ObservationMatchesCurrentState(observation))
                {
                    return new ControlCenterSessionObservationApplyResult(
                        ControlCenterSessionObservationApplyOutcome.NoOp,
                        BuildSnapshot(),
                        new List<SemanticMessage>());
                }

                bool changed = false;
                if (observation.HasTargetWindowValid)
                    changed |= SetIfDifferent(
                        sessionState.TargetWindowValid,
                        observation.TargetWindowValid,
                        delegate { sessionState.TargetWindowValid = observation.TargetWindowValid; });
                if (observation.HasFoxWindowContext)
                {
                    bool foxRoomContextChanged = !AreSameFoxRoomIdentityContext(
                        sessionState.FoxWindowContext,
                        observation.FoxWindowContext);
                    bool foxContextChanged = SetIfDifferent(
                        sessionState.FoxWindowContext,
                        observation.FoxWindowContext,
                        AreSameFoxWindowContext,
                        delegate { sessionState.FoxWindowContext = observation.FoxWindowContext; });
                    changed |= foxContextChanged;
                    if (foxRoomContextChanged && sessionState.DetectedAutoPlayColor != null)
                    {
                        sessionState.DetectedAutoPlayColor = null;
                        changed = true;
                    }
                }
                if (observation.HasYikeWindowContext)
                    changed |= SetIfDifferent(
                        sessionState.YikeWindowContext,
                        observation.YikeWindowContext,
                        AreSameYikeWindowContext,
                        delegate { sessionState.YikeWindowContext = observation.YikeWindowContext; });
                if (observation.HasBoardRegion)
                    changed |= SetIfDifferent(
                        sessionState.BoardRegionRecognized,
                        observation.BoardRegionRecognized,
                        delegate { sessionState.BoardRegionRecognized = observation.BoardRegionRecognized; });
                if (observation.HasPlacementRegion)
                    changed |= SetIfDifferent(
                        sessionState.PlacementRegionResolved,
                        observation.PlacementRegionResolved,
                        delegate { sessionState.PlacementRegionResolved = observation.PlacementRegionResolved; });
                if (observation.HasSyncActivity)
                {
                    changed |= SetIfDifferent(
                        sessionState.QuickSyncActive,
                        observation.QuickSyncActive,
                        delegate { sessionState.QuickSyncActive = observation.QuickSyncActive; });
                    changed |= SetIfDifferent(
                        sessionState.ContinuousSyncActive,
                        observation.ContinuousSyncActive,
                        delegate { sessionState.ContinuousSyncActive = observation.ContinuousSyncActive; });
                }
                if (observation.HasAnalysisState)
                {
                    changed |= SetIfDifferent(
                        sessionState.AnalysisRunning,
                        observation.AnalysisRunning,
                        delegate { sessionState.AnalysisRunning = observation.AnalysisRunning; });
                    changed |= SetIfDifferent(
                        sessionState.AnalysisStateAvailable,
                        observation.AnalysisStateAvailable,
                        delegate { sessionState.AnalysisStateAvailable = observation.AnalysisStateAvailable; });
                }
                if (observation.HasRecentSync)
                {
                    changed |= SetIfDifferent(
                        sessionState.LastSync,
                        observation.LastSync,
                        delegate { sessionState.LastSync = observation.LastSync; });
                    changed |= SetIfDifferent(
                        sessionState.StoneCount,
                        observation.StoneCount,
                        delegate { sessionState.StoneCount = observation.StoneCount; });
                    changed |= SetIfDifferent(
                        sessionState.Duration,
                        observation.Duration,
                        delegate { sessionState.Duration = observation.Duration; });
                }
                if (observation.HasTitleTurn)
                    changed |= SetIfDifferent(
                        sessionState.TitleTurn,
                        observation.TitleTurn,
                        delegate { sessionState.TitleTurn = observation.TitleTurn; });
                if (observation.HasHostConnected)
                    changed |= SetIfDifferent(
                        sessionState.HostConnected,
                        observation.HostConnected,
                        delegate { sessionState.HostConnected = observation.HostConnected; });

                if (!changed && observation.SemanticMessages.Count == 0)
                    return new ControlCenterSessionObservationApplyResult(
                        ControlCenterSessionObservationApplyOutcome.NoOp,
                        BuildSnapshot(),
                        new List<SemanticMessage>());

                lastAppliedObservationFingerprint = observation.Fingerprint;
                return new ControlCenterSessionObservationApplyResult(
                    ControlCenterSessionObservationApplyOutcome.Applied,
                    BuildSnapshot(),
                    observation.SemanticMessages);
            }
        }

        private bool ObservationMatchesCurrentState(ControlCenterSessionObservation observation)
        {
            if (observation.HasTargetWindowValid
                && !EqualityComparer<bool?>.Default.Equals(
                    sessionState.TargetWindowValid,
                    observation.TargetWindowValid))
                return false;
            if (observation.HasFoxWindowContext
                && !AreSameFoxWindowContext(
                    sessionState.FoxWindowContext,
                    observation.FoxWindowContext))
                return false;
            if (observation.HasYikeWindowContext
                && !AreSameYikeWindowContext(
                    sessionState.YikeWindowContext,
                    observation.YikeWindowContext))
                return false;
            if (observation.HasBoardRegion
                && (sessionState.BoardRegionRecognized != observation.BoardRegionRecognized
                    || sessionState.PlacementRegionResolved != observation.PlacementRegionResolved))
                return false;
            if (observation.HasSyncActivity
                && (sessionState.QuickSyncActive != observation.QuickSyncActive
                    || sessionState.ContinuousSyncActive != observation.ContinuousSyncActive))
                return false;
            if (observation.HasAnalysisState
                && (sessionState.AnalysisRunning != observation.AnalysisRunning
                    || sessionState.AnalysisStateAvailable != observation.AnalysisStateAvailable))
                return false;
            if (observation.HasRecentSync
                && (!string.Equals(sessionState.LastSync, observation.LastSync, StringComparison.Ordinal)
                    || sessionState.StoneCount != observation.StoneCount
                    || !string.Equals(sessionState.Duration, observation.Duration, StringComparison.Ordinal)))
                return false;
            if (observation.HasTitleTurn && sessionState.TitleTurn != observation.TitleTurn)
                return false;
            if (observation.HasHostConnected && sessionState.HostConnected != observation.HostConnected)
                return false;
            return true;
        }

        public void ProjectCurrentState()
        {
            sessionAdapter.Apply(preferences.Clone(), sessionState.Clone());
        }

        public bool ApplyFoxIdentityRecognition(
            string nicknameSignature,
            FoxWindowContext foxWindowContext,
            FoxIdentityRecognitionResult recognition)
        {
            if (recognition == null
                || !recognition.Accepted
                || recognition.Snapshot == null)
                return false;

            string normalizedSignature = nicknameSignature ?? string.Empty;
            FoxWindowContext normalizedContext = FoxWindowContext.CopyOf(foxWindowContext);
            if (!string.Equals(
                    recognition.Snapshot.RoomContextSignature,
                    FoxIdentitySelection.BuildRoomContextSignature(normalizedContext),
                    StringComparison.Ordinal)
                || !string.Equals(
                    sessionState.FoxAutoPlayNicknameSignature,
                    normalizedSignature,
                    StringComparison.Ordinal)
                || !AreSameFoxRoomIdentityContext(
                    sessionState.FoxWindowContext,
                    normalizedContext))
                return false;

            AutoPlayColorResolution authorizedColor = recognition.Snapshot.DerivedAuthorization;
            if (authorizedColor != null
                && authorizedColor.IsKnown
                && !IsRecognizedFoxColor(authorizedColor))
                return false;

            if (AreSameAutoPlayColorResolution(
                    sessionState.DetectedAutoPlayColor,
                    authorizedColor))
                return false;

            sessionState.DetectedAutoPlayColor = authorizedColor;
            return true;
        }

        public bool UpdateAutoPlayObservation(
            string nicknameSignature,
            FoxWindowContext foxWindowContext,
            AutoPlayColorResolution detectedColor)
        {
            if (detectedColor != null && detectedColor.IsKnown)
                return false;

            string normalizedSignature = nicknameSignature ?? string.Empty;
            FoxWindowContext normalizedContext = FoxWindowContext.CopyOf(foxWindowContext);
            if (string.Equals(sessionState.FoxAutoPlayNicknameSignature, normalizedSignature, StringComparison.Ordinal)
                && AreSameFoxWindowContext(sessionState.FoxWindowContext, normalizedContext)
                && AreSameAutoPlayColorResolution(sessionState.DetectedAutoPlayColor, detectedColor))
                return false;

            sessionState.FoxAutoPlayNicknameSignature = normalizedSignature;
            sessionState.FoxWindowContext = normalizedContext;
            sessionState.DetectedAutoPlayColor = detectedColor;
            return true;
        }

        public bool ClearAutoPlayObservation()
        {
            if (sessionState.DetectedAutoPlayColor == null)
                return false;

            sessionState.DetectedAutoPlayColor = null;
            return true;
        }

        public ControlCenterApplyResult Apply(ControlCenterIntent intent)
        {
            if (intent == null)
                throw new ArgumentNullException("intent");

            ControlCenterPreferences candidate;
            ControlCenterSessionState sessionCandidate = sessionState.Clone();
            if (!TryBuildCandidate(intent, out candidate, sessionCandidate))
                return new ControlCenterApplyResult(
                    ControlCenterApplyOutcome.Rejected,
                    BuildSnapshot());

            if (!CanApply(intent, candidate, sessionCandidate))
                return new ControlCenterApplyResult(
                    ControlCenterApplyOutcome.Rejected,
                    BuildSnapshot());

            if (ControlCenterPreferences.Equals(preferences, candidate)
                && AreSameSessionState(sessionState, sessionCandidate))
                return new ControlCenterApplyResult(
                    ControlCenterApplyOutcome.NoOp,
                    BuildSnapshot());

            bool preferenceChanged = !ControlCenterPreferences.Equals(preferences, candidate);
            preferences = candidate;
            sessionState = sessionCandidate;
            sessionAdapter.Apply(candidate.Clone(), sessionState.Clone());
            if (preferenceChanged)
                TryPersist(candidate);
            return new ControlCenterApplyResult(
                ControlCenterApplyOutcome.Changed,
                BuildSnapshot());
        }

        public ControlCenterActionApplyResult ApplyAction(ControlCenterActionIntent intent)
        {
            if (intent == null)
                throw new ArgumentNullException("intent");

            IList<ControlCenterActionEffect> effects;
            if (!TryPlanAction(intent, out effects))
                return new ControlCenterActionApplyResult(
                    ControlCenterActionApplyOutcome.Rejected,
                    BuildSnapshot());

            for (int i = 0; i < effects.Count; i++)
            {
                ControlCenterActionExecutionOutcome execution = actionAdapter.Execute(effects[i]);
                if (execution == ControlCenterActionExecutionOutcome.Rejected)
                    return new ControlCenterActionApplyResult(
                        ControlCenterActionApplyOutcome.Rejected,
                        BuildSnapshot());
                if (execution == ControlCenterActionExecutionOutcome.NoOp)
                    return new ControlCenterActionApplyResult(
                        ControlCenterActionApplyOutcome.NoOp,
                        BuildSnapshot());
            }

            return new ControlCenterActionApplyResult(
                ControlCenterActionApplyOutcome.Accepted,
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

        private bool TryPlanAction(
            ControlCenterActionIntent intent,
            out IList<ControlCenterActionEffect> effects)
        {
            effects = new List<ControlCenterActionEffect>();
            ControlCenterRuntimeSnapshot snapshot = BuildSnapshot();
            switch (intent.Kind)
            {
                case ControlCenterActionKind.QuickSync:
                    if (!snapshot.QuickSyncEnabled)
                        return false;
                    effects.Add(snapshot.QuickSyncActive || snapshot.ContinuousSyncActive
                        ? ControlCenterActionEffect.StopSync()
                        : ControlCenterActionEffect.StartQuickSync());
                    return true;
                case ControlCenterActionKind.ContinuousSync:
                    if (!snapshot.ContinuousSyncEnabled)
                        return false;
                    effects.Add(snapshot.ContinuousSyncActive
                        ? ControlCenterActionEffect.StopSync()
                        : ControlCenterActionEffect.StartContinuousSync());
                    return true;
                case ControlCenterActionKind.OneTimeSync:
                    if (!snapshot.OneTimeSyncEnabled)
                        return false;
                    effects.Add(ControlCenterActionEffect.RunOneTimeSync());
                    return true;
                case ControlCenterActionKind.ToggleAnalysis:
                    if (!snapshot.AnalysisToggleEnabled)
                        return false;
                    effects.Add(snapshot.AnalysisRunning
                        ? ControlCenterActionEffect.PauseAnalysis()
                        : ControlCenterActionEffect.ResumeAnalysis());
                    return true;
                case ControlCenterActionKind.SwapOrder:
                    if (!snapshot.SwapOrderEnabled)
                        return false;
                    effects.Add(ControlCenterActionEffect.SwapOrder());
                    return true;
                case ControlCenterActionKind.ForceRebuild:
                    if (!snapshot.ForceRebuildEnabled)
                        return false;
                    effects.Add(ControlCenterActionEffect.ForceRebuild());
                    return true;
                case ControlCenterActionKind.ClearBoard:
                    if (!snapshot.ClearBoardEnabled)
                        return false;
                    effects.Add(ControlCenterActionEffect.ClearBoard());
                    return true;
                case ControlCenterActionKind.SelectBoard:
                    if (!IsBoardSelectionEnabled(snapshot, intent.BoardSelectionMode))
                        return false;
                    effects.Add(ControlCenterActionEffect.SelectBoard(intent.BoardSelectionMode));
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsBoardSelectionEnabled(
            ControlCenterRuntimeSnapshot snapshot,
            ControlCenterBoardSelectionMode mode)
        {
            switch (mode)
            {
                case ControlCenterBoardSelectionMode.Inside:
                    return snapshot.BoardSelectionInsideEnabled;
                case ControlCenterBoardSelectionMode.Rectangle:
                    return snapshot.BoardSelectionRectangleEnabled;
                case ControlCenterBoardSelectionMode.Line1:
                    return snapshot.BoardSelectionLine1Enabled;
                default:
                    return false;
            }
        }

        private bool TryBuildCandidate(
            ControlCenterIntent intent,
            out ControlCenterPreferences candidate,
            ControlCenterSessionState sessionCandidate)
        {
            candidate = preferences.Clone();
            switch (intent.Kind)
            {
                case ControlCenterIntentKind.SetPlatform:
                    if (!IsDefinedPlatform(intent.Platform))
                        return false;
                    candidate.Platform = intent.Platform;
                    if (candidate.Platform != preferences.Platform)
                    {
                        sessionCandidate.FoxWindowContext = FoxWindowContext.Unknown();
                        sessionCandidate.DetectedAutoPlayColor = null;
                    }
                    if (!ControlCenterPreferences.UsesManualSelection(intent.Platform)
                        && candidate.BoardSizeKind == ControlCenterBoardSizeKind.Custom)
                    {
                        candidate.BoardSizeKind = ControlCenterBoardSizeKind.Preset19;
                        candidate.BoardWidth = 19;
                        candidate.BoardHeight = 19;
                    }
                    if (!ControlCenterPreferences.SupportsShowOnBoard(intent.Platform))
                        candidate.ShowOnBoard = false;
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
                case ControlCenterIntentKind.SetTwoWaySync:
                    candidate.TwoWaySync = intent.Enabled;
                    if (!intent.Enabled)
                    {
                        sessionCandidate.AutoPlayEnabled = false;
                        sessionCandidate.FoxWindowContext = FoxWindowContext.Unknown();
                        sessionCandidate.DetectedAutoPlayColor = null;
                    }
                    return true;
                case ControlCenterIntentKind.SetShowOnBoard:
                    if (intent.Enabled && !ControlCenterPreferences.SupportsShowOnBoard(candidate.Platform))
                        return false;
                    candidate.ShowOnBoard = intent.Enabled;
                    return true;
                case ControlCenterIntentKind.SetAutoPlayEnabled:
                    if (intent.Enabled && !candidate.TwoWaySync)
                        return false;
                    sessionCandidate.AutoPlayEnabled = intent.Enabled;
                    if (!intent.Enabled)
                    {
                        sessionCandidate.FoxWindowContext = FoxWindowContext.Unknown();
                        sessionCandidate.DetectedAutoPlayColor = null;
                    }
                    return true;
                case ControlCenterIntentKind.SetAutoPlayColor:
                    if (!IsDefinedAutoPlayColorMode(intent.AutoPlayColorMode))
                        return false;
                    candidate.AutoPlayColorMode = intent.AutoPlayColorMode;
                    return true;
                case ControlCenterIntentKind.SetAutoPlayMoveMode:
                    if (!IsDefinedAutoPlayMoveMode(intent.AutoPlayMoveMode))
                        return false;
                    candidate.AutoPlayMoveMode = intent.AutoPlayMoveMode;
                    return true;
                case ControlCenterIntentKind.SetAiTime:
                    return TrySetEngineCondition(
                        intent.Value,
                        true,
                        sessionCandidate,
                        delegate(ControlCenterSessionState state, string value)
                        {
                            state.AiTimeValue = value;
                        });
                case ControlCenterIntentKind.SetPlayouts:
                    return TrySetEngineCondition(
                        intent.Value,
                        true,
                        sessionCandidate,
                        delegate(ControlCenterSessionState state, string value)
                        {
                            state.PlayoutsValue = value;
                        });
                case ControlCenterIntentKind.SetFirstPolicy:
                    return TrySetEngineCondition(
                        intent.Value,
                        true,
                        sessionCandidate,
                        delegate(ControlCenterSessionState state, string value)
                        {
                            state.FirstPolicyValue = value;
                        });
                default:
                    return false;
            }
        }

        private bool CanApply(
            ControlCenterIntent intent,
            ControlCenterPreferences candidate,
            ControlCenterSessionState sessionCandidate)
        {
            if (sessionAdapter.HasActiveSyncOperation
                && (intent.Kind == ControlCenterIntentKind.SetPlatform
                    || intent.Kind == ControlCenterIntentKind.SetBoardSize
                    || intent.Kind == ControlCenterIntentKind.SetCustomBoardWidth
                    || intent.Kind == ControlCenterIntentKind.SetCustomBoardHeight))
                return false;

            if (intent.Kind == ControlCenterIntentKind.SetAutoPlayColor)
            {
                if (!sessionCandidate.AutoPlayEnabled)
                    return false;
                if (intent.AutoPlayColorMode == AutoPlayColorMode.FoxAuto
                    && !IsFoxPlatform(candidate.Platform))
                    return false;
            }

            if (intent.Kind == ControlCenterIntentKind.SetAutoPlayMoveMode
                && !sessionCandidate.AutoPlayEnabled)
                return false;

            if ((intent.Kind == ControlCenterIntentKind.SetAiTime
                    || intent.Kind == ControlCenterIntentKind.SetPlayouts
                    || intent.Kind == ControlCenterIntentKind.SetFirstPolicy)
                && (!sessionCandidate.AutoPlayEnabled
                    || (intent.Kind == ControlCenterIntentKind.SetFirstPolicy
                        && candidate.AutoPlayMoveMode != AutoPlayMoveMode.FirstCandidate)))
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
            bool liveSyncOperationActive = sessionAdapter.HasActiveSyncOperation;
            bool configurationEnabled = !liveSyncOperationActive;
            bool quickSyncEnabled = IsFastSyncPlatform(preferences.Platform);
            bool continuousSyncEnabled = !sessionState.QuickSyncActive;
            bool oneTimeSyncEnabled = !sessionState.QuickSyncActive
                && !sessionState.ContinuousSyncActive
                && !liveSyncOperationActive;
            bool boardSelectionEnabled = oneTimeSyncEnabled;
            AutoPlayColorResolution autoPlayColor = ResolveAutoPlayColor();
            bool autoPlayEnabled = sessionState.AutoPlayEnabled;
            bool autoPlayToggleEnabled = preferences.TwoWaySync;
            bool manualColorEnabled = autoPlayEnabled;
            bool foxAutoColorEnabled = autoPlayEnabled && IsFoxPlatform(preferences.Platform);
            bool moveModeEnabled = autoPlayEnabled;
            return new ControlCenterRuntimeSnapshot
            {
                Platform = preferences.Platform,
                BoardSizeKind = preferences.BoardSizeKind,
                BoardWidth = preferences.BoardWidth,
                BoardHeight = preferences.BoardHeight,
                CustomBoardWidth = preferences.CustomBoardWidth,
                CustomBoardHeight = preferences.CustomBoardHeight,
                TwoWaySync = preferences.TwoWaySync,
                ShowOnBoard = preferences.ShowOnBoard,
                AutoPlayEnabled = autoPlayEnabled,
                AutoPlayColorMode = preferences.AutoPlayColorMode,
                AutoPlayMoveMode = preferences.AutoPlayMoveMode,
                AutoPlayColorResolution = autoPlayColor,
                FoxWindowContext = global::readboard.FoxWindowContext.CopyOf(sessionState.FoxWindowContext),
                YikeWindowContext = global::readboard.YikeWindowContext.CopyOf(sessionState.YikeWindowContext),
                PlayColor = autoPlayColor.PlayColor,
                AutoPlayColorStatus = autoPlayColor.Status,
                AiTimeValue = sessionState.AiTimeValue,
                PlayoutsValue = sessionState.PlayoutsValue,
                FirstPolicyValue = sessionState.FirstPolicyValue,
                TargetWindowValid = sessionState.TargetWindowValid,
                BoardRegionRecognized = sessionState.BoardRegionRecognized,
                PlacementRegionResolved = sessionState.PlacementRegionResolved,
                QuickSyncActive = sessionState.QuickSyncActive,
                ContinuousSyncActive = sessionState.ContinuousSyncActive,
                QuickSyncEnabled = quickSyncEnabled,
                ContinuousSyncEnabled = continuousSyncEnabled,
                OneTimeSyncEnabled = oneTimeSyncEnabled,
                AnalysisRunning = sessionState.AnalysisRunning,
                AnalysisStateAvailable = sessionState.AnalysisStateAvailable,
                AnalysisToggleEnabled = sessionState.AnalysisStateAvailable
                    || sessionState.AnalysisRunning,
                SwapOrderEnabled = true,
                ForceRebuildEnabled = true,
                ClearBoardEnabled = true,
                BoardSelectionInsideEnabled = boardSelectionEnabled
                    && !ControlCenterPreferences.UsesManualSelection(preferences.Platform),
                BoardSelectionRectangleEnabled = boardSelectionEnabled
                    && ControlCenterPreferences.UsesManualSelection(preferences.Platform),
                BoardSelectionLine1Enabled = boardSelectionEnabled
                    && ControlCenterPreferences.UsesManualSelection(preferences.Platform),
                LastSync = sessionState.LastSync,
                StoneCount = sessionState.StoneCount,
                Duration = sessionState.Duration,
                TitleTurn = sessionState.TitleTurn,
                HostConnected = sessionState.HostConnected,
                SessionObservationGeneration = sessionObservationGeneration,
                ConfigurationEnabled = configurationEnabled,
                CustomBoardSizeEnabled = configurationEnabled
                    && ControlCenterPreferences.UsesManualSelection(preferences.Platform),
                CustomBoardDimensionsEnabled = configurationEnabled
                    && ControlCenterPreferences.UsesManualSelection(preferences.Platform)
                    && preferences.BoardSizeKind == ControlCenterBoardSizeKind.Custom,
                TwoWaySyncEnabled = true,
                ShowOnBoardEnabled = ControlCenterPreferences.SupportsShowOnBoard(preferences.Platform),
                AutoPlayToggleEnabled = autoPlayToggleEnabled,
                AutoPlayControlsEnabled = autoPlayEnabled,
                ManualColorEnabled = manualColorEnabled,
                FoxAutoColorEnabled = foxAutoColorEnabled,
                MoveModeEnabled = moveModeEnabled,
                AiTimeEnabled = autoPlayEnabled,
                PlayoutsEnabled = autoPlayEnabled,
                FirstPolicyEnabled = autoPlayEnabled
                    && preferences.AutoPlayMoveMode == AutoPlayMoveMode.FirstCandidate,
                IdentityEnabled = IsFoxPlatform(preferences.Platform),
                PreferencesSaved = preferencesSaved,
                PersistenceError = persistenceError
            };
        }

        private static bool IsFastSyncPlatform(SyncMode platform)
        {
            return platform == SyncMode.Fox
                || platform == SyncMode.FoxBackgroundPlace
                || platform == SyncMode.Yike
                || platform == SyncMode.Tygem
                || platform == SyncMode.Sina;
        }

        private static bool SetIfDifferent<T>(
            T current,
            T next,
            Action setter)
        {
            if (EqualityComparer<T>.Default.Equals(current, next))
                return false;
            setter();
            return true;
        }

        private static bool SetIfDifferent<T>(
            T current,
            T next,
            Func<T, T, bool> areSame,
            Action setter)
        {
            if (areSame(current, next))
                return false;
            setter();
            return true;
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

        private static bool IsDefinedAutoPlayColorMode(AutoPlayColorMode mode)
        {
            return mode == AutoPlayColorMode.ManualBlack
                || mode == AutoPlayColorMode.ManualWhite
                || mode == AutoPlayColorMode.FoxAuto;
        }

        private static bool IsDefinedAutoPlayMoveMode(AutoPlayMoveMode mode)
        {
            return mode == AutoPlayMoveMode.FirstCandidate
                || mode == AutoPlayMoveMode.GenmoveAnalyze;
        }

        private static bool IsFoxPlatform(SyncMode platform)
        {
            return platform == SyncMode.Fox || platform == SyncMode.FoxBackgroundPlace;
        }

        private static bool TrySetEngineCondition(
            string value,
            bool allowEmpty,
            ControlCenterSessionState sessionCandidate,
            Action<ControlCenterSessionState, string> setter)
        {
            string normalized;
            if (!TryNormalizeEngineValue(value, allowEmpty, out normalized))
                return false;
            setter(sessionCandidate, normalized);
            return true;
        }

        private static bool TryNormalizeEngineValue(
            string value,
            bool allowEmpty,
            out string normalized)
        {
            normalized = string.Empty;
            if (value == null)
                return false;

            string trimmed = value.Trim();
            if (trimmed.Length == 0)
                return allowEmpty;

            int parsed;
            if (!int.TryParse(trimmed, out parsed) || parsed < (allowEmpty ? 0 : 1))
                return false;
            normalized = parsed.ToString();
            return true;
        }

        private AutoPlayColorResolution ResolveAutoPlayColor()
        {
            if (!sessionState.AutoPlayEnabled)
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);

            return FoxAutoPlayColorResolver.Resolve(
                preferences.AutoPlayColorMode,
                preferences.Platform,
                sessionState.FoxAutoPlayNicknameSignature,
                sessionState.FoxWindowContext,
                sessionState.DetectedAutoPlayColor);
        }

        private static bool AreSameSessionState(
            ControlCenterSessionState left,
            ControlCenterSessionState right)
        {
            return left.AutoPlayEnabled == right.AutoPlayEnabled
                && string.Equals(left.AiTimeValue, right.AiTimeValue, StringComparison.Ordinal)
                && string.Equals(left.PlayoutsValue, right.PlayoutsValue, StringComparison.Ordinal)
                && string.Equals(left.FirstPolicyValue, right.FirstPolicyValue, StringComparison.Ordinal)
                && string.Equals(
                    left.FoxAutoPlayNicknameSignature,
                    right.FoxAutoPlayNicknameSignature,
                    StringComparison.Ordinal)
                && AreSameFoxWindowContext(left.FoxWindowContext, right.FoxWindowContext)
                && AreSameAutoPlayColorResolution(left.DetectedAutoPlayColor, right.DetectedAutoPlayColor);
        }

        private static bool AreSameFoxWindowContext(
            FoxWindowContext left,
            FoxWindowContext right)
        {
            if (left == null || right == null)
                return left == right;

            return left.Kind == right.Kind
                && left.LiveRoomState == right.LiveRoomState
                && string.Equals(left.RoomToken, right.RoomToken, StringComparison.Ordinal)
                && left.LiveTitleMove == right.LiveTitleMove
                && left.RecordCurrentMove == right.RecordCurrentMove
                && left.RecordTotalMove == right.RecordTotalMove
                && left.RecordAtEnd == right.RecordAtEnd
                && string.Equals(left.TitleFingerprint, right.TitleFingerprint, StringComparison.Ordinal);
        }

        private static bool AreSameFoxRoomIdentityContext(
            FoxWindowContext left,
            FoxWindowContext right)
        {
            if (left == null || right == null)
                return left == right;
            if (left.Kind != right.Kind || left.LiveRoomState != right.LiveRoomState)
                return false;
            if (left.Kind == FoxWindowKind.LiveRoom)
            {
                return string.Equals(left.RoomToken, right.RoomToken, StringComparison.Ordinal);
            }
            return string.Equals(left.TitleFingerprint, right.TitleFingerprint, StringComparison.Ordinal);
        }

        private static bool AreSameYikeWindowContext(
            YikeWindowContext left,
            YikeWindowContext right)
        {
            if (left == null || right == null)
                return left == right;

            return string.Equals(left.RoomToken, right.RoomToken, StringComparison.Ordinal)
                && left.MoveNumber == right.MoveNumber;
        }

        private static bool AreSameAutoPlayColorResolution(
            AutoPlayColorResolution left,
            AutoPlayColorResolution right)
        {
            if (left == null || right == null)
                return left == right;

            return left.IsKnown == right.IsKnown
                && left.Status == right.Status
                && string.Equals(left.PlayColor, right.PlayColor, StringComparison.Ordinal);
        }

        private static bool IsRecognizedFoxColor(AutoPlayColorResolution resolution)
        {
            return resolution != null
                && resolution.IsKnown
                && ((resolution.Status == AutoPlayColorStatus.RecognizedBlack
                        && string.Equals(resolution.PlayColor, "black", StringComparison.Ordinal))
                    || (resolution.Status == AutoPlayColorStatus.RecognizedWhite
                        && string.Equals(resolution.PlayColor, "white", StringComparison.Ordinal)));
        }

    }
}
