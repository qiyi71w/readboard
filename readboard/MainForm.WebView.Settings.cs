using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace readboard
{
    public partial class MainForm
    {
        private ReadBoardSettingsUiState webViewSettingsDraft;
        private ReadBoardDialogUiState webViewSettingsDialog;

        internal ReadBoardSettingsUiState GetWebViewSettingsState()
        {
            if (webViewSettingsDraft == null)
                OpenWebViewSettingsDraft();
            return webViewSettingsDraft;
        }

        internal ReadBoardDialogUiState GetWebViewSettingsDialogState()
        {
            return webViewSettingsDialog;
        }

        internal void OpenWebViewSettingsDraft()
        {
            webViewSettingsDraft = CreateWebViewSettingsState(Program.CurrentConfig);
            webViewSettingsDialog = null;
        }

        internal bool HandleWebViewSettingsCommand(ReadBoardUiCommand command)
        {
            if (command == null)
                return false;

            switch (command.Type)
            {
                case "settings.update":
                    UpdateWebViewSetting(command.Payload);
                    return true;
                case "settings.save":
                    SaveWebViewSettings();
                    return true;
                case "settings.cancel":
                    OpenWebViewSettingsDraft();
                    return true;
                case "settings.resetDefaults":
                    ShowWebViewSettingsDialog("resetDefaults");
                    return true;
                case "settings.openDiagnostics":
                    OpenWebViewDiagnosticsDirectory();
                    return true;
                case "dialog.confirm":
                    ConfirmWebViewSettingsDialog();
                    return true;
                case "dialog.cancel":
                    webViewSettingsDialog = null;
                    return true;
                case "dialog.dontShowAgain":
                    DisableWebViewShowInBoardHint();
                    return true;
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

        internal static ReadBoardSettingsUiState CreateWebViewSettingsState(AppConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");
            return new ReadBoardSettingsUiState
            {
                AutoMinimize = config.AutoMinimize,
                BackgroundAnalysis = config.PlayPonder,
                Magnifier = config.UseMagnifier,
                EnhancedCapture = config.UseEnhanceScreen,
                PlacementValidation = config.VerifyMove,
                DisableShowShortcut = config.DisableShowInBoardShortcut,
                SyncInterval = config.SyncIntervalMs.ToString(),
                GrayOffset = config.GrayOffset.ToString(),
                BlackOffset = config.BlackOffset.ToString(),
                BlackPercent = config.BlackPercent.ToString(),
                WhiteOffset = config.WhiteOffset.ToString(),
                WhitePercent = config.WhitePercent.ToString(),
                Theme = ResolveWebViewTheme(config.ColorMode),
                Diagnostics = config.DebugDiagnosticsEnabled,
                Dirty = false
            };
        }

        internal static bool TryBuildWebViewSettingsConfig(
            AppConfig current,
            ReadBoardSettingsUiState settings,
            out AppConfig updated)
        {
            if (current == null)
                throw new ArgumentNullException("current");
            if (settings == null)
                throw new ArgumentNullException("settings");

            updated = current.Clone();
            settings.Errors = new Dictionary<string, string>();
            int syncInterval;
            int grayOffset;
            int blackOffset;
            int blackPercent;
            int whiteOffset;
            int whitePercent;
            ReadInteger(settings.SyncInterval, "syncInterval", settings.Errors, out syncInterval);
            ReadInteger(settings.GrayOffset, "grayOffset", settings.Errors, out grayOffset);
            ReadInteger(settings.BlackOffset, "blackOffset", settings.Errors, out blackOffset);
            ReadInteger(settings.BlackPercent, "blackPercent", settings.Errors, out blackPercent);
            ReadInteger(settings.WhiteOffset, "whiteOffset", settings.Errors, out whiteOffset);
            ReadInteger(settings.WhitePercent, "whitePercent", settings.Errors, out whitePercent);
            if (!settings.Errors.ContainsKey("syncInterval") && syncInterval < 20)
                settings.Errors["syncInterval"] = "请输入不小于 20 的整数";
            AddRangeError(grayOffset, 0, 255, "grayOffset", settings.Errors);
            AddRangeError(blackOffset, 0, 255, "blackOffset", settings.Errors);
            AddRangeError(blackPercent, 0, 100, "blackPercent", settings.Errors);
            AddRangeError(whiteOffset, 0, 255, "whiteOffset", settings.Errors);
            AddRangeError(whitePercent, 0, 100, "whitePercent", settings.Errors);
            if (settings.Errors.Count != 0)
                return false;

            updated.SyncIntervalMs = syncInterval;
            updated.GrayOffset = grayOffset;
            updated.BlackOffset = blackOffset;
            updated.BlackPercent = blackPercent;
            updated.WhiteOffset = whiteOffset;
            updated.WhitePercent = whitePercent;
            updated.AutoMinimize = settings.AutoMinimize;
            updated.PlayPonder = settings.BackgroundAnalysis;
            updated.UseMagnifier = settings.Magnifier;
            updated.UseEnhanceScreen = settings.EnhancedCapture;
            updated.VerifyMove = settings.PlacementValidation;
            updated.DisableShowInBoardShortcut = settings.DisableShowShortcut;
            updated.DebugDiagnosticsEnabled = settings.Diagnostics;
            updated.ColorMode = ResolveColorMode(settings.Theme);
            return true;
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
                || key == "disableShowShortcut"
                || key == "diagnostics")
                return value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False;
            if (key == "syncInterval"
                || key == "grayOffset"
                || key == "blackOffset"
                || key == "blackPercent"
                || key == "whiteOffset"
                || key == "whitePercent")
                return value.ValueKind == JsonValueKind.String;
            return key == "theme" && IsAllowedString(value, "system", "dark", "light");
        }

        private void UpdateWebViewSetting(JsonElement payload)
        {
            ReadBoardSettingsUiState settings = GetWebViewSettingsState();
            string key = payload.GetProperty("key").GetString();
            JsonElement value = payload.GetProperty("value");
            if (key == "diagnostics" && value.GetBoolean() && !settings.Diagnostics)
            {
                ShowWebViewSettingsDialog("diagnostics");
                return;
            }

            switch (key)
            {
                case "autoMinimize": settings.AutoMinimize = value.GetBoolean(); break;
                case "backgroundAnalysis": settings.BackgroundAnalysis = value.GetBoolean(); break;
                case "magnifier": settings.Magnifier = value.GetBoolean(); break;
                case "enhancedCapture": settings.EnhancedCapture = value.GetBoolean(); break;
                case "placementValidation": settings.PlacementValidation = value.GetBoolean(); break;
                case "disableShowShortcut": settings.DisableShowShortcut = value.GetBoolean(); break;
                case "diagnostics": settings.Diagnostics = value.GetBoolean(); break;
                case "syncInterval": settings.SyncInterval = value.GetString(); break;
                case "grayOffset": settings.GrayOffset = value.GetString(); break;
                case "blackOffset": settings.BlackOffset = value.GetString(); break;
                case "blackPercent": settings.BlackPercent = value.GetString(); break;
                case "whiteOffset": settings.WhiteOffset = value.GetString(); break;
                case "whitePercent": settings.WhitePercent = value.GetString(); break;
                case "theme": settings.Theme = value.GetString(); break;
            }
            settings.Errors.Remove(key);
            settings.Dirty = true;
        }

        private void SaveWebViewSettings()
        {
            AppConfig updated;
            ReadBoardSettingsUiState settings = GetWebViewSettingsState();
            if (!TryBuildWebViewSettingsConfig(Program.CurrentConfig, settings, out updated))
                return;

            bool colorModeChanged = updated.ColorMode != Program.CurrentConfig.ColorMode;
            Program.CurrentContext.Config = updated;
            PersistConfiguration();
            RefreshShowInBoardShortcutToolTip();
            resetBtnKeepSyncName();
            sendPonderStatus();
            webViewSettingsDraft = CreateWebViewSettingsState(Program.CurrentConfig);
            webViewSettingsDialog = colorModeChanged
                ? new ReadBoardDialogUiState { Open = true, Kind = "themeRestart" }
                : null;
        }

        private void ShowWebViewSettingsDialog(string kind)
        {
            webViewSettingsDialog = new ReadBoardDialogUiState { Open = true, Kind = kind };
        }

        private void ConfirmWebViewSettingsDialog()
        {
            string kind = webViewSettingsDialog == null ? null : webViewSettingsDialog.Kind;
            webViewSettingsDialog = null;
            if (kind == "diagnostics")
            {
                ReadBoardSettingsUiState settings = GetWebViewSettingsState();
                settings.Diagnostics = true;
                settings.Dirty = true;
            }
            else if (kind == "resetDefaults")
            {
                AppConfig current = Program.CurrentConfig;
                webViewSettingsDraft = CreateWebViewSettingsState(
                    AppConfig.CreateDefault(current.ProtocolVersion, current.MachineKey));
                webViewSettingsDraft.Dirty = true;
            }
        }

        private void DisableWebViewShowInBoardHint()
        {
            if (webViewSettingsDialog == null || webViewSettingsDialog.Kind != "showInBoardHint")
                return;
            AppConfig updated = Program.CurrentConfig.Clone();
            updated.ShowInBoardHint = false;
            Program.CurrentContext.Config = updated;
            PersistConfiguration();
            webViewSettingsDialog = null;
        }

        private static void OpenWebViewDiagnosticsDirectory()
        {
            string directory = BoardDebugDiagnosticsPaths.GetRootDirectory(AppDomain.CurrentDomain.BaseDirectory);
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }

        private static void ReadInteger(
            string value,
            string key,
            IDictionary<string, string> errors,
            out int parsed)
        {
            if (!int.TryParse(value, out parsed))
                errors[key] = "请输入整数";
        }

        private static void AddRangeError(
            int value,
            int minimum,
            int maximum,
            string key,
            IDictionary<string, string> errors)
        {
            if (!errors.ContainsKey(key) && (value < minimum || value > maximum))
                errors[key] = "请输入 " + minimum + "–" + maximum + " 之间的整数";
        }

        private static string ResolveWebViewTheme(int colorMode)
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
}
