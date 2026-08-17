using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace readboard
{
    public partial class MainForm
    {
        private SettingsDraftRuntime webViewSettingsDraft;
        private ReadBoardDialogUiState webViewSettingsDialog;

        internal ReadBoardSettingsUiState GetWebViewSettingsState()
        {
            SettingsDraftState draft = EnsureWebViewSettingsDraft().Snapshot;
            return CreateWebViewSettingsState(draft);
        }

        internal ReadBoardDialogUiState GetWebViewSettingsDialogState()
        {
            return ResolveWebViewDialogState(
                webViewSettingsDialog,
                getLangStr,
                Program.GetDefaultLanguageText);
        }

        internal static ReadBoardDialogUiState ResolveWebViewDialogState(
            ReadBoardDialogUiState state,
            Func<string, string> getLocalizedText,
            Func<string, string> getDefaultText)
        {
            if (state == null)
                return null;
            return new ReadBoardDialogUiState
            {
                Open = state.Open,
                Kind = state.Kind,
                Title = ResolveWebViewDialogText(
                    state.TitleMessage,
                    state.Title,
                    getLocalizedText,
                    getDefaultText),
                Heading = state.Heading,
                Message = ResolveWebViewDialogText(
                    state.MessageMessage,
                    state.Message,
                    getLocalizedText,
                    getDefaultText),
                Detail = ResolveWebViewDialogText(
                    state.DetailMessage,
                    state.Detail,
                    getLocalizedText,
                    getDefaultText),
                ConfirmLabel = ResolveWebViewDialogText(
                    state.ConfirmLabelMessage,
                    state.ConfirmLabel,
                    getLocalizedText,
                    getDefaultText),
                CancelLabel = ResolveWebViewDialogText(
                    state.CancelLabelMessage,
                    state.CancelLabel,
                    getLocalizedText,
                    getDefaultText),
                DontShowAgainLabel = ResolveWebViewDialogText(
                    state.DontShowAgainLabelMessage,
                    state.DontShowAgainLabel,
                    getLocalizedText,
                    getDefaultText)
            };
        }

        private static string ResolveWebViewDialogText(
            SemanticMessage message,
            string fallback,
            Func<string, string> getLocalizedText,
            Func<string, string> getDefaultText)
        {
            return message == null
                ? fallback
                : SemanticMessageResolver.Resolve(message, getLocalizedText, getDefaultText);
        }

        private bool HandleWebViewSettingsCommand(ReadBoardUiCommand command)
        {
            if (command == null)
                return false;

            switch (command.Type)
            {
                case "settings.update":
                {
                    SettingsDraftOperationResult result = UpdateWebViewSetting(command.Payload);
                    return result == null || result.ShouldPublishSnapshot;
                }
                case "settings.save":
                {
                    SettingsDraftOperationResult result = SaveWebViewSettings();
                    return result == null || result.ShouldPublishSnapshot;
                }
                case "settings.cancel":
                {
                    bool dialogWasOpen = webViewSettingsDialog != null;
                    SettingsDraftOperationResult result = EnsureWebViewSettingsDraft().Cancel();
                    webViewSettingsDialog = null;
                    return dialogWasOpen || (result != null && result.ShouldPublishSnapshot);
                }
                case "settings.resetDefaults":
                    ShowWebViewSettingsDialog("resetDefaults");
                    return true;
                case "settings.openDiagnostics":
                    OpenWebViewDiagnosticsDirectory();
                    return false;
                case "dialog.confirm":
                    return ConfirmWebViewSettingsDialog();
                case "dialog.cancel":
                {
                    bool dialogWasOpen = webViewSettingsDialog != null;
                    webViewSettingsDialog = null;
                    return dialogWasOpen;
                }
                case "dialog.dontShowAgain":
                    return DisableWebViewShowInBoardHint();
                default:
                    return false;
            }
        }

        internal static bool IsValidWebViewSettingsCommand(ReadBoardUiCommand command)
        {
            if (command == null)
                return false;
            switch (command.Type)
            {
                case "settings.update":
                    return IsValidWebViewSettingUpdate(command.Payload);
                case "settings.save":
                case "settings.cancel":
                case "settings.resetDefaults":
                case "settings.openDiagnostics":
                case "dialog.confirm":
                case "dialog.cancel":
                case "dialog.dontShowAgain":
                    return HasEmptyPayload(command.Payload);
                default:
                    return false;
            }
        }

        private ReadBoardSettingsUiState CreateWebViewSettingsState(SettingsDraftState draft)
        {
            return WebViewSettingsStateProjector.Project(
                draft,
                getLangStr,
                Program.GetDefaultLanguageText);
        }



        private SettingsDraftRuntime EnsureWebViewSettingsDraft()
        {
            if (webViewSettingsDraft == null)
            {
                SettingsDraftRuntime draft = new SettingsDraftRuntime(
                    BuildCurrentAppConfig(),
                    delegate
                    {
                        AppConfig current = Program.CurrentConfig;
                        return AppConfig.CreateDefault(current.ProtocolVersion, current.MachineKey);
                    },
                    new MainFormSettingsDraftPersistence(
                        BuildCurrentAppConfig,
                        delegate(AppConfig candidate) { Program.ConfigStore.Save(candidate); },
                        delegate(AppConfig candidate)
                        {
                            Program.CurrentContext.Config = candidate;
                            Program.CurrentContext.HasConfigFile = true;
                        },
                        controlCenterRuntime.MarkPersistenceSucceeded),
                    new MainFormSettingsDraftRuntimeEffects(
                        delegate(string preference) { Program.ApplyLanguagePreference(preference); },
                        ApplyMainWindowTitle,
                        ApplyMainFormUi,
                        delegate(bool enabled)
                        {
                            resetBtnKeepSyncName();
                            sessionCoordinator.SendPonderStatus(enabled);
                        }));
                webViewSettingsDraft = draft;
            }
            return webViewSettingsDraft;
        }
        internal static class WebViewSettingsStateProjector
        {
            public static ReadBoardSettingsUiState Project(
                SettingsDraftState draft,
                Func<string, string> getLocalizedText,
                Func<string, string> getDefaultText)
            {
                if (draft == null)
                    throw new ArgumentNullException("draft");
                if (getLocalizedText == null)
                    throw new ArgumentNullException("getLocalizedText");
                if (getDefaultText == null)
                    throw new ArgumentNullException("getDefaultText");

                return new ReadBoardSettingsUiState
                {
                    AutoMinimize = draft.AutoMinimize,
                    BackgroundAnalysis = draft.BackgroundAnalysis,
                    Magnifier = draft.Magnifier,
                    EnhancedCapture = draft.EnhancedCapture,
                    PlacementValidation = draft.PlacementValidation,
                    SyncInterval = draft.SyncInterval,
                    GrayOffset = draft.GrayOffset,
                    BlackOffset = draft.BlackOffset,
                    BlackPercent = draft.BlackPercent,
                    WhiteOffset = draft.WhiteOffset,
                    WhitePercent = draft.WhitePercent,
                    Theme = draft.Theme,
                    Language = draft.Language,
                    Diagnostics = draft.Diagnostics,
                    Dirty = draft.Dirty,
                    DirtyStatus = ResolveSettingsMessage(
                        SemanticMessage.Create(draft.Dirty
                            ? "WebView_unsavedChanges"
                            : "WebView_noUnsavedChanges"),
                        getLocalizedText,
                        getDefaultText),
                    Errors = ResolveSettingsErrors(draft.Errors, getLocalizedText, getDefaultText),
                    SaveError = ResolveSettingsMessage(draft.SaveError, getLocalizedText, getDefaultText)
                };
            }

            private static IDictionary<string, string> ResolveSettingsErrors(
                IDictionary<string, SemanticMessage> errors,
                Func<string, string> getLocalizedText,
                Func<string, string> getDefaultText)
            {
                IDictionary<string, string> resolved = new Dictionary<string, string>();
                foreach (KeyValuePair<string, SemanticMessage> error in errors)
                {
                    resolved[error.Key] = ResolveSettingsMessage(
                        error.Value,
                        getLocalizedText,
                        getDefaultText);
                }
                return resolved;
            }

            private static string ResolveSettingsMessage(
                SemanticMessage message,
                Func<string, string> getLocalizedText,
                Func<string, string> getDefaultText)
            {
                if (message == null)
                    return null;

                return SemanticMessageResolver.Resolve(
                    message,
                    getLocalizedText,
                    getDefaultText);
            }
        }

        private static bool IsValidWebViewSettingUpdate(JsonElement payload)
        {
            JsonElement keyValue;
            JsonElement value;
            if (payload.ValueKind != JsonValueKind.Object
                || CountProperties(payload) != 2
                || !payload.TryGetProperty("key", out keyValue)
                || keyValue.ValueKind != JsonValueKind.String
                || !payload.TryGetProperty("value", out value))
                return false;

            string key = keyValue.GetString();
            if (key == "autoMinimize"
                || key == "backgroundAnalysis"
                || key == "magnifier"
                || key == "enhancedCapture"
                || key == "placementValidation"
                || key == "diagnostics")
                return value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False;
            if (key == "syncInterval"
                || key == "grayOffset"
                || key == "blackOffset"
                || key == "blackPercent"
                || key == "whiteOffset"
                || key == "whitePercent")
                return value.ValueKind == JsonValueKind.String;
            if (key == "theme")
                return IsAllowedString(value, "system", "dark", "light");
            return key == "language"
                && value.ValueKind == JsonValueKind.String
                && AppConfig.IsSupportedLanguagePreference(value.GetString());
        }

        private SettingsDraftOperationResult UpdateWebViewSetting(JsonElement payload)
        {
            ReadBoardSettingsUiState settings = GetWebViewSettingsState();
            string key = payload.GetProperty("key").GetString();
            JsonElement value = payload.GetProperty("value");
            if (key == "diagnostics" && value.GetBoolean() && !settings.Diagnostics)
            {
                ShowWebViewSettingsDialog("diagnostics");
                return null;
            }

            return EnsureWebViewSettingsDraft().Update(
                CreateSettingsDraftUpdate(key, value));
        }

        private static SettingsDraftUpdate CreateSettingsDraftUpdate(string key, JsonElement value)
        {
            switch (key)
            {
                case "autoMinimize":
                    return SettingsDraftUpdate.Boolean(SettingsDraftField.AutoMinimize, value.GetBoolean());
                case "backgroundAnalysis":
                    return SettingsDraftUpdate.Boolean(SettingsDraftField.BackgroundAnalysis, value.GetBoolean());
                case "magnifier":
                    return SettingsDraftUpdate.Boolean(SettingsDraftField.Magnifier, value.GetBoolean());
                case "enhancedCapture":
                    return SettingsDraftUpdate.Boolean(SettingsDraftField.EnhancedCapture, value.GetBoolean());
                case "placementValidation":
                    return SettingsDraftUpdate.Boolean(SettingsDraftField.PlacementValidation, value.GetBoolean());
                case "diagnostics":
                    return SettingsDraftUpdate.Boolean(SettingsDraftField.Diagnostics, value.GetBoolean());
                case "syncInterval":
                    return SettingsDraftUpdate.Text(SettingsDraftField.SyncInterval, value.GetString());
                case "grayOffset":
                    return SettingsDraftUpdate.Text(SettingsDraftField.GrayOffset, value.GetString());
                case "blackOffset":
                    return SettingsDraftUpdate.Text(SettingsDraftField.BlackOffset, value.GetString());
                case "blackPercent":
                    return SettingsDraftUpdate.Text(SettingsDraftField.BlackPercent, value.GetString());
                case "whiteOffset":
                    return SettingsDraftUpdate.Text(SettingsDraftField.WhiteOffset, value.GetString());
                case "whitePercent":
                    return SettingsDraftUpdate.Text(SettingsDraftField.WhitePercent, value.GetString());
                case "theme":
                    return SettingsDraftUpdate.Text(SettingsDraftField.Theme, value.GetString());
                case "language":
                    return SettingsDraftUpdate.Text(SettingsDraftField.Language, value.GetString());
                default:
                    throw new ArgumentOutOfRangeException("key");
            }
        }

        private SettingsDraftOperationResult SaveWebViewSettings()
        {
            SettingsDraftOperationResult result = EnsureWebViewSettingsDraft().Save();
            if (result.Outcome == SettingsDraftOperationOutcome.Saved)
                webViewSettingsDialog = null;
            return result;
        }

        private void ShowWebViewSettingsDialog(string kind)
        {
            webViewSettingsDialog = CreateWebViewDialog(kind);
        }

        internal static ReadBoardDialogUiState CreateWebViewMessageDialog(string titleKey, string messageKey)
        {
            return new ReadBoardDialogUiState
            {
                Open = true,
                TitleMessage = SemanticMessage.Create(titleKey),
                MessageMessage = SemanticMessage.Create(messageKey),
                ConfirmLabelMessage = SemanticMessage.Create("SettingsForm_btnConfirm"),
                CancelLabelMessage = SemanticMessage.Create("SettingsForm_btnCancel")
            };
        }

        private static ReadBoardDialogUiState CreateWebViewDialog(string kind)
        {
            ReadBoardDialogUiState dialog = new ReadBoardDialogUiState
            {
                Open = true,
                Kind = kind
            };
            switch (kind)
            {
                case "resetDefaults":
                    dialog.TitleMessage = SemanticMessage.Create("SettingsForm_btnReset");
                    dialog.MessageMessage = SemanticMessage.Create("WebView_resetDefaultsDescription");
                    dialog.ConfirmLabelMessage = SemanticMessage.Create("WebView_resetDefaults");
                    dialog.CancelLabelMessage = SemanticMessage.Create("SettingsForm_btnCancel");
                    return dialog;
                case "diagnostics":
                    dialog.TitleMessage = SemanticMessage.Create("WebView_enableDiagnostics");
                    dialog.MessageMessage = SemanticMessage.Create("WebView_diagnosticsDescription");
                    dialog.ConfirmLabelMessage = SemanticMessage.Create("WebView_continueEnable");
                    dialog.CancelLabelMessage = SemanticMessage.Create("SettingsForm_btnCancel");
                    return dialog;
                case "showInBoardHint":
                    dialog.TitleMessage = SemanticMessage.Create("TipsForm_title");
                    dialog.MessageMessage = SemanticMessage.Create("WebView_showInBoardHintForeground");
                    dialog.DetailMessage = SemanticMessage.Create("WebView_showInBoardHintRestore");
                    dialog.ConfirmLabelMessage = SemanticMessage.Create("TipsForm_btnConfirm");
                    dialog.DontShowAgainLabelMessage = SemanticMessage.Create("TipsForm_btnNotAskAgain");
                    return dialog;
                default:
                    throw new ArgumentOutOfRangeException("kind");
            }
        }

        private bool ConfirmWebViewSettingsDialog()
        {
            if (webViewSettingsDialog == null)
                return false;

            string kind = webViewSettingsDialog.Kind;
            webViewSettingsDialog = null;
            if (kind == "diagnostics")
                EnsureWebViewSettingsDraft().Update(
                    SettingsDraftUpdate.Boolean(SettingsDraftField.Diagnostics, true));
            else if (kind == "resetDefaults")
                EnsureWebViewSettingsDraft().Reset();
            return true;
        }

        private bool DisableWebViewShowInBoardHint()
        {
            if (webViewSettingsDialog == null || webViewSettingsDialog.Kind != "showInBoardHint")
                return false;
            AppConfig updated = Program.CurrentConfig.Clone();
            updated.ShowInBoardHint = false;
            Program.CurrentContext.Config = updated;
            PersistConfiguration();
            webViewSettingsDialog = null;
            return true;
        }

        private static void OpenWebViewDiagnosticsDirectory()
        {
            string directory = BoardDebugDiagnosticsPaths.GetRootDirectory(AppDomain.CurrentDomain.BaseDirectory);
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }

        internal sealed class MainFormSettingsDraftPersistence : ISettingsDraftPersistence
        {
            private readonly Func<AppConfig> getLatestActiveConfig;
            private readonly Action<AppConfig> persist;
            private readonly Action<AppConfig> replaceActiveConfig;
            private readonly Action markPersistenceSucceeded;

            public MainFormSettingsDraftPersistence(
                Func<AppConfig> getLatestActiveConfig,
                Action<AppConfig> persist,
                Action<AppConfig> replaceActiveConfig,
                Action markPersistenceSucceeded)
            {
                this.getLatestActiveConfig = getLatestActiveConfig ?? throw new ArgumentNullException("getLatestActiveConfig");
                this.persist = persist ?? throw new ArgumentNullException("persist");
                this.replaceActiveConfig = replaceActiveConfig ?? throw new ArgumentNullException("replaceActiveConfig");
                this.markPersistenceSucceeded = markPersistenceSucceeded ?? throw new ArgumentNullException("markPersistenceSucceeded");
            }

            public AppConfig GetLatestActiveConfig()
            {
                return getLatestActiveConfig();
            }

            public void Persist(AppConfig candidate)
            {
                persist(candidate);
            }

            public void ReplaceActiveConfig(AppConfig candidate)
            {
                replaceActiveConfig(candidate.Clone());
                markPersistenceSucceeded();
            }
        }

        internal sealed class MainFormSettingsDraftRuntimeEffects : ISettingsDraftRuntimeEffects
        {
            private readonly Action<string> applyLanguagePreference;
            private readonly Action applyMainWindowTitle;
            private readonly Action applyTheme;
            private readonly Action<bool> applyBackgroundAnalysis;

            public MainFormSettingsDraftRuntimeEffects(
                Action<string> applyLanguagePreference,
                Action applyMainWindowTitle,
                Action applyTheme,
                Action<bool> applyBackgroundAnalysis)
            {
                this.applyLanguagePreference = applyLanguagePreference ?? throw new ArgumentNullException("applyLanguagePreference");
                this.applyMainWindowTitle = applyMainWindowTitle ?? throw new ArgumentNullException("applyMainWindowTitle");
                this.applyTheme = applyTheme ?? throw new ArgumentNullException("applyTheme");
                this.applyBackgroundAnalysis = applyBackgroundAnalysis ?? throw new ArgumentNullException("applyBackgroundAnalysis");
            }

            public void ApplyLanguagePreference(string preference)
            {
                try
                {
                    applyLanguagePreference(preference);
                }
                finally
                {
                    applyMainWindowTitle();
                }
            }

            public void ApplyTheme(int colorMode)
            {
                applyTheme();
            }

            public void ApplyBackgroundAnalysis(bool enabled)
            {
                applyBackgroundAnalysis(enabled);
            }

        }

        private static string ResolveWebViewTheme(int colorMode)
        {
            if (colorMode == AppConfig.ColorModeDark)
                return "dark";
            return colorMode == AppConfig.ColorModeLight ? "light" : "system";
        }
    }
}
