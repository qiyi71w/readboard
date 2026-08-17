using System;
using System.IO;
using System.Text.Json;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class WebViewUiPolishTests
    {
        [Fact]
        public void MovePlacementSelector_UsesApprovedCompactSegmentLayout()
        {
            string styles = LoadWebViewAsset("styles.css");

            Assert.Contains(".placement-row > b { font-size: 14px; }", styles);
            Assert.Contains(".placement-row { display: grid; grid-template-columns: 62px minmax(0, 1fr); gap: 8px;", styles);
            Assert.Contains(".placement-row .segments { flex: 1; max-width: 424px; }", styles);
            Assert.Contains(".placement-row .segments label { display: flex; flex: 1; min-width: 0; min-height: var(--control-center-control-height); align-items: center; justify-content: center; padding: 6px 10px; font-size: 14px; }", styles);
        }

        [Fact]
        public void SyncControls_GroupRelatedOptionsIntoAlignedRows()
        {
            string html = LoadWebViewAsset("index.html");
            string styles = LoadWebViewAsset("styles.css");

            Assert.Contains("<h3 data-i18n=\"WebView_syncSettings\">同步设置</h3>", html);
            Assert.Contains("<div class=\"sync-toggle-row\">", html);
            Assert.Contains("<div class=\"color-row\"><b data-i18n=\"WebView_stoneColor\">执子颜色</b>", html);
            Assert.Contains("<div class=\"segments color-segments\" role=\"radiogroup\" aria-label=\"执子颜色\" data-i18n-aria-label=\"WebView_stoneColor\">", html);
            Assert.Contains(".sync-toggle-row { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }", styles);
            Assert.Contains(".sync-options { display: grid; min-width: 0; grid-template-rows: auto repeat(3, var(--control-center-control-height)); gap: var(--control-center-inner-gap);", styles);
            Assert.Contains(".color-row { display: grid; grid-template-columns: 62px minmax(0, 1fr) 90px; gap: 8px;", styles);
            Assert.Contains(".color-row button { width: 90px; height: var(--control-center-control-height); min-height: var(--control-center-control-height); }", styles);
            Assert.Contains(".color-row .segments label { display: flex; flex: 1; min-width: 0; min-height: var(--control-center-control-height); align-items: center; justify-content: center; padding: 6px 10px; font-size: 14px; }", styles);
        }
        [Fact]
        public void AutoPlayColorStatus_IsBoundToFoxAutoColorMode()
        {
            string html = LoadWebViewAsset("index.html");
            string styles = LoadWebViewAsset("styles.css");
            string script = LoadWebViewAsset("app.js");

            const string autoPlayControl = "<div class=\"auto-play-option\"><label><input id=\"auto-play\" type=\"checkbox\"><span data-i18n=\"MainForm_chkAutoPlay\">自动落子</span></label><span id=\"auto-play-color-status\" class=\"auto-play-color-status\" role=\"status\" aria-live=\"polite\" hidden></span></div>";
            Assert.Contains(autoPlayControl, html);
            Assert.DoesNotContain("<label class=\"auto-play-option\">", html);
            Assert.Contains(".auto-play-color-status", styles);
            Assert.Contains("const showAutoPlayColorStatus = (control.platform === \"fox\" || control.platform === \"foxBackground\") && control.color === \"auto\";", script);
            Assert.Contains("autoPlayColorStatus.hidden = !showAutoPlayColorStatus;", script);
            Assert.Contains("autoPlayColorStatus.textContent = showAutoPlayColorStatus ? (control.autoPlayColorStatus || \"\") : \"\";", script);
        }

        [Fact]
        public void ControlCenter_UsesCappedHeightResponsiveVerticalRhythm()
        {
            string styles = LoadWebViewAsset("styles.css");

            Assert.Contains("--control-center-control-height: clamp(37px, calc(5.5vh - .4px), 48px);", styles);
            Assert.Contains("--control-center-section-gap: clamp(6px, calc(.9vh - .12px), 12px);", styles);
            Assert.Contains("--control-center-inner-gap: clamp(4px, calc(.65vh - .42px), 8px);", styles);
            Assert.Contains("gap: var(--control-center-section-gap);", styles);
            Assert.Contains(".engine-options { display: grid; min-width: 0; grid-template-rows: auto repeat(3, var(--control-center-control-height)); gap: var(--control-center-inner-gap); }", styles);
        }

        [Fact]
        public void ControlCenter_GroupFramesUseResponsiveBottomClearance()
        {
            string styles = LoadWebViewAsset("styles.css");

            Assert.Contains("--control-center-group-padding: clamp(3px, calc(2.75vh - 15.7px), 8px);", styles);
            Assert.Contains(".page[data-page-panel=\"controlCenter\"] > fieldset:not(.log-card) { padding-bottom: var(--control-center-group-padding); }", styles);
        }

        [Fact]
        public void ShowInBoardHint_UsesLegacyCopyWithoutShortcutAndOnlyExpectedActions()
        {
            string styles = LoadWebViewAsset("styles.css");
            string html = LoadWebViewAsset("index.html");

            Assert.Contains("id=\"modal-layer\"", html);
            Assert.Contains("role=\"dialog\" aria-modal=\"true\"", html);
            Assert.Contains(".modal.show-in-board-hint { grid-template-rows: 44px auto 52px; }", styles);
            Assert.Contains(".modal.show-in-board-hint .modal-body { min-height: 0; padding: 12px 16px; overflow: hidden; }", styles);

            ReadBoardDialogUiState dialog = new ReadBoardDialogUiState
            {
                Open = true,
                Kind = "showInBoardHint",
                TitleMessage = SemanticMessage.Create("TipsForm_title"),
                MessageMessage = SemanticMessage.Create("WebView_showInBoardHintForeground"),
                DetailMessage = SemanticMessage.Create("WebView_showInBoardHintRestore"),
                ConfirmLabelMessage = SemanticMessage.Create("TipsForm_btnConfirm"),
                DontShowAgainLabelMessage = SemanticMessage.Create("TipsForm_btnNotAskAgain")
            };
            ReadBoardDialogUiState projected = MainForm.ResolveWebViewDialogState(
                dialog,
                delegate(string key) { return key + " localized"; },
                delegate(string key) { return key + " default"; });
            Assert.Equal("TipsForm_title localized", projected.Title);
            Assert.Equal("WebView_showInBoardHintForeground localized", projected.Message);
            Assert.Equal("WebView_showInBoardHintRestore localized", projected.Detail);
            Assert.Equal("TipsForm_btnConfirm localized", projected.ConfirmLabel);
            Assert.Equal("TipsForm_btnNotAskAgain localized", projected.DontShowAgainLabel);

            foreach (string language in new[] { "cn", "en", "jp", "kr" })
            {
                string[] languageLines = File.ReadAllLines(Path.Combine(
                    VerificationFixtureLocator.RepositoryRoot(),
                    "readboard",
                    "language_" + language + ".txt"));
                string firstLine = Array.Find(languageLines, line => line.StartsWith("WebView_showInBoardHintForeground=", StringComparison.Ordinal));
                string secondLine = Array.Find(languageLines, line => line.StartsWith("WebView_showInBoardHintRestore=", StringComparison.Ordinal));
                Assert.NotNull(firstLine);
                Assert.NotNull(secondLine);
                Assert.DoesNotContain("Ctrl+X", firstLine);
                Assert.DoesNotContain("Ctrl+X", secondLine);
            }
        }

        [Fact]
        public void MessageDialog_ProjectsConfirmAndCancelLabels()
        {
            ReadBoardDialogUiState dialog = MainForm.CreateWebViewMessageDialog(
                "WebView_syncFailedTitle",
                "noSelectedBoardAndFailed");
            ReadBoardDialogUiState projected = MainForm.ResolveWebViewDialogState(
                dialog,
                delegate(string key) { return key + " localized"; },
                delegate(string key) { return key + " default"; });

            Assert.Equal("WebView_syncFailedTitle localized", projected.Title);
            Assert.Equal("noSelectedBoardAndFailed localized", projected.Message);
            Assert.Equal("SettingsForm_btnConfirm localized", projected.ConfirmLabel);
            Assert.Equal("SettingsForm_btnCancel localized", projected.CancelLabel);
        }

        [Fact]
        public void SettingsAndAbout_UseCurrentProductContent()
        {
            string html = LoadWebViewAsset("index.html");

            Assert.DoesNotContain("禁用盘上显示快捷键", html);
            Assert.DoesNotContain("disableShowShortcut", html);
            Assert.Contains("github.com/qiyi71w/readboard", html);
            Assert.Contains("打开项目仓库", html);
            Assert.DoesNotContain("打开上游仓库", html);
        }

        [Fact]
        public void SettingsLanguageSelector_UsesTheExistingAppearanceControlStyle()
        {
            string html = LoadWebViewAsset("index.html");
            string styles = LoadWebViewAsset("styles.css");
            string script = LoadWebViewAsset("app.js");

            Assert.Contains("<div class=\"appearance-language\">", html);
            Assert.Contains("<select id=\"settings-language\" data-setting=\"language\">", html);
            Assert.Contains("<option value=\"host\" data-i18n=\"WebView_followHostLanguage\">跟随 LizzieYzy-Next</option>", html);
            Assert.Contains("<option value=\"cn\">简体中文</option><option value=\"en\">English</option><option value=\"jp\">日本語</option><option value=\"kr\">한국어</option>", html);
            Assert.Contains("button, input, select { font: inherit; color: inherit; }", styles);
            Assert.Contains(".appearance-language { display: grid; grid-template-columns: minmax(0, 1.5fr) minmax(200px, 1fr) 160px; align-items: center; gap: 24px;", styles);
            Assert.Contains(".appearance-language select { grid-column: 2 / -1;", styles);
            Assert.Contains(".page[data-page-panel=\"settings\"] .field-error:empty { display: none; }", styles);
            Assert.Contains(".page[data-page-panel=\"settings\"] .settings-card { padding: 8px 12px 7px; }", styles);
            Assert.Contains("input instanceof HTMLSelectElement", script);

            AssertLanguageValue("cn", "WebView_language", "界面语言");
            AssertLanguageValue("en", "WebView_language", "Interface language");
            AssertLanguageValue("jp", "WebView_language", "表示言語");
            AssertLanguageValue("kr", "WebView_language", "인터페이스 언어");
        }
        [Fact]
        public void SettingsSaveErrorHasAccessiblePresentation()
        {
            string html = LoadWebViewAsset("index.html");
            string styles = LoadWebViewAsset("styles.css");

            Assert.Contains("id=\"settings-error\" class=\"settings-error\" role=\"alert\"", html);
            Assert.Contains(".settings-error:empty { display: none; }", styles);
        }


        [Fact]
        public void EnginePlacement_DisablesFirstPolicyAfterRestoringItsValue()
        {
            string script = LoadWebViewAsset("app.js");
            int valueIndex = script.IndexOf("setValue(\"#first-policy\", control.firstPolicy ?? \"\");", StringComparison.Ordinal);
            int disabledIndex = script.IndexOf("setDisabled(\"#first-policy\", !control.firstPolicyEnabled);", StringComparison.Ordinal);

            Assert.True(valueIndex >= 0, "The first-policy value must be restored from state.");
            Assert.True(disabledIndex > valueIndex, "The host state must disable first-policy only after restoring its value.");
        }

        [Fact]
        public void ControlCenter_DisabledControlsHaveScopedLightAndDarkVisualStates()
        {
            string styles = LoadWebViewAsset("styles.css");

            Assert.Contains("--disabled-border: #c8d2dc;", styles);
            Assert.Contains("--disabled-selected-background: #dfe7ee;", styles);
            Assert.Contains("--disabled-selected-indicator: #8494a5;", styles);
            Assert.Contains("--disabled-border: #484848;", styles);
            Assert.Contains("--disabled-selected-background: #383838;", styles);
            Assert.Contains("--disabled-selected-indicator: #606060;", styles);
            Assert.Contains("button:disabled { color: var(--disabled-text); border-color: var(--disabled-border); background: var(--disabled-background); cursor: default; }", styles);
            Assert.Contains(".page[data-page-panel=\"controlCenter\"] label:has(input:disabled) { color: var(--disabled-text); cursor: default; }", styles);
            Assert.Contains(".page[data-page-panel=\"controlCenter\"] input[type=\"radio\"]:disabled, .page[data-page-panel=\"controlCenter\"] input[type=\"checkbox\"]:disabled { accent-color: var(--disabled-selected-indicator); cursor: default; }", styles);
            Assert.Contains(".page[data-page-panel=\"controlCenter\"] input[type=\"number\"]:disabled { color: var(--disabled-text); border-color: var(--disabled-border); background: var(--disabled-background); cursor: default; }", styles);
            Assert.Contains(".page[data-page-panel=\"controlCenter\"] .choice-grid label:has(input:disabled) { border-color: var(--disabled-border); background: var(--disabled-background); }", styles);
            Assert.Contains(".page[data-page-panel=\"controlCenter\"] .choice-grid label:has(input:checked:disabled) { background: var(--disabled-selected-background); }", styles);
            Assert.Contains(".page[data-page-panel=\"controlCenter\"] .segments:has(input:disabled) { border-color: var(--disabled-border); background: var(--disabled-background); }", styles);
            Assert.Contains(".page[data-page-panel=\"controlCenter\"] .segments label:has(input:checked:disabled) { color: var(--disabled-text); background: var(--disabled-selected-background); }", styles);
            Assert.Contains(".page[data-page-panel=\"controlCenter\"] .color-row:has(input:disabled) > b, .page[data-page-panel=\"controlCenter\"] .placement-row:has(input:disabled) > b, .page[data-page-panel=\"controlCenter\"] .board-size-row:has(input:disabled) > b { color: var(--disabled-text); }", styles);
            Assert.DoesNotContain(".page[data-page-panel=\"controlCenter\"] label:has(input:disabled) { opacity:", styles);

        }

        [Fact]
        public void ControlCenter_PersistenceFailureHasVisibleLocalizedStatus()
        {
            string root = VerificationFixtureLocator.RepositoryRoot();
            string html = LoadWebViewAsset("index.html");
            string styles = LoadWebViewAsset("styles.css");

            Assert.Contains("id=\"preferences-status\"", html);
            Assert.Contains("role=\"status\"", html);
            Assert.Contains(".top-status #preferences-status.not-saved", styles);

            ReadBoardUiState state = new ReadBoardUiState
            {
                ControlCenter = new ReadBoardControlCenterState
                {
                    PreferencesSaved = false,
                    PreferencesStatus = "偏好已生效，但尚未保存",
                    PersistenceError = "disk full"
                }
            };
            using (JsonDocument document = JsonDocument.Parse(MainForm.SerializeWebViewState(state)))
            {
                JsonElement control = document.RootElement.GetProperty("payload").GetProperty("controlCenter");
                Assert.False(control.GetProperty("preferencesSaved").GetBoolean());
                Assert.Equal("偏好已生效，但尚未保存", control.GetProperty("preferencesStatus").GetString());
                Assert.Equal("disk full", control.GetProperty("persistenceError").GetString());
            }

            foreach (string language in new[] { "cn", "en", "jp", "kr" })
            {
                string languageFile = File.ReadAllText(Path.Combine(
                    root,
                    "readboard",
                    "language_" + language + ".txt"));
                Assert.Contains("WebView_preferencesSaved=", languageFile);
                Assert.Contains("WebView_preferencesNotSaved=", languageFile);
            }
        }

        [Fact]
        public void HostStatus_DistinguishesCompatibleModeFromConfirmedCommunication()
        {
            string html = LoadWebViewAsset("index.html");
            Assert.Contains("id=\"host-state\"", html);
            Assert.Contains("id=\"context-platform\"", html);
            Assert.DoesNotContain("当前通过 LizzieYzy-Next 启动", html);

            ReadBoardUiState state = new ReadBoardUiState
            {
                Shell = new ReadBoardShellState
                {
                    HostStatus = "宿主模式已启动",
                    Connected = false
                }
            };
            using (JsonDocument document = JsonDocument.Parse(MainForm.SerializeWebViewState(state)))
            {
                JsonElement shell = document.RootElement.GetProperty("payload").GetProperty("shell");
                Assert.Equal("宿主模式已启动", shell.GetProperty("hostStatus").GetString());
                Assert.False(shell.GetProperty("connected").GetBoolean());
            }
        }

        [Fact]
        public void SyncAndAnalysisActions_RenderIndependentHostState()
        {
            string html = LoadWebViewAsset("index.html");
            Assert.Contains("id=\"quick-label\"", html);
            Assert.Contains("id=\"continuous-label\"", html);
            Assert.Contains("id=\"analysis-label\"", html);

            ReadBoardUiState state = new ReadBoardUiState
            {
                ControlCenter = new ReadBoardControlCenterState
                {
                    QuickSyncLabel = "快速同步",
                    ContinuousSyncLabel = "持续同步 (200ms)",
                    AnalysisLabel = "暂停分析",
                    AnalysisToggleEnabled = true
                }
            };
            using (JsonDocument document = JsonDocument.Parse(MainForm.SerializeWebViewState(state)))
            {
                JsonElement control = document.RootElement.GetProperty("payload").GetProperty("controlCenter");
                Assert.Equal("快速同步", control.GetProperty("quickSyncLabel").GetString());
                Assert.Equal("持续同步 (200ms)", control.GetProperty("continuousSyncLabel").GetString());
                Assert.Equal("暂停分析", control.GetProperty("analysisLabel").GetString());
                Assert.True(control.GetProperty("analysisToggleEnabled").GetBoolean());
            }
        }

        [Fact]
        public void Theme_UsesHostPreferenceAndSemanticDarkPalette()
        {
            string styles = LoadWebViewAsset("styles.css");
            string script = LoadWebViewAsset("app.js");

            Assert.Contains(":root[data-theme=\"dark\"] {", styles);
            Assert.Contains("--window-background: #1e1e1e;", styles);
            Assert.Contains("--surface-background: #282828;", styles);
            Assert.Contains("--input-background: #323232;", styles);
            Assert.Contains("--primary-text: #d2d2d2;", styles);
            Assert.Contains("--secondary-text: #909090;", styles);
            Assert.Contains("const systemThemeQuery = window.matchMedia(\"(prefers-color-scheme: dark)\");", script);
            Assert.Contains("applyTheme(state.shell.theme || \"system\");", script);
            Assert.Contains("systemThemeQuery.addEventListener(\"change\"", script);
        }

        [Fact]
        public void Theme_SaveAppliesWithoutRestartDialogOrDuplicateHeaderShortcut()
        {
            string html = LoadWebViewAsset("index.html");
            string script = LoadWebViewAsset("app.js");

            Assert.Contains("data-page=\"settings\"", html);
            Assert.Contains("data-command=\"rules.openManual\"", html);
            Assert.DoesNotContain("shell.toggleTheme", html);
            Assert.DoesNotContain("themeRestart", script);
            Assert.Contains("data-command=\"settings.save\"", html);
        }

        [Fact]
        public void LocalizedPlatformChoices_ReserveEachLabelSingleLineWidth()
        {
            string styles = LoadWebViewAsset("styles.css");

            Assert.Contains(
                ".choice-grid.platforms { grid-template-columns: repeat(7, minmax(max-content, 1fr)); }",
                styles);
        }

        [Fact]
        public void LocalizedSettingSwitches_ReserveAStableControlColumn()
        {
            string styles = LoadWebViewAsset("styles.css");

            Assert.Contains(
                ".toggle-grid > label, .diagnostics { display: grid; min-width: 0; min-height: 42px; grid-template-columns: minmax(0, 1fr) 44px; align-items: center; gap: 10px; }",
                styles);
        }

        [Fact]
        public void LocalizedSettingDescriptions_UseCompactSingleLineCopy()
        {
            AssertLanguageValue("en", "WebView_autoMinimizeDescription", "Minimize after one-time sync");
            AssertLanguageValue("en", "WebView_backgroundAnalysisDescription", "Analyze during the opponent's turn");
            AssertLanguageValue("en", "WebView_magnifierDescription", "Magnify while selecting the board");
            AssertLanguageValue("en", "WebView_enhancedCaptureDescription", "Capture off-screen window content");
            AssertLanguageValue("en", "WebView_placementValidationDescription", "Verify the move after placement");

            AssertLanguageValue("jp", "WebView_autoMinimizeDescription", "単発同期後に最小化");
            AssertLanguageValue("jp", "WebView_backgroundAnalysisDescription", "双方向同期中も相手番を分析");
            AssertLanguageValue("jp", "WebView_magnifierDescription", "盤面選択中に拡大表示");
            AssertLanguageValue("jp", "WebView_enhancedCaptureDescription", "画面外のウィンドウも取得");
            AssertLanguageValue("jp", "WebView_placementValidationDescription", "着手後に配置結果を確認");

            AssertLanguageValue("kr", "WebView_autoMinimizeDescription", "일회 동기화 후 창 최소화");
            AssertLanguageValue("kr", "WebView_backgroundAnalysisDescription", "양방향 동기화 중 상대 차례 분석");
            AssertLanguageValue("kr", "WebView_magnifierDescription", "바둑판 선택 중 확대 표시");
            AssertLanguageValue("kr", "WebView_enhancedCaptureDescription", "화면 밖 창 내용도 캡처");
            AssertLanguageValue("kr", "WebView_placementValidationDescription", "착수 후 배치 결과 확인");
        }

        [Fact]
        public void CompactControls_UseNaturalEnglishCopy()
        {
            AssertLanguageValue("en", "MainForm_chkBothSync", "Two-way sync");
            AssertLanguageValue("en", "MainForm_chkAutoPlay", "Auto-play");
            AssertLanguageValue("en", "MainForm_radioBlack", "Black");
            AssertLanguageValue("en", "MainForm_radioWhite", "White");
            AssertLanguageValue("en", "MainForm_lblTime", "Time per move");
            AssertLanguageValue("en", "MainForm_lblTotalVisits", "Total visits (opt.)");
            AssertLanguageValue("en", "MainForm_lblBestMoveVisits", "Preferred visits (opt.)");
            AssertLanguageValue("en", "MainForm_btnClickBoard", "Select board (click inside)");
            AssertLanguageValue("en", "MainForm_btnCircleBoard", "Drag-select board");
            AssertLanguageValue("en", "MainForm_btnCircleRow1", "Select first row");
            AssertLanguageValue("en", "MainForm_chkShowInBoard", "Show on source board");
            AssertLanguageValue("en", "MainForm_btnOneTimeSync", "Sync once");
            AssertLanguageValue("en", "MainForm_btnClearBoard", "Clear board");
            AssertLanguageValue("en", "SettingsForm_btnReset", "Reset all");
            AssertLanguageValue("en", "SettingsForm_chkEnhanceScreen", "Enhanced capture");
        }

        [Fact]
        public void AboutPage_LabelsItsWindowsValueAsSupportedOs()
        {
            AssertLanguageValue("cn", "WebView_platformRuntime", "支持系统");
            AssertLanguageValue("en", "WebView_platformRuntime", "Supported OS");
            AssertLanguageValue("jp", "WebView_platformRuntime", "対応OS");
            AssertLanguageValue("kr", "WebView_platformRuntime", "지원 OS");
        }

        [Fact]
        public void JapanesePlatformModes_UseCompactSemanticLabels()
        {
            AssertLanguageValue("jp", "MainForm_rdoFoxBack", "野狐（背景着手）");
            AssertLanguageValue("jp", "MainForm_rdoBack", "その他（背景）");
            AssertLanguageValue("jp", "MainForm_rdoFore", "その他（前面）");
        }

        [Fact]
        public void LocalizedDiagnostics_UseCompactCopy()
        {
            AssertLanguageValue("en", "WebView_debugDiagnosticsDescription", "Save diagnostic capture details");
            AssertLanguageValue("jp", "WebView_debugDiagnosticsDescription", "調査用のキャプチャ情報を保存");
            AssertLanguageValue("kr", "WebView_debugDiagnosticsDescription", "문제 해결용 캡처 정보 저장");
        }

        private static string LoadWebViewAsset(string fileName)
        {
            string path = Path.Combine(
                VerificationFixtureLocator.RepositoryRoot(),
                "readboard",
                "WebView",
                fileName);
            return File.ReadAllText(path);
        }


        private static void AssertLanguageValue(string language, string key, string expected)
        {
            string line = Array.Find(
                File.ReadAllLines(Path.Combine(
                    VerificationFixtureLocator.RepositoryRoot(),
                    "readboard",
                    "language_" + language + ".txt")),
                candidate => candidate.StartsWith(key + "=", StringComparison.Ordinal));

            Assert.Equal(key + "=" + expected, line);
        }
    }
}
