using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
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
            string source = File.ReadAllText(Path.Combine(
                VerificationFixtureLocator.RepositoryRoot(),
                "readboard",
                "MainForm.WebView.cs"));

            Assert.Equal(
                "https://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                installerUri.AbsoluteUri);
            Assert.Equal(installerUri.AbsoluteUri, startInfo.FileName);
            Assert.True(startInfo.UseShellExecute);
            Assert.Contains("getLangStr(\"WebViewRuntime_openDownload\")", source);
            Assert.Contains("getLangStr(\"WebViewRuntime_retry\")", source);
            Assert.Contains("getLangStr(\"WebViewRuntime_exit\")", source);
            Assert.Contains("AppReleaseVersion.GetCurrentVersion()", source);
            Assert.DoesNotContain("ReadBoard v3.1.0", source);
            Assert.Contains("CoreWebView2Environment.GetAvailableBrowserVersionString();", source);
            Assert.DoesNotContain("InitializeComponent();", GetRuntimeCheckSlice(source));
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
        [InlineData(false, false, "宿主模式已启动")]
        [InlineData(true, false, "就绪")]
        [InlineData(true, true, "同步中")]
        public void ResolveWebViewSyncStatus_DistinguishesHostModeFromConfirmedCommunication(
            bool communicationEstablished,
            bool activeSync,
            string expected)
        {
            Assert.Equal(
                expected,
                MainForm.ResolveWebViewSyncStatus(communicationEstablished, activeSync));
        }

        private static string GetRuntimeCheckSlice(string source)
        {
            int start = source.IndexOf(
                "internal bool EnsureWebViewRuntimeAvailable()",
                StringComparison.Ordinal);
            Assert.True(start >= 0);
            int end = source.IndexOf(
                "private void InitializeWebViewShell()",
                start,
                StringComparison.Ordinal);
            Assert.True(end > start);
            return source.Substring(start, end - start);
        }
    }
}
