using System;
using System.Collections.Generic;
using System.Globalization;

namespace readboard
{
    internal enum SettingsDraftField
    {
        AutoMinimize = 0,
        BackgroundAnalysis = 1,
        Magnifier = 2,
        EnhancedCapture = 3,
        PlacementValidation = 4,
        SyncInterval = 5,
        GrayOffset = 6,
        BlackOffset = 7,
        BlackPercent = 8,
        WhiteOffset = 9,
        WhitePercent = 10,
        Theme = 11,
        Language = 12,
        Diagnostics = 13
    }

    internal enum SettingsDraftOperationOutcome
    {
        Applied = 0,
        Saved = 1,
        ValidationFailed = 2,
        PersistenceFailed = 3,
        DurablePersistenceFailed = 4,
        EffectsFailed = 5
    }

    internal static class SettingsDraftMessageKeys
    {
        public const string MustBeInteger = "SettingsForm_mustBeInteger";
        public const string IntegerAtLeast = "WebView_integerAtLeast";
        public const string IntegerRange = "WebView_integerRange";
        public const string InvalidChoice = "SettingsForm_invalidChoice";
        public const string SaveFailed = "WebView_settingsSaveFailed";
        public const string DurableSaveFailed = "WebView_settingsDurableSaveFailed";
        public const string EffectFailed = "WebView_settingsEffectFailed";

        private static readonly string[] all =
        {
            MustBeInteger,
            IntegerAtLeast,
            IntegerRange,
            InvalidChoice,
            SaveFailed,
            DurableSaveFailed,
            EffectFailed
        };

        public static IReadOnlyList<string> All
        {
            get { return all; }
        }
    }

    [Flags]
    internal enum SettingsDraftEffectKind
    {
        None = 0,
        Language = 1,
        Theme = 2,
        BackgroundAnalysis = 4,
        Diagnostics = 8
    }

    internal sealed class SettingsDraftEffectResult
    {
        public SettingsDraftEffectResult(
            SettingsDraftEffectKind pending,
            Exception failure)
        {
            Pending = pending;
            Failure = failure;
        }

        public SettingsDraftEffectKind Pending { get; private set; }
        public Exception Failure { get; private set; }
    }

    internal sealed class SettingsDraftUpdate
    {
        private SettingsDraftUpdate(SettingsDraftField field, bool? booleanValue, string textValue)
        {
            Field = field;
            BooleanValue = booleanValue;
            TextValue = textValue;
        }

        public SettingsDraftField Field { get; private set; }
        public bool? BooleanValue { get; private set; }
        public string TextValue { get; private set; }

        public static SettingsDraftUpdate Boolean(SettingsDraftField field, bool value)
        {
            return new SettingsDraftUpdate(field, value, null);
        }

        public static SettingsDraftUpdate Text(SettingsDraftField field, string value)
        {
            return new SettingsDraftUpdate(field, null, value);
        }
    }


    internal sealed class SettingsDraftState
    {
        public bool AutoMinimize { get; set; }
        public bool BackgroundAnalysis { get; set; }
        public bool Magnifier { get; set; }
        public bool EnhancedCapture { get; set; }
        public bool PlacementValidation { get; set; }
        public string SyncInterval { get; set; }
        public string GrayOffset { get; set; }
        public string BlackOffset { get; set; }
        public string BlackPercent { get; set; }
        public string WhiteOffset { get; set; }
        public string WhitePercent { get; set; }
        public string Theme { get; set; }
        public string Language { get; set; }
        public bool Diagnostics { get; set; }
        public bool Dirty { get; set; }
        public IDictionary<string, SemanticMessage> Errors { get; set; } =
            new Dictionary<string, SemanticMessage>();
        public SemanticMessage SaveError { get; set; }

        public static SettingsDraftState FromConfig(AppConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            return new SettingsDraftState
            {
                AutoMinimize = config.AutoMinimize,
                BackgroundAnalysis = config.PlayPonder,
                Magnifier = config.UseMagnifier,
                EnhancedCapture = config.UseEnhanceScreen,
                PlacementValidation = config.VerifyMove,
                SyncInterval = config.SyncIntervalMs.ToString(CultureInfo.InvariantCulture),
                GrayOffset = config.GrayOffset.ToString(CultureInfo.InvariantCulture),
                BlackOffset = config.BlackOffset.ToString(CultureInfo.InvariantCulture),
                BlackPercent = config.BlackPercent.ToString(CultureInfo.InvariantCulture),
                WhiteOffset = config.WhiteOffset.ToString(CultureInfo.InvariantCulture),
                WhitePercent = config.WhitePercent.ToString(CultureInfo.InvariantCulture),
                Theme = ResolveTheme(config.ColorMode),
                Language = AppConfig.NormalizeLanguagePreference(config.LanguagePreference),
                Diagnostics = config.DebugDiagnosticsEnabled,
                Dirty = false
            };
        }

        public SettingsDraftState Clone()
        {
            return new SettingsDraftState
            {
                AutoMinimize = AutoMinimize,
                BackgroundAnalysis = BackgroundAnalysis,
                Magnifier = Magnifier,
                EnhancedCapture = EnhancedCapture,
                PlacementValidation = PlacementValidation,
                SyncInterval = SyncInterval,
                GrayOffset = GrayOffset,
                BlackOffset = BlackOffset,
                BlackPercent = BlackPercent,
                WhiteOffset = WhiteOffset,
                WhitePercent = WhitePercent,
                Theme = Theme,
                Language = Language,
                Diagnostics = Diagnostics,
                Dirty = Dirty,
                Errors = new Dictionary<string, SemanticMessage>(Errors),
                SaveError = SaveError
            };
        }

        internal bool Apply(SettingsDraftUpdate update)
        {
            if (update == null)
                throw new ArgumentNullException("update");

            bool changed = false;
            switch (update.Field)
            {
                case SettingsDraftField.AutoMinimize:
                    bool nextAutoMinimize = RequireBoolean(update);
                    changed = AutoMinimize != nextAutoMinimize;
                    AutoMinimize = nextAutoMinimize;
                    break;
                case SettingsDraftField.BackgroundAnalysis:
                    bool nextBackgroundAnalysis = RequireBoolean(update);
                    changed = BackgroundAnalysis != nextBackgroundAnalysis;
                    BackgroundAnalysis = nextBackgroundAnalysis;
                    break;
                case SettingsDraftField.Magnifier:
                    bool nextMagnifier = RequireBoolean(update);
                    changed = Magnifier != nextMagnifier;
                    Magnifier = nextMagnifier;
                    break;
                case SettingsDraftField.EnhancedCapture:
                    bool nextEnhancedCapture = RequireBoolean(update);
                    changed = EnhancedCapture != nextEnhancedCapture;
                    EnhancedCapture = nextEnhancedCapture;
                    break;
                case SettingsDraftField.PlacementValidation:
                    bool nextPlacementValidation = RequireBoolean(update);
                    changed = PlacementValidation != nextPlacementValidation;
                    PlacementValidation = nextPlacementValidation;
                    break;
                case SettingsDraftField.Diagnostics:
                    bool nextDiagnostics = RequireBoolean(update);
                    changed = Diagnostics != nextDiagnostics;
                    Diagnostics = nextDiagnostics;
                    break;
                case SettingsDraftField.SyncInterval:
                    string nextSyncInterval = RequireText(update);
                    changed = !string.Equals(SyncInterval, nextSyncInterval, StringComparison.Ordinal);
                    SyncInterval = nextSyncInterval;
                    break;
                case SettingsDraftField.GrayOffset:
                    string nextGrayOffset = RequireText(update);
                    changed = !string.Equals(GrayOffset, nextGrayOffset, StringComparison.Ordinal);
                    GrayOffset = nextGrayOffset;
                    break;
                case SettingsDraftField.BlackOffset:
                    string nextBlackOffset = RequireText(update);
                    changed = !string.Equals(BlackOffset, nextBlackOffset, StringComparison.Ordinal);
                    BlackOffset = nextBlackOffset;
                    break;
                case SettingsDraftField.BlackPercent:
                    string nextBlackPercent = RequireText(update);
                    changed = !string.Equals(BlackPercent, nextBlackPercent, StringComparison.Ordinal);
                    BlackPercent = nextBlackPercent;
                    break;
                case SettingsDraftField.WhiteOffset:
                    string nextWhiteOffset = RequireText(update);
                    changed = !string.Equals(WhiteOffset, nextWhiteOffset, StringComparison.Ordinal);
                    WhiteOffset = nextWhiteOffset;
                    break;
                case SettingsDraftField.WhitePercent:
                    string nextWhitePercent = RequireText(update);
                    changed = !string.Equals(WhitePercent, nextWhitePercent, StringComparison.Ordinal);
                    WhitePercent = nextWhitePercent;
                    break;
                case SettingsDraftField.Theme:
                    string nextTheme = RequireText(update);
                    changed = !string.Equals(Theme, nextTheme, StringComparison.Ordinal);
                    Theme = nextTheme;
                    break;
                case SettingsDraftField.Language:
                    string nextLanguage = RequireText(update);
                    changed = !string.Equals(Language, nextLanguage, StringComparison.Ordinal);
                    Language = nextLanguage;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("update");
            }

            if (!changed)
                return false;

            Errors.Remove(SettingsDraftFieldNames.GetKey(update.Field));
            SaveError = null;
            Dirty = true;
            return true;
        }

        internal bool TryOverlay(AppConfig latest, out AppConfig candidate)
        {
            if (latest == null)
                throw new ArgumentNullException("latest");

            candidate = latest.Clone();
            Errors = new Dictionary<string, SemanticMessage>();
            SaveError = null;

            int syncInterval;
            int grayOffset;
            int blackOffset;
            int blackPercent;
            int whiteOffset;
            int whitePercent;
            ReadInteger(SyncInterval, "syncInterval", out syncInterval);
            ReadInteger(GrayOffset, "grayOffset", out grayOffset);
            ReadInteger(BlackOffset, "blackOffset", out blackOffset);
            ReadInteger(BlackPercent, "blackPercent", out blackPercent);
            ReadInteger(WhiteOffset, "whiteOffset", out whiteOffset);
            ReadInteger(WhitePercent, "whitePercent", out whitePercent);
            if (!Errors.ContainsKey("syncInterval") && syncInterval < 20)
                Errors["syncInterval"] = SemanticMessage.Create(
                    SettingsDraftMessageKeys.IntegerAtLeast,
                    20);
            AddRangeError(grayOffset, 0, 255, "grayOffset");
            AddRangeError(blackOffset, 0, 255, "blackOffset");
            AddRangeError(blackPercent, 0, 100, "blackPercent");
            AddRangeError(whiteOffset, 0, 255, "whiteOffset");
            AddRangeError(whitePercent, 0, 100, "whitePercent");
            if (!IsSupportedTheme(Theme))
                Errors["theme"] = SemanticMessage.Create(SettingsDraftMessageKeys.InvalidChoice);
            if (!AppConfig.IsSupportedLanguagePreference(Language))
                Errors["language"] = SemanticMessage.Create(SettingsDraftMessageKeys.InvalidChoice);
            if (Errors.Count != 0)
                return false;

            candidate.SyncIntervalMs = syncInterval;
            candidate.GrayOffset = grayOffset;
            candidate.BlackOffset = blackOffset;
            candidate.BlackPercent = blackPercent;
            candidate.WhiteOffset = whiteOffset;
            candidate.WhitePercent = whitePercent;
            candidate.AutoMinimize = AutoMinimize;
            candidate.PlayPonder = BackgroundAnalysis;
            candidate.UseMagnifier = Magnifier;
            candidate.UseEnhanceScreen = EnhancedCapture;
            candidate.VerifyMove = PlacementValidation;
            candidate.DebugDiagnosticsEnabled = Diagnostics;
            candidate.ColorMode = ResolveColorMode(Theme);
            candidate.LanguagePreference = AppConfig.NormalizeLanguagePreference(Language);
            return true;
        }

        private void ReadInteger(string value, string key, out int parsed)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                Errors[key] = SemanticMessage.Create(SettingsDraftMessageKeys.MustBeInteger);
        }

        private void AddRangeError(int value, int minimum, int maximum, string key)
        {
            if (!Errors.ContainsKey(key) && (value < minimum || value > maximum))
                Errors[key] = SemanticMessage.Create(
                    SettingsDraftMessageKeys.IntegerRange,
                    minimum,
                    maximum);
        }

        private static bool RequireBoolean(SettingsDraftUpdate update)
        {
            if (!update.BooleanValue.HasValue)
                throw new ArgumentException("A boolean update value is required.", "update");
            return update.BooleanValue.Value;
        }

        private static string RequireText(SettingsDraftUpdate update)
        {
            if (update.TextValue == null)
                throw new ArgumentException("A text update value is required.", "update");
            return update.TextValue;
        }

        private static bool IsSupportedTheme(string theme)
        {
            return theme == "system" || theme == "dark" || theme == "light";
        }

        private static string ResolveTheme(int colorMode)
        {
            if (colorMode == AppConfig.ColorModeDark)
                return "dark";
            return colorMode == AppConfig.ColorModeLight ? "light" : "system";
        }

        private static int ResolveColorMode(string theme)
        {
            if (theme == "dark")
                return AppConfig.ColorModeDark;
            return theme == "light" ? AppConfig.ColorModeLight : AppConfig.ColorModeSystem;
        }
    }

    internal static class SettingsDraftFieldNames
    {
        public static string GetKey(SettingsDraftField field)
        {
            switch (field)
            {
                case SettingsDraftField.AutoMinimize: return "autoMinimize";
                case SettingsDraftField.BackgroundAnalysis: return "backgroundAnalysis";
                case SettingsDraftField.Magnifier: return "magnifier";
                case SettingsDraftField.EnhancedCapture: return "enhancedCapture";
                case SettingsDraftField.PlacementValidation: return "placementValidation";
                case SettingsDraftField.SyncInterval: return "syncInterval";
                case SettingsDraftField.GrayOffset: return "grayOffset";
                case SettingsDraftField.BlackOffset: return "blackOffset";
                case SettingsDraftField.BlackPercent: return "blackPercent";
                case SettingsDraftField.WhiteOffset: return "whiteOffset";
                case SettingsDraftField.WhitePercent: return "whitePercent";
                case SettingsDraftField.Theme: return "theme";
                case SettingsDraftField.Language: return "language";
                case SettingsDraftField.Diagnostics: return "diagnostics";
                default: throw new ArgumentOutOfRangeException("field");
            }
        }
    }

    internal interface ISettingsDraftPersistence
    {
        AppConfig GetLatestActiveConfig();
        void Persist(AppConfig candidate);
        void ReplaceActiveConfig(AppConfig candidate);
    }

    internal interface ISettingsDraftRuntimeEffects
    {
        void ApplyLanguagePreference(string preference);
        void ApplyTheme(int colorMode);
        void ApplyBackgroundAnalysis(bool enabled);
        void ApplyDiagnostics(bool enabled);
    }

    internal sealed class SettingsDraftOperationResult
    {
        public SettingsDraftOperationResult(
            SettingsDraftOperationOutcome outcome,
            SettingsDraftState state,
            Exception failure = null,
            bool shouldPublishSnapshot = true)
        {
            Outcome = outcome;
            State = state == null ? throw new ArgumentNullException("state") : state.Clone();
            Failure = failure;
            ShouldPublishSnapshot = shouldPublishSnapshot;
        }

        public SettingsDraftOperationOutcome Outcome { get; private set; }
        public SettingsDraftState State { get; private set; }
        public Exception Failure { get; private set; }
        public bool ShouldPublishSnapshot { get; private set; }
    }


    internal sealed class SettingsDraftRuntime
    {
        private readonly Func<AppConfig> defaultFactory;
        private readonly ISettingsDraftPersistence persistence;
        private readonly ISettingsDraftRuntimeEffects effects;
        private SettingsDraftState activeSettings;
        private SettingsDraftState draft;
        private SettingsDraftEffectKind pendingEffects;

        public SettingsDraftRuntime(
            AppConfig initialActiveConfig,
            Func<AppConfig> defaultFactory,
            ISettingsDraftPersistence persistence,
            ISettingsDraftRuntimeEffects effects)
        {
            if (initialActiveConfig == null)
                throw new ArgumentNullException("initialActiveConfig");
            this.defaultFactory = defaultFactory ?? throw new ArgumentNullException("defaultFactory");
            this.persistence = persistence ?? throw new ArgumentNullException("persistence");
            this.effects = effects ?? throw new ArgumentNullException("effects");
            activeSettings = SettingsDraftState.FromConfig(initialActiveConfig);
            draft = activeSettings.Clone();
        }

        public SettingsDraftState Snapshot
        {
            get { return draft.Clone(); }
        }

        public SettingsDraftOperationResult Update(SettingsDraftUpdate update)
        {
            bool changed = draft.Apply(update);
            if (changed
                && AreSameSettingsValues(draft, activeSettings)
                && draft.Errors.Count == 0
                && draft.SaveError == null)
                draft.Dirty = false;
            return CreateResult(SettingsDraftOperationOutcome.Applied, null, changed);
        }

        public SettingsDraftOperationResult Reset()
        {
            AppConfig defaults = defaultFactory();
            if (defaults == null)
                throw new InvalidOperationException("The settings defaults factory returned null.");

            SettingsDraftState next = SettingsDraftState.FromConfig(defaults);
            next.Dirty = !AreSameSettingsValues(next, activeSettings);
            if (IsSameCleanDraft(next))
                return CreateResult(SettingsDraftOperationOutcome.Applied, null, false);

            draft = next;
            return CreateResult(SettingsDraftOperationOutcome.Applied);
        }

        public SettingsDraftOperationResult Cancel()
        {
            AppConfig latest = persistence.GetLatestActiveConfig();
            if (latest == null)
                throw new InvalidOperationException("The settings persistence adapter returned no active configuration.");

            SettingsDraftState next = SettingsDraftState.FromConfig(latest);
            activeSettings = next.Clone();
            if (IsSameCleanDraft(next))
                return CreateResult(SettingsDraftOperationOutcome.Applied, null, false);

            draft = next;
            return CreateResult(SettingsDraftOperationOutcome.Applied);
        }

        private bool IsSameCleanDraft(SettingsDraftState target)
        {
            return AreSameSettingsValues(draft, target)
                && draft.Dirty == target.Dirty
                && draft.Errors.Count == 0
                && draft.SaveError == null;
        }

        private static bool AreSameSettingsValues(
            SettingsDraftState left,
            SettingsDraftState right)
        {
            return left.AutoMinimize == right.AutoMinimize
                && left.BackgroundAnalysis == right.BackgroundAnalysis
                && left.Magnifier == right.Magnifier
                && left.EnhancedCapture == right.EnhancedCapture
                && left.PlacementValidation == right.PlacementValidation
                && string.Equals(left.SyncInterval, right.SyncInterval, StringComparison.Ordinal)
                && string.Equals(left.GrayOffset, right.GrayOffset, StringComparison.Ordinal)
                && string.Equals(left.BlackOffset, right.BlackOffset, StringComparison.Ordinal)
                && string.Equals(left.BlackPercent, right.BlackPercent, StringComparison.Ordinal)
                && string.Equals(left.WhiteOffset, right.WhiteOffset, StringComparison.Ordinal)
                && string.Equals(left.WhitePercent, right.WhitePercent, StringComparison.Ordinal)
                && string.Equals(left.Theme, right.Theme, StringComparison.Ordinal)
                && string.Equals(left.Language, right.Language, StringComparison.Ordinal)
                && left.Diagnostics == right.Diagnostics;
        }

        public SettingsDraftOperationResult Save()
        {
            AppConfig latest = persistence.GetLatestActiveConfig();
            if (latest == null)
                throw new InvalidOperationException("The settings persistence adapter returned no active configuration.");
            AppConfig candidate;
            if (!draft.TryOverlay(latest, out candidate))
                return CreateResult(SettingsDraftOperationOutcome.ValidationFailed);

            SettingsDraftState latestDraft = SettingsDraftState.FromConfig(latest);
            if (pendingEffects == SettingsDraftEffectKind.None
                && AreSameSettingsValues(draft, latestDraft))
            {
                if (IsSameCleanDraft(latestDraft))
                    return CreateResult(SettingsDraftOperationOutcome.Applied, null, false);
                activeSettings = latestDraft.Clone();
                draft = latestDraft;
                return CreateResult(SettingsDraftOperationOutcome.Applied);
            }

            bool recoveryOnly = pendingEffects != SettingsDraftEffectKind.None && !draft.Dirty;
            if (recoveryOnly)
                candidate = latest.Clone();
            else
            {
                try
                {
                    persistence.Persist(candidate);
                }
                catch (DurableConfigurationException exception)
                {
                    draft.SaveError = SemanticMessage.CreateWithDiagnostic(
                        SettingsDraftMessageKeys.DurableSaveFailed,
                        exception.Message);
                    return CreateResult(SettingsDraftOperationOutcome.DurablePersistenceFailed, exception);
                }
                catch (Exception exception)
                {
                    draft.SaveError = SemanticMessage.CreateWithDiagnostic(
                        SettingsDraftMessageKeys.SaveFailed,
                        exception.Message);
                    return CreateResult(SettingsDraftOperationOutcome.PersistenceFailed, exception);
                }

                persistence.ReplaceActiveConfig(candidate);
            }

            SettingsDraftEffectKind effectMask = pendingEffects | ResolveChangedEffects(latest, candidate);
            SettingsDraftEffectResult effectResult = ApplyRuntimeEffects(candidate, effectMask);
            pendingEffects = effectResult.Pending;
            activeSettings = SettingsDraftState.FromConfig(candidate);
            draft = activeSettings.Clone();
            if (effectResult.Failure != null)
            {
                draft.SaveError = SemanticMessage.CreateWithDiagnostic(
                    SettingsDraftMessageKeys.EffectFailed,
                    effectResult.Failure.Message);
                return CreateResult(SettingsDraftOperationOutcome.EffectsFailed, effectResult.Failure);
            }

            return CreateResult(SettingsDraftOperationOutcome.Saved);
        }
        private SettingsDraftEffectKind ResolveChangedEffects(
            AppConfig previous,
            AppConfig current)
        {
            SettingsDraftEffectKind changed = SettingsDraftEffectKind.None;
            if (!string.Equals(
                AppConfig.NormalizeLanguagePreference(previous.LanguagePreference),
                AppConfig.NormalizeLanguagePreference(current.LanguagePreference),
                StringComparison.Ordinal))
                changed |= SettingsDraftEffectKind.Language;
            if (previous.ColorMode != current.ColorMode)
                changed |= SettingsDraftEffectKind.Theme;
            if (previous.PlayPonder != current.PlayPonder)
                changed |= SettingsDraftEffectKind.BackgroundAnalysis;
            if (previous.DebugDiagnosticsEnabled != current.DebugDiagnosticsEnabled)
                changed |= SettingsDraftEffectKind.Diagnostics;
            return changed;
        }

        private SettingsDraftEffectResult ApplyRuntimeEffects(
            AppConfig current,
            SettingsDraftEffectKind mask)
        {
            SettingsDraftEffectKind pending = SettingsDraftEffectKind.None;
            Exception firstFailure = null;
            if ((mask & SettingsDraftEffectKind.Language) != SettingsDraftEffectKind.None)
                CaptureEffectFailure(
                    delegate { effects.ApplyLanguagePreference(current.LanguagePreference); },
                    SettingsDraftEffectKind.Language,
                    ref pending,
                    ref firstFailure);
            if ((mask & SettingsDraftEffectKind.Theme) != SettingsDraftEffectKind.None)
                CaptureEffectFailure(
                    delegate { effects.ApplyTheme(current.ColorMode); },
                    SettingsDraftEffectKind.Theme,
                    ref pending,
                    ref firstFailure);
            if ((mask & SettingsDraftEffectKind.BackgroundAnalysis) != SettingsDraftEffectKind.None)
                CaptureEffectFailure(
                    delegate { effects.ApplyBackgroundAnalysis(current.PlayPonder); },
                    SettingsDraftEffectKind.BackgroundAnalysis,
                    ref pending,
                    ref firstFailure);
            if ((mask & SettingsDraftEffectKind.Diagnostics) != SettingsDraftEffectKind.None)
                CaptureEffectFailure(
                    delegate { effects.ApplyDiagnostics(current.DebugDiagnosticsEnabled); },
                    SettingsDraftEffectKind.Diagnostics,
                    ref pending,
                    ref firstFailure);
            return new SettingsDraftEffectResult(pending, firstFailure);
        }

        private static void CaptureEffectFailure(
            Action effect,
            SettingsDraftEffectKind kind,
            ref SettingsDraftEffectKind pending,
            ref Exception firstFailure)
        {
            try
            {
                effect();
            }
            catch (Exception exception)
            {
                pending |= kind;
                if (firstFailure == null)
                    firstFailure = exception;
            }
        }

        private SettingsDraftOperationResult CreateResult(
            SettingsDraftOperationOutcome outcome,
            Exception failure = null,
            bool shouldPublishSnapshot = true)
        {
            return new SettingsDraftOperationResult(outcome, draft, failure, shouldPublishSnapshot);
        }
    }
}
