using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class WebViewBridgeTests
    {
        [Fact]
        public void MissingRuntime_OffersOfficialEvergreenDownloadRetryAndExit()
        {
            Uri installerUri = MainForm.GetWebViewRuntimeInstallerUri();
            var startInfo = MainForm.CreateWebViewRuntimeDownloadStartInfo(installerUri);

            Assert.Equal(
                "https://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                installerUri.AbsoluteUri);
            Assert.Equal(installerUri.AbsoluteUri, startInfo.FileName);
            Assert.True(startInfo.UseShellExecute);
        }

        [Fact]
        public void MissingRuntimePrompt_RetryAndDownloadActionsReprobeUntilRuntimeAppears()
        {
            Queue<MainForm.WebViewRuntimePromptChoice> choices =
                new Queue<MainForm.WebViewRuntimePromptChoice>(new[]
                {
                    MainForm.WebViewRuntimePromptChoice.OpenDownload,
                    MainForm.WebViewRuntimePromptChoice.Retry,
                    MainForm.WebViewRuntimePromptChoice.Retry
                });
            int probeCount = 0;
            int openDownloadCount = 0;
            int exitCount = 0;

            bool available = MainForm.ResolveWebViewRuntimeAvailability(
                delegate
                {
                    probeCount++;
                    return probeCount == 4;
                },
                () => choices.Dequeue(),
                delegate { openDownloadCount++; },
                delegate { exitCount++; });

            Assert.True(available);
            Assert.Equal(4, probeCount);
            Assert.Equal(1, openDownloadCount);
            Assert.Equal(0, exitCount);
            Assert.Empty(choices);
        }

        [Fact]
        public void MissingRuntimePrompt_ExitDisposesAndStopsStartup()
        {
            int probeCount = 0;
            int exitCount = 0;

            bool available = MainForm.ResolveWebViewRuntimeAvailability(
                delegate
                {
                    probeCount++;
                    return false;
                },
                () => MainForm.WebViewRuntimePromptChoice.Exit,
                delegate { },
                delegate { exitCount++; });

            Assert.False(available);
            Assert.Equal(1, probeCount);
            Assert.Equal(1, exitCount);
        }

        [Fact]
        public void MissingRuntimePrompt_HasDefaultsAndAllLanguageOverrides()
        {
            string root = VerificationFixtureLocator.RepositoryRoot();
            string defaults = File.ReadAllText(Path.Combine(root, "readboard", "Program.cs"));
            string[] languages = { "cn", "en", "jp", "kr" };
            string[] keys =
            {
                "WebViewRuntime_caption",
                "WebViewRuntime_heading",
                "WebViewRuntime_message",
                "WebViewRuntime_openDownload",
                "WebViewRuntime_retry",
                "WebViewRuntime_exit",
                "WebViewRuntime_openDownloadFailed"
            };

            foreach (string key in keys)
            {
                Assert.Contains("langItems[\"" + key + "\"]", defaults);
                foreach (string language in languages)
                {
                    string content = File.ReadAllText(Path.Combine(
                        root,
                        "readboard",
                        "language_" + language + ".txt"));
                    Assert.Contains(key + "=", content);
                }
            }
        }

        [Theory]
        [InlineData(1100, 680, 1d)]
        [InlineData(1400, 900, 1d)]
        [InlineData(960, 600, 0.8727272727d)]
        [InlineData(800, 500, 0.8727272727d)]
        public void ResolveWebViewScale_UsesLimitingDimensionWithinSupportedRange(
            int width,
            int height,
            double expected)
        {
            double actual = WebViewWindowLayoutPolicy.ResolveScale(new Size(width, height));

            Assert.Equal(expected, actual, 8);
        }

        [Theory]
        [InlineData(96, 960, 600)]
        [InlineData(120, 1200, 750)]
        [InlineData(144, 1440, 900)]
        public void ScaleLogicalClientSize_UsesPerMonitorDpi(int dpi, int expectedWidth, int expectedHeight)
        {
            Size actual = WebViewWindowLayoutPolicy.ScaleLogicalSize(
                WebViewWindowLayoutPolicy.MinimumLogicalClientSize,
                dpi);

            Assert.Equal(new Size(expectedWidth, expectedHeight), actual);
        }

        [Theory]
        [InlineData(96, 1100, 680, 1100, 680)]
        [InlineData(120, 1375, 850, 1100, 680)]
        [InlineData(144, 1650, 1020, 1100, 680)]
        public void UnscalePhysicalClientSize_PersistsLogicalDimensions(
            int dpi,
            int width,
            int height,
            int expectedWidth,
            int expectedHeight)
        {
            Size actual = WebViewWindowLayoutPolicy.UnscalePhysicalSize(new Size(width, height), dpi);

            Assert.Equal(new Size(expectedWidth, expectedHeight), actual);
        }

        [Theory]
        [InlineData(1800, 900, 1200, 800, 0, 0, 1920, 1080, 720, 280, 1200, 800)]
        [InlineData(320, 240, 500, 400, 0, 0, 1920, 1080, 320, 240, 960, 600)]
        [InlineData(320, 240, 1100, 680, 0, 0, 800, 500, 0, 0, 800, 500)]
        public void ClampBoundsToWorkingArea_PreservesReachableUsableWindow(
            int x,
            int y,
            int width,
            int height,
            int workX,
            int workY,
            int workWidth,
            int workHeight,
            int expectedX,
            int expectedY,
            int expectedWidth,
            int expectedHeight)
        {
            Rectangle actual = WebViewWindowLayoutPolicy.ClampBoundsToWorkingArea(
                new Rectangle(x, y, width, height),
                new Rectangle(workX, workY, workWidth, workHeight),
                WebViewWindowLayoutPolicy.MinimumLogicalClientSize);

            Assert.Equal(new Rectangle(expectedX, expectedY, expectedWidth, expectedHeight), actual);
        }

        [Theory]
        [InlineData("{\"type\":\"window.minimize\"}")]
        [InlineData("{\"type\":\"window.maximize\",\"payload\":{}}")]
        [InlineData("{\"type\":\"sync.once\",\"payload\":null}")]
        [InlineData("{\"type\":\"navigate\",\"payload\":{\"page\":\"settings\"}}")]
        [InlineData("{\"type\":\"board.select\",\"payload\":{\"mode\":\"rectangle\"}}")]
        [InlineData("{\"type\":\"control.update\",\"payload\":{\"key\":\"platform\",\"value\":\"yike\"}}")]
        [InlineData("{\"type\":\"control.update\",\"payload\":{\"key\":\"two-way\",\"value\":true}}")]
        [InlineData("{\"type\":\"control.update\",\"payload\":{\"key\":\"board-width\",\"value\":\"25\"}}")]
        [InlineData("{\"type\":\"settings.save\",\"payload\":{}}")]
        [InlineData("{\"type\":\"about.checkUpdate\",\"payload\":{}}")]
        [InlineData("{\"type\":\"update.install\",\"payload\":{}}")]
        [InlineData("{\"type\":\"identity.select\",\"payload\":{\"candidateId\":\"candidate-1\"}}")]


        public void TryParseWebViewCommand_AcceptsWhitelistedShape(string json)
        {
            Assert.True(MainForm.TryParseWebViewCommand(json, out ReadBoardUiCommand command));
            Assert.NotNull(command);
        }
        [Fact]
        public void WebViewPublication_MapsIdentityNoOpAndCloseState()
        {
            FoxIdentitySelection selection = new FoxIdentitySelection(new IdentityPublicationPersistence());
            selection.Open(
                new[]
                {
                    new FoxIdentityCandidate(
                        "candidate-1",
                        SemanticMessage.Create("WebView_candidateRowNumber", 1),
                        "signature",
                        null)
                },
                false,
                AutoPlayColorMode.ManualBlack);

            selection.Select("candidate-1");
            FoxIdentitySelectionResult sameSelection = selection.Select("candidate-1");
            FoxIdentitySelectionResult rejectedSelection = selection.Select("missing");
            Assert.False(MainForm.ShouldPublishWebViewIdentityResult(sameSelection));
            Assert.False(MainForm.IsWebViewUpdateCloseAllowed(true, false));
            Assert.True(MainForm.IsWebViewUpdateCloseAllowed(true, true));
            Assert.True(MainForm.IsWebViewUpdateCloseAllowed(false, false));
            Assert.True(MainForm.ShouldPublishWebViewIdentityResult(rejectedSelection));

        }
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-json")]
        [InlineData("{}")]
        [InlineData("{\"type\":\"unknown\"}")]
        [InlineData("{\"type\":\"window.close\",\"payload\":{\"force\":true}}")]
        [InlineData("{\"type\":\"navigate\",\"payload\":{\"page\":\"external\"}}")]
        [InlineData("{\"type\":\"navigate\",\"payload\":{\"page\":\"about\",\"extra\":true}}")]
        [InlineData("{\"type\":\"control.update\",\"payload\":{\"key\":\"two-way\",\"value\":\"true\"}}")]
        [InlineData("{\"type\":\"control.update\",\"payload\":{\"key\":\"board-width\",\"value\":\"26\"}}")]
        [InlineData("{\"type\":\"control.update\",\"payload\":{\"key\":\"platform\",\"value\":\"unknown\"}}")]
        [InlineData("{\"type\":\"shell.toggleTheme\",\"payload\":{}}")]
        [InlineData("{\"type\":\"window.close\",\"extra\":true}")]
        [InlineData("{\"Type\":\"window.close\"}")]
        [InlineData("{\"type\":\"window.close\",\"type\":\"window.close\"}")]
        public void TryParseWebViewCommand_RejectsUnknownOrMalformedShape(string json)
        {
            Assert.False(MainForm.TryParseWebViewCommand(json, out _));
        }

        [Theory]
        [InlineData(null, JsonValueKind.Null)]
        [InlineData(false, JsonValueKind.False)]
        [InlineData(true, JsonValueKind.True)]
        public void SerializeWebViewState_PreservesTargetWindowTriState(bool? targetWindowValid, JsonValueKind expectedKind)
        {
            ReadBoardUiState state = new ReadBoardUiState();
            state.Shell.TargetWindowValid = targetWindowValid;

            using JsonDocument json = JsonDocument.Parse(MainForm.SerializeWebViewState(state));

            Assert.Equal("state", json.RootElement.GetProperty("type").GetString());
            Assert.Equal(expectedKind, json.RootElement.GetProperty("payload").GetProperty("shell").GetProperty("targetWindowValid").ValueKind);
        }

        [Fact]
        public void SerializeWebViewState_PreservesShellTheme()
        {
            ReadBoardUiState state = new ReadBoardUiState();
            state.Shell.Theme = "dark";

            using JsonDocument json = JsonDocument.Parse(MainForm.SerializeWebViewState(state));

            Assert.Equal(
                "dark",
                json.RootElement.GetProperty("payload").GetProperty("shell").GetProperty("theme").GetString());
        }

        [Fact]
        public void SerializeWebViewState_CarriesHostLanguageAndLocalizedText()
        {
            ReadBoardUiState state = new ReadBoardUiState();
            state.Language = "en";
            state.Text["WebView_navControlCenter"] = "Control Center";

            using JsonDocument json = JsonDocument.Parse(MainForm.SerializeWebViewState(state));
            JsonElement payload = json.RootElement.GetProperty("payload");

            Assert.Equal("en", payload.GetProperty("language").GetString());
            Assert.Equal(
                "Control Center",
                payload.GetProperty("text").GetProperty("WebView_navControlCenter").GetString());
        }

        [Fact]
        public void SerializeWebViewState_CarriesLocalizationTextOnEverySnapshot()
        {
            ReadBoardUiState first = new ReadBoardUiState
            {
                Text = new Dictionary<string, string>
                {
                    { "WebView_navControlCenter", "Control Center" }
                }
            };
            ReadBoardUiState second = new ReadBoardUiState
            {
                Text = new Dictionary<string, string>
                {
                    { "WebView_navControlCenter", "控制中心" }
                }
            };

            using JsonDocument firstJson = JsonDocument.Parse(MainForm.SerializeWebViewState(first));
            using JsonDocument secondJson = JsonDocument.Parse(MainForm.SerializeWebViewState(second));

            Assert.True(firstJson.RootElement.GetProperty("payload").TryGetProperty("text", out JsonElement firstText));
            Assert.True(secondJson.RootElement.GetProperty("payload").TryGetProperty("text", out JsonElement secondText));
            Assert.Equal("Control Center", firstText.GetProperty("WebView_navControlCenter").GetString());
            Assert.Equal("控制中心", secondText.GetProperty("WebView_navControlCenter").GetString());
        }
        [Fact]
        public void SerializeWebViewState_CarriesLatestUpdateVersion()
        {
            ReadBoardUiState state = new ReadBoardUiState
            {
                Update = new ReadBoardUpdateUiState
                {
                    Open = true,
                    Status = "available",
                    CurrentVersion = "3.0.0",
                    LatestVersion = "3.1.0"
                }
            };

            using JsonDocument json = JsonDocument.Parse(MainForm.SerializeWebViewState(state));

            JsonElement update = json.RootElement
                .GetProperty("payload")
                .GetProperty("update");
            Assert.Equal("3.1.0", update.GetProperty("latestVersion").GetString());
        }


        [Fact]
        public void WebViewShell_UsesHostLanguageForStaticNavigation()
        {
            string root = VerificationFixtureLocator.RepositoryRoot();
            string html = File.ReadAllText(Path.Combine(root, "readboard", "WebView", "index.html"));

            Assert.Contains("<html lang=\"und\">", html);
            Assert.Contains("data-i18n=\"WebView_navControlCenter\"", html);
            Assert.Contains("data-i18n-aria-label=\"WebView_windowControls\"", html);
            Assert.Contains("data-i18n-aria-label=\"WebView_mainNavigation\"", html);

            foreach (string language in new[] { "cn", "en", "jp", "kr" })
            {
                string languageFile = File.ReadAllText(Path.Combine(
                    root,
                    "readboard",
                    "language_" + language + ".txt"));
                Assert.Contains("WebView_navControlCenter=", languageFile);
            }
        }

        [Fact]
        public void WebViewVisibleText_UsesCompleteHostLanguageResources()
        {
            string root = VerificationFixtureLocator.RepositoryRoot();
            string html = File.ReadAllText(Path.Combine(root, "readboard", "WebView", "index.html"));
            string script = File.ReadAllText(Path.Combine(root, "readboard", "WebView", "app.js"));

            Assert.Contains("data-i18n=\"WebView_navControlCenter\"", html);
            Assert.Contains("data-i18n-aria-label=\"WebView_mainNavigation\"", html);
            Assert.Contains("id=\"log-list\" role=\"log\" aria-live=\"polite\"", html);

            string[] keys = ExtractLocalizationKeys(html, script);
            Assert.NotEmpty(keys);

            foreach (string language in new[] { "cn", "en", "jp", "kr" })
            {
                string[] languageLines = File.ReadAllLines(Path.Combine(
                    root,
                    "readboard",
                    "language_" + language + ".txt"));
                languageLines = languageLines.Where(line => line.Length != 0).ToArray();
                Assert.All(languageLines, line => Assert.Equal(1, line.Count(character => character == '=')));
                string[] languageKeys = languageLines
                    .Select(line => line.Substring(0, line.IndexOf('=')))
                    .ToArray();
                Assert.Equal(
                    languageKeys.Length,
                    languageKeys.Distinct(StringComparer.Ordinal).Count());
                Assert.All(keys, key => Assert.Contains(key, languageKeys));
            }
        }

        [Fact]
        public void WebViewAndShellLocalizationKeys_AreCoveredInAllLanguages()
        {
            string root = VerificationFixtureLocator.RepositoryRoot();
            string defaults = File.ReadAllText(Path.Combine(root, "readboard", "Program.cs"));
            LocalizationSource[] sources = LoadProductionLocalizationSources();
            string[] keys = ExtractLocalizationKeys(sources);

            Assert.NotEmpty(keys);
            string[] semanticKeys = ExtractSemanticMessageKeys(sources);
            Assert.NotEmpty(semanticKeys);
            Assert.All(keys, key => Assert.Contains("langItems[\"" + key + "\"]", defaults));
            foreach (string language in new[] { "cn", "en", "jp", "kr" })
            {
                string languageFile = File.ReadAllText(Path.Combine(
                    root,
                    "readboard",
                    "language_" + language + ".txt"));
                Assert.All(keys, key => Assert.Contains(key + "=", languageFile));
            }
        }

        [Fact]
        public void LocalizationKeyExtractor_IgnoresOrdinaryStringsDomAttributesAndDiagnostics()
        {
            string[] keys = ExtractLocalizationKeys(
                "<!-- <div data-i18n=\"WebView_fake\"></div> --> <div class=\"WebView_fake\" data-state=\"WebView_fake\" data-i18n=\"WebView_static\"></div>",
                "const diagnostic = \"WebView_fake\"; const ordinary = \"plain text\"; const label = 'diagnostic: t(\"WebView_fake\")'; const template = `t(\"WebView_fake\")`; t(\"WebView_script\");",
                "// t(\"WebView_fake\")" + Environment.NewLine + "/* getLangStr(\"WebView_fake\") */ t(\"WebView_script\");");

            Assert.Contains("WebView_static", keys);
            Assert.Contains("WebView_script", keys);
            Assert.DoesNotContain("WebView_fake", keys);
            Assert.DoesNotContain("plain", keys);
        }

        [Fact]
        public void LocalizationKeyExtractor_ResolvesVariableCarriedSemanticKeys()
        {
            string[] keys = ExtractSemanticMessageKeys(
                "string selectedKey = condition ? \"Update_upToDateRetired\" : \"Update_upToDate\"; SemanticMessage.Create(selectedKey);");
            string[] codeKeys = ExtractSemanticMessageKeys(
                "\"SemanticMessage.Create(\\\"WebView_fake\\\")\"; SemanticMessage.Create(\"WebView_real\");");

            Assert.Contains("Update_upToDateRetired", keys);
            Assert.Contains("Update_upToDate", keys);
            Assert.Contains("WebView_real", codeKeys);
            Assert.DoesNotContain("WebView_fake", codeKeys);
        }

        [Fact]
        public void WebViewSnapshotCarriesLocalizedDynamicTextAndSemanticLogDetails()
        {
            ReadBoardUiState state = new ReadBoardUiState
            {
                Shell = new ReadBoardShellState
                {
                    SyncStatus = "同步中",
                    HostStatus = "宿主通信正常",
                    TargetStatus = "目标窗口有效",
                    BoardStatus = "棋盘区域已识别",
                    PlacementStatus = "落子区域已解析"
                },
                ControlCenter = new ReadBoardControlCenterState
                {
                    PlatformLabel = "野狐",
                    NextTurn = "黑",
                    BindingStatus = "已绑定",
                    QuickSyncLabel = "快速同步",
                    ContinuousSyncLabel = "持续同步 (200ms)",
                    AnalysisLabel = "暂停分析",
                    PreferencesStatus = "偏好已保存"
                },
                Settings = new ReadBoardSettingsUiState
                {
                    DirtyStatus = "有尚未保存的更改",
                    Errors = new Dictionary<string, string>
                    {
                        { "syncInterval", "请输入不小于 20 的整数" }
                    }
                },
                Update = new ReadBoardUpdateUiState
                {
                    Open = true,
                    Status = "check-failed",
                    Title = "检查更新失败",
                    Detail = "网络错误"
                },
                Dialog = new ReadBoardDialogUiState
                {
                    Open = true,
                    Kind = "showInBoardHint",
                    Title = "提示",
                    Message = "前台同步不支持",
                    Detail = "开启双向同步可恢复落子",
                    ConfirmLabel = "确定",
                    DontShowAgainLabel = "不再提示"
                },
                Logs = new List<ReadBoardUiLogEntry>
                {
                    new ReadBoardUiLogEntry
                    {
                        Time = "12:34:56",
                        Level = "SYNC",
                        Message = "开始持续同步",
                        MessageKey = "WebView_continuousSyncStarted",
                        DiagnosticDetail = "200ms"
                    }
                }
            };

            using JsonDocument json = JsonDocument.Parse(MainForm.SerializeWebViewState(state));
            JsonElement payload = json.RootElement.GetProperty("payload");
            Assert.Equal("同步中", payload.GetProperty("shell").GetProperty("syncStatus").GetString());
            Assert.Equal("宿主通信正常", payload.GetProperty("shell").GetProperty("hostStatus").GetString());
            Assert.Equal("野狐", payload.GetProperty("controlCenter").GetProperty("platformLabel").GetString());
            Assert.Equal("请输入不小于 20 的整数", payload.GetProperty("settings").GetProperty("errors").GetProperty("syncInterval").GetString());
            Assert.Equal("有尚未保存的更改", payload.GetProperty("settings").GetProperty("dirtyStatus").GetString());
            Assert.Equal("检查更新失败", payload.GetProperty("update").GetProperty("title").GetString());
            Assert.Equal("网络错误", payload.GetProperty("update").GetProperty("detail").GetString());
            JsonElement dialog = payload.GetProperty("dialog");
            Assert.Equal("showInBoardHint", dialog.GetProperty("kind").GetString());
            Assert.Equal("提示", dialog.GetProperty("title").GetString());
            Assert.Equal("前台同步不支持", dialog.GetProperty("message").GetString());
            Assert.Equal("开启双向同步可恢复落子", dialog.GetProperty("detail").GetString());
            Assert.Equal("确定", dialog.GetProperty("confirmLabel").GetString());
            Assert.Equal("不再提示", dialog.GetProperty("dontShowAgainLabel").GetString());
            string html = File.ReadAllText(Path.Combine(
                VerificationFixtureLocator.RepositoryRoot(),
                "readboard",
                "WebView",
                "index.html"));
            Assert.Contains("id=\"host-state\"", html);
            Assert.Contains("id=\"context-platform\"", html);
            Assert.Contains("id=\"settings-dirty\"", html);
            Assert.Contains("id=\"settings-error\" class=\"settings-error\" role=\"alert\"", html);
            Assert.Contains("id=\"log-list\" role=\"log\" aria-live=\"polite\"", html);
            JsonElement log = payload.GetProperty("logs")[0];
            Assert.Equal("开始持续同步", log.GetProperty("message").GetString());
            Assert.Equal("WebView_continuousSyncStarted", log.GetProperty("messageKey").GetString());
            Assert.Equal("200ms", log.GetProperty("diagnosticDetail").GetString());

        }

        [Fact]
        public void DynamicSemanticMessages_PreserveFormattingPlaceholderSets()
        {
            string root = VerificationFixtureLocator.RepositoryRoot();
            string defaults = File.ReadAllText(Path.Combine(root, "readboard", "Program.cs"));
            string[] dynamicKeys = ExtractSemanticMessageKeys(LoadProductionLocalizationSources());
            foreach (string key in dynamicKeys)
            {
                string[] expected = ExtractFormatPlaceholders(ReadDefaultLanguageValue(defaults, key));
                foreach (string language in new[] { "cn", "en", "jp", "kr" })
                {
                    Assert.Equal(
                        expected.OrderBy(value => value),
                        ExtractFormatPlaceholders(ReadLanguageValue(root, language, key))
                            .OrderBy(value => value));
                }
            }
        }

        [Fact]
        public void SerializeWebViewState_SeparatesSyncModesAndAnalysisCapability()
        {
            ReadBoardUiState state = new ReadBoardUiState();
            state.ControlCenter.QuickSyncActive = true;
            state.ControlCenter.ContinuousSyncActive = false;
            state.ControlCenter.AnalysisRunning = false;
            state.ControlCenter.AnalysisStateAvailable = true;

            using JsonDocument json = JsonDocument.Parse(MainForm.SerializeWebViewState(state));
            JsonElement control = json.RootElement.GetProperty("payload").GetProperty("controlCenter");

            Assert.True(control.GetProperty("quickSyncActive").GetBoolean());
            Assert.False(control.GetProperty("continuousSyncActive").GetBoolean());
            Assert.False(control.GetProperty("analysisRunning").GetBoolean());
            Assert.True(control.GetProperty("analysisStateAvailable").GetBoolean());
        }



        [Fact]
        public void IsBoardRegionRecognized_RequiresViewportAndPositiveCapturedDimensions()
        {
            BoardFrame frame = new BoardFrame
            {
                Viewport = new BoardViewport { SourceBounds = new PixelRect(10, 20, 190, 190) }
            };

            Assert.True(MainForm.IsBoardRegionRecognized(frame, 190, 190));
            Assert.False(MainForm.IsBoardRegionRecognized(frame, 0, 190));
            Assert.False(MainForm.IsBoardRegionRecognized(frame, 190, 0));
            Assert.False(MainForm.IsBoardRegionRecognized(new BoardFrame(), 190, 190));
            Assert.False(MainForm.IsBoardRegionRecognized(null, 190, 190));
        }

        [Fact]
        public void ResetShellSyncState_ClearsOnlyRuntimeRecognitionState()
        {
            ReadBoardShellState shell = new ReadBoardShellState
            {
                Connected = true,
                BoardRegionRecognized = true,
                PlacementRegionResolved = true,
                LastSync = "12:34:56",
                StoneCount = 42
            };

            MainForm.ResetShellSyncState(shell);

            Assert.True(shell.Connected);
            Assert.False(shell.BoardRegionRecognized);
            Assert.False(shell.PlacementRegionResolved);
            Assert.Null(shell.LastSync);
            Assert.Equal(0, shell.StoneCount);
        }

        [Theory]
        [InlineData(0, 0, MainForm.HtTopLeft)]
        [InlineData(99, 0, MainForm.HtTopRight)]
        [InlineData(0, 79, MainForm.HtBottomLeft)]
        [InlineData(99, 79, MainForm.HtBottomRight)]
        [InlineData(0, 40, MainForm.HtLeft)]
        [InlineData(99, 40, MainForm.HtRight)]
        [InlineData(50, 0, MainForm.HtTop)]
        [InlineData(50, 79, MainForm.HtBottom)]
        [InlineData(50, 40, MainForm.HtClient)]
        [InlineData(-1, 40, MainForm.HtClient)]
        public void ResolveResizeHitTest_MapsEdgesAndClient(int x, int y, int expected)
        {
            Assert.Equal(expected, MainForm.ResolveResizeHitTest(new Point(x, y), new Size(100, 80), 6));
        }

        [Theory]
        [InlineData(1025, 24, false, MainForm.HtMaxButton)]
        [InlineData(1075, 24, false, MainForm.HtClient)]
        [InlineData(0, 0, false, MainForm.HtTopLeft)]
        [InlineData(1025, 24, true, MainForm.HtClient)]
        public void ResolveWebViewNonClientHitTest_PreservesResizeAndNativeMaximizeBehavior(
            int x,
            int y,
            bool maximized,
            int expected)
        {
            Assert.Equal(
                expected,
                MainForm.ResolveWebViewNonClientHitTest(
                    new Point(x, y),
                    new Size(1100, 680),
                    6,
                    48,
                    maximized));
        }

        [Fact]
        public void ResolveWebViewWindowStyle_EnablesNativeBorderlessResizeAndWindowCommands()
        {
            int style = MainForm.ResolveWebViewWindowStyle(0);

            Assert.Equal(MainForm.WsThickFrame, style & MainForm.WsThickFrame);
            Assert.Equal(MainForm.WsMinimizeBox, style & MainForm.WsMinimizeBox);
            Assert.Equal(MainForm.WsMaximizeBox, style & MainForm.WsMaximizeBox);
        }

        [Theory]
        [InlineData(false, false, "WebView_hostModeStarted")]
        [InlineData(true, false, "WebView_ready")]
        [InlineData(true, true, "WebView_syncing")]
        public void ResolveWebViewSyncStatusKey_DistinguishesHostModeFromConfirmedCommunication(
            bool communicationEstablished,
            bool activeSync,
            string expected)
        {
            Assert.Equal(
                expected,
                MainForm.ResolveWebViewSyncStatusKey(communicationEstablished, activeSync));
        }

        private static ReadBoardUiCommand ParseWebViewCommand(string json)
        {
            ReadBoardUiCommand command;
            Assert.True(MainForm.TryParseWebViewCommand(json, out command));
            return command;
        }



        private sealed class IdentityPublicationPersistence : IFoxIdentityPersistence
        {
            public string LoadSavedIdentitySignature()
            {
                return string.Empty;
            }

            public void SaveIdentitySignature(string signature)
            {
            }

            public void ClearSavedIdentity()
            {
            }
        }

        private sealed class LocalizationSource
        {
            public LocalizationSource(string filePath, string content)
            {
                FilePath = filePath;
                Content = content ?? string.Empty;
            }

            public string FilePath { get; private set; }
            public string Content { get; private set; }
        }

        private static LocalizationSource[] LoadProductionLocalizationSources()
        {
            string root = Path.Combine(
                VerificationFixtureLocator.RepositoryRoot(),
                "readboard");
            return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(IsProductionLocalizationSource)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new LocalizationSource(path, File.ReadAllText(path)))
                .ToArray();
        }

        private static bool IsProductionLocalizationSource(string path)
        {
            string extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".js", StringComparison.OrdinalIgnoreCase))
                return false;

            string root = Path.Combine(
                VerificationFixtureLocator.RepositoryRoot(),
                "readboard");
            string relative = Path.GetRelativePath(root, path);
            return !relative.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsHtmlSource(LocalizationSource source)
        {
            return string.Equals(
                    Path.GetExtension(source.FilePath),
                    ".html",
                    StringComparison.OrdinalIgnoreCase)
                || (source.FilePath == null
                    && source.Content.TrimStart().StartsWith("<", StringComparison.Ordinal));
        }

        private static bool IsJavaScriptSource(LocalizationSource source)
        {
            return string.Equals(
                    Path.GetExtension(source.FilePath),
                    ".js",
                    StringComparison.OrdinalIgnoreCase)
                || (source.FilePath == null
                    && (source.Content.IndexOf("t(", StringComparison.Ordinal) >= 0
                        || source.Content.IndexOf("const ", StringComparison.Ordinal) >= 0
                        || source.Content.IndexOf("let ", StringComparison.Ordinal) >= 0
                        || source.Content.IndexOf("var ", StringComparison.Ordinal) >= 0));
        }

        private static bool IsCSharpSource(LocalizationSource source)
        {
            return !IsHtmlSource(source) && !IsJavaScriptSource(source);
        }

        private static string StripComments(string source)
        {
            if (string.IsNullOrEmpty(source))
                return source ?? string.Empty;

            char[] result = source.ToCharArray();
            bool inString = false;
            bool inChar = false;
            bool escaped = false;
            bool inLineComment = false;
            bool inBlockComment = false;
            for (int index = 0; index < result.Length; index++)
            {
                char current = result[index];
                char next = index + 1 < result.Length ? result[index + 1] : (char)0;
                if (inLineComment)
                {
                    if (current == (char)10)
                        inLineComment = false;
                    else
                        result[index] = ' ';
                    continue;
                }
                if (inBlockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        result[index] = ' ';
                        result[++index] = ' ';
                        inBlockComment = false;
                    }
                    else if (current != (char)10 && current != (char)13)
                        result[index] = ' ';
                    continue;
                }
                if (inString)
                {
                    if (escaped)
                        escaped = false;
                    else if (current == (char)92)
                        escaped = true;
                    else if (current == (char)34)
                        inString = false;
                    continue;
                }
                if (inChar)
                {
                    if (escaped)
                        escaped = false;
                    else if (current == (char)92)
                        escaped = true;
                    else if (current == (char)39)
                        inChar = false;
                    continue;
                }
                if (current == '/' && next == '/')
                {
                    result[index] = ' ';
                    result[++index] = ' ';
                    inLineComment = true;
                    continue;
                }
                if (current == '/' && next == '*')
                {
                    result[index] = ' ';
                    result[++index] = ' ';
                    inBlockComment = true;
                    continue;
                }
                if (current == (char)34)
                    inString = true;
                else if (current == (char)39)
                    inChar = true;
            }
            return new string(result);
        }
        private static string MaskCSharpStringLiterals(string source)
        {
            char[] result = (source ?? string.Empty).ToCharArray();
            bool inString = false;
            bool inChar = false;
            bool escaped = false;
            for (int index = 0; index < result.Length; index++)
            {
                char current = result[index];
                if (inString)
                {
                    if (escaped)
                        escaped = false;
                    else if (current == (char)92)
                        escaped = true;
                    else if (current == (char)34)
                        inString = false;
                    if (current != (char)10 && current != (char)13)
                        result[index] = ' ';
                    continue;
                }
                if (inChar)
                {
                    if (escaped)
                        escaped = false;
                    else if (current == (char)92)
                        escaped = true;
                    else if (current == (char)39)
                        inChar = false;
                    if (current != (char)10 && current != (char)13)
                        result[index] = ' ';
                    continue;
                }
                if (current == (char)34)
                {
                    inString = true;
                    result[index] = ' ';
                }
                else if (current == (char)39)
                {
                    inChar = true;
                    result[index] = ' ';
                }
            }
            return new string(result);
        }

        private static string StripHtmlComments(string source)
        {
            return Regex.Replace(
                source ?? string.Empty,
                "<!--(?s:.*?)-->",
                string.Empty,
                RegexOptions.CultureInvariant);
        }

        private static string[] ExtractLocalizationKeys(params string[] sources)
        {
            return ExtractLocalizationKeys(
                sources.Select(source => new LocalizationSource(null, source)));
        }

        private static string[] ExtractLocalizationKeys(
            IEnumerable<LocalizationSource> sources)
        {
            string[] csharpPatterns =
            {
                "\\b(?:getLangStr|GetLangText|ResolveWebViewMessage|ShowWebViewMessage)\\(\\s*\\\"([A-Za-z][A-Za-z0-9_]*)\\\"",
                "\\blangItems\\[\\\"([A-Za-z][A-Za-z0-9_]*)\\\"]",
                "\\bconst\\s+string\\s+\\w+\\s*=\\s*\\\"((?:WebView|MainForm|Update|SettingsForm|FoxAutoPlayIdentityDialog|TipsForm|WebViewRuntime|MagnifierForm)_[A-Za-z0-9_]+)\\\"",
                "\\breturn\\s+\\\"((?:WebView|MainForm|Update|SettingsForm|FoxAutoPlayIdentityDialog|TipsForm|WebViewRuntime|MagnifierForm)_[A-Za-z0-9_]+)\\\""
            };
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            LocalizationSource[] sourceList = sources.ToArray();
            foreach (LocalizationSource source in sourceList)
            {
                if (IsHtmlSource(source))
                {
                    foreach (Match match in Regex.Matches(
                        StripHtmlComments(source.Content),
                        "data-i18n(?:-aria-label)?\\s*=\\s*[\\\"']([A-Za-z][A-Za-z0-9_]*)[\\\"']"))
                    {
                        keys.Add(match.Groups[1].Value);
                    }
                }
                else if (IsJavaScriptSource(source))
                {
                    keys.UnionWith(ExtractJavaScriptLocalizationKeys(source.Content));
                }
                else
                {
                    string value = StripComments(source.Content);
                    foreach (string pattern in csharpPatterns)
                    {
                        foreach (Match match in Regex.Matches(value, pattern))
                            keys.Add(match.Groups[1].Value);
                    }
                }
            }
            keys.UnionWith(ExtractSemanticMessageKeys(sourceList));
            return keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        }

        private static IEnumerable<string> ExtractJavaScriptLocalizationKeys(string source)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            ScanJavaScriptCode(source ?? string.Empty, 0, keys, false);
            return keys;
        }

        private static int ScanJavaScriptCode(
            string source,
            int start,
            ISet<string> keys,
            bool stopAtClosingBrace)
        {
            int index = start;
            int braceDepth = 0;
            while (index < source.Length)
            {
                char current = source[index];
                char next = index + 1 < source.Length ? source[index + 1] : (char)0;
                if (current == '/' && next == '/')
                {
                    index += 2;
                    while (index < source.Length && source[index] != (char)10)
                        index++;
                    continue;
                }
                if (current == '/' && next == '*')
                {
                    index += 2;
                    while (index + 1 < source.Length
                        && !(source[index] == '*' && source[index + 1] == '/'))
                        index++;
                    index = Math.Min(source.Length, index + 2);
                    continue;
                }
                if (current == (char)34 || current == (char)39)
                {
                    index = SkipJavaScriptQuotedLiteral(source, index);
                    continue;
                }
                if (current == '`')
                {
                    index = ScanJavaScriptTemplateLiteral(source, index, keys);
                    continue;
                }
                if (stopAtClosingBrace && current == '}' && braceDepth == 0)
                    return index + 1;
                if (current == '{')
                {
                    braceDepth++;
                    index++;
                    continue;
                }
                if (current == '}' && braceDepth > 0)
                {
                    braceDepth--;
                    index++;
                    continue;
                }
                if (current == 't'
                    && (index == 0 || !IsJavaScriptIdentifierPart(source[index - 1])))
                {
                    int afterName = index + 1;
                    while (afterName < source.Length
                        && char.IsWhiteSpace(source[afterName]))
                        afterName++;
                    if (afterName < source.Length && source[afterName] == '('
                        && (afterName + 1 >= source.Length
                            || !IsJavaScriptIdentifierPart(source[afterName + 1])))
                    {
                        int argument = afterName + 1;
                        while (argument < source.Length
                            && char.IsWhiteSpace(source[argument]))
                            argument++;
                        if (argument < source.Length
                            && (source[argument] == (char)34 || source[argument] == (char)39))
                        {
                            string key = ReadJavaScriptQuotedLiteral(source, argument);
                            if (Regex.IsMatch(
                                key,
                                "^[A-Za-z][A-Za-z0-9_]*$",
                                RegexOptions.CultureInvariant))
                                keys.Add(key);
                        }
                    }
                }
                index++;
            }
            return index;
        }

        private static int ScanJavaScriptTemplateLiteral(
            string source,
            int start,
            ISet<string> keys)
        {
            int index = start + 1;
            while (index < source.Length)
            {
                if (source[index] == (char)92)
                {
                    index = Math.Min(source.Length, index + 2);
                    continue;
                }
                if (source[index] == '`')
                    return index + 1;
                if (source[index] == '$'
                    && index + 1 < source.Length
                    && source[index + 1] == '{')
                {
                    index = ScanJavaScriptCode(source, index + 2, keys, true);
                    continue;
                }
                index++;
            }
            return index;
        }

        private static int SkipJavaScriptQuotedLiteral(string source, int start)
        {
            char quote = source[start];
            int index = start + 1;
            while (index < source.Length)
            {
                if (source[index] == (char)92)
                {
                    index = Math.Min(source.Length, index + 2);
                    continue;
                }
                if (source[index] == quote)
                    return index + 1;
                index++;
            }
            return index;
        }

        private static string ReadJavaScriptQuotedLiteral(string source, int start)
        {
            char quote = source[start];
            System.Text.StringBuilder value = new System.Text.StringBuilder();
            int index = start + 1;
            while (index < source.Length)
            {
                char current = source[index++];
                if (current == quote)
                    break;
                if (current == (char)92 && index < source.Length)
                    current = source[index++];
                value.Append(current);
            }
            return value.ToString();
        }

        private static bool IsJavaScriptIdentifierPart(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_' || value == '$';
        }

        private static string[] ExtractSemanticMessageKeys(params string[] sources)
        {
            return ExtractSemanticMessageKeys(
                sources.Select(source => new LocalizationSource(null, source)));
        }

        private static string[] ExtractSemanticMessageKeys(
            IEnumerable<LocalizationSource> sources)
        {
            const string keyPattern = "\\\"([A-Za-z][A-Za-z0-9_]*)\\\"";
            const string returnedKeyPattern = "\\\"((?:WebView|MainForm|Update|SettingsForm|FoxAutoPlayIdentityDialog|TipsForm|WebViewRuntime|MagnifierForm)_[A-Za-z0-9_]+)\\\"";
            Dictionary<string, HashSet<string>> variables = ExtractResourceVariables(sources);
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            LocalizationSource[] sourceList = sources.ToArray();
            AddSemanticInvocationKeys(
                sourceList,
                "\\bnew\\s+SemanticMessage\\s*\\(",
                0,
                variables,
                keys,
                keyPattern);
            AddSemanticInvocationKeys(
                sourceList,
                "\\bSemanticMessage\\.(?:Create|CreateWithDiagnostic)\\s*\\(",
                0,
                variables,
                keys,
                keyPattern);
            AddSemanticInvocationKeys(
                sourceList,
                "\\bSemanticMessage\\.(?:CreateLog|CreateLogWithDiagnostic)\\s*\\(",
                1,
                variables,
                keys,
                keyPattern);
            AddSemanticInvocationKeys(
                sourceList,
                "\\b(?:WithSemanticLog|ShowWebViewMessage)\\s*\\(",
                1,
                variables,
                keys,
                keyPattern);
            AddSemanticInvocationKeys(
                sourceList,
                "\\bFailCurrentOperation\\s*\\(",
                2,
                variables,
                keys,
                keyPattern);

            foreach (LocalizationSource source in sourceList)
            {
                if (!IsCSharpSource(source))
                    continue;
                foreach (Match match in Regex.Matches(
                    StripComments(source.Content),
                    "\\breturn\\s+" + returnedKeyPattern))
                {
                    keys.Add(match.Groups[1].Value);
                }
            }

            return keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        }

        private static Dictionary<string, HashSet<string>> ExtractResourceVariables(
            IEnumerable<LocalizationSource> sources)
        {
            Dictionary<string, HashSet<string>> variables =
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            const string declarationPattern =
                "\\b(?:const\\s+)?string\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*(?<expression>[^;]+);";
            const string keyPattern =
                "\\\"((?:WebView|MainForm|Update|SettingsForm|FoxAutoPlayIdentityDialog|TipsForm|WebViewRuntime|MagnifierForm)_[A-Za-z0-9_]+)\\\"";
            foreach (LocalizationSource source in sources)
            {
                if (!IsCSharpSource(source))
                    continue;
                foreach (Match declaration in Regex.Matches(
                    StripComments(source.Content),
                    declarationPattern))
                {
                    HashSet<string> values = new HashSet<string>(StringComparer.Ordinal);
                    foreach (Match key in Regex.Matches(
                        declaration.Groups["expression"].Value,
                        keyPattern))
                    {
                        values.Add(key.Groups[1].Value);
                    }
                    if (values.Count != 0)
                        variables[declaration.Groups["name"].Value] = values;
                }
            }
            return variables;
        }

        private static void AddSemanticInvocationKeys(
            IEnumerable<LocalizationSource> sources,
            string invocationPattern,
            int keyArgumentIndex,
            IDictionary<string, HashSet<string>> variables,
            ISet<string> keys,
            string keyPattern)
        {
            Regex callPattern = new Regex(invocationPattern, RegexOptions.CultureInvariant);
            foreach (LocalizationSource source in sources)
            {
                if (!IsCSharpSource(source))
                    continue;
                string value = StripComments(source.Content);
                string code = MaskCSharpStringLiterals(value);
                foreach (Match call in callPattern.Matches(code))
                {
                    int open = code.IndexOf('(', call.Index, call.Length);
                    int close = FindMatchingParenthesis(value, open);
                    if (open < 0 || close < 0)
                        continue;
                    List<string> arguments = SplitTopLevelArguments(
                        value.Substring(open + 1, close - open - 1));
                    if (keyArgumentIndex >= arguments.Count)
                        continue;

                    string argument = arguments[keyArgumentIndex];
                    foreach (Match key in Regex.Matches(argument, keyPattern))
                        keys.Add(key.Groups[1].Value);

                    string identifier = argument.Trim();
                    HashSet<string> values;
                    if (variables.TryGetValue(identifier, out values))
                        keys.UnionWith(values);
                    int separator = identifier.LastIndexOf('.');
                    if (separator >= 0
                        && variables.TryGetValue(identifier.Substring(separator + 1), out values))
                    {
                        keys.UnionWith(values);
                    }
                }
            }
        }

        private static int FindMatchingParenthesis(string source, int open)
        {
            if (open < 0)
                return -1;
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            bool inLineComment = false;
            bool inBlockComment = false;
            for (int index = open; index < source.Length; index++)
            {
                char current = source[index];
                char next = index + 1 < source.Length ? source[index + 1] : (char)0;
                if (inLineComment)
                {
                    if (current == (char)10)
                        inLineComment = false;
                    continue;
                }
                if (inBlockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        inBlockComment = false;
                        index++;
                    }
                    continue;
                }
                if (inString)
                {
                    if (escaped)
                        escaped = false;
                    else if (current == (char)92)
                        escaped = true;
                    else if (current == (char)34)
                        inString = false;
                    continue;
                }
                if (current == '/' && next == '/')
                {
                    inLineComment = true;
                    index++;
                    continue;
                }
                if (current == '/' && next == '*')
                {
                    inBlockComment = true;
                    index++;
                    continue;
                }
                if (current == (char)34)
                {
                    inString = true;
                    continue;
                }
                if (current == '(')
                    depth++;
                else if (current == ')' && --depth == 0)
                    return index;
            }
            return -1;
        }

        private static List<string> SplitTopLevelArguments(string value)
        {
            List<string> arguments = new List<string>();
            int start = 0;
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (inString)
                {
                    if (escaped)
                        escaped = false;
                    else if (current == (char)92)
                        escaped = true;
                    else if (current == (char)34)
                        inString = false;
                    continue;
                }
                if (current == (char)34)
                {
                    inString = true;
                    continue;
                }
                if (current == '(' || current == '[' || current == '{')
                    depth++;
                else if (current == ')' || current == ']' || current == '}')
                    depth--;
                else if (current == ',' && depth == 0)
                {
                    arguments.Add(value.Substring(start, index - start));
                    start = index + 1;
                }
            }
            arguments.Add(value.Substring(start));
            return arguments;
        }

        private static string ReadLanguageValue(string root, string language, string key)
        {
            string line = File.ReadAllLines(Path.Combine(
                root,
                "readboard",
                "language_" + language + ".txt"))
                .Single(value => value.StartsWith(key + "=", StringComparison.Ordinal));
            return line.Substring(key.Length + 1);
        }
        private static string ReadDefaultLanguageValue(string source, string key)
        {
            Match match = Regex.Match(
                source,
                "langItems\\[\\\"" + Regex.Escape(key) + "\\\"\\]\\s*=\\s*\\\"([^\\\"]*)\\\";");
            Assert.True(match.Success, "Missing default language value for " + key);
            return match.Groups[1].Value;
        }

        private static string[] ExtractFormatPlaceholders(string value)
        {
            return Regex.Matches(value, "\\{\\d+(?:,[^}]*)?(?::[^}]*)?\\}")
                .Cast<Match>()
                .Select(match => match.Value)
                .ToArray();
        }
    }
}
