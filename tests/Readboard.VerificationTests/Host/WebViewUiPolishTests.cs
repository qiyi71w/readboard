using System;
using System.IO;
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
            Assert.Contains(".placement-row .segments label { display: flex; flex: 1; min-width: 0; min-height: 32px; align-items: center; justify-content: center; padding: 6px 10px; font-size: 14px; }", styles);
        }

        [Fact]
        public void SyncControls_GroupRelatedOptionsIntoAlignedRows()
        {
            string html = LoadWebViewAsset("index.html");
            string styles = LoadWebViewAsset("styles.css");

            Assert.Contains("<h3>同步设置</h3>", html);
            Assert.Contains("<div class=\"sync-toggle-row\">", html);
            Assert.Contains("<div class=\"color-row\"><b>执子颜色</b>", html);
            Assert.Contains("<div class=\"segments color-segments\" role=\"radiogroup\" aria-label=\"执子颜色\">", html);
            Assert.Contains(".sync-toggle-row { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }", styles);
            Assert.Contains(".sync-options { display: grid; min-width: 0; grid-template-rows: auto repeat(3, 37px); gap: 4px;", styles);
            Assert.Contains(".color-row { display: grid; grid-template-columns: 62px minmax(0, 1fr) 90px; gap: 8px;", styles);
            Assert.Contains(".color-row button { width: 90px; height: 34px; min-height: 34px; }", styles);
            Assert.Contains(".color-row .segments label { display: flex; flex: 1; min-width: 0; min-height: 32px; align-items: center; justify-content: center; padding: 6px 10px; font-size: 14px; }", styles);
        }

        [Fact]
        public void ShowInBoardHint_UsesLegacyCopyWithoutShortcutAndOnlyExpectedActions()
        {
            string styles = LoadWebViewAsset("styles.css");
            string script = LoadWebViewAsset("app.js");

            Assert.Contains(".modal.show-in-board-hint { grid-template-rows: 44px auto 52px; }", styles);
            Assert.Contains(".modal.show-in-board-hint .modal-body { min-height: 0; padding: 12px 16px; overflow: hidden; }", styles);
            Assert.Contains("min(520px, calc(100vw - 48px))", script);
            Assert.Contains("openModal(\"dialog show-in-board-hint\", \"提示\"", script);
            Assert.Contains("[前台]方式同步时不支持此功能。选点显示在原棋盘上后，原棋盘将无法落子。", script);
            Assert.Contains("可通过勾选“双向同步”选项恢复落子功能。", script);
            Assert.DoesNotContain("Ctrl+X", script);
            Assert.Contains("button(\"dialog.dontShowAgain\", \"不再提示\") + button(\"dialog.confirm\",", script);
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
        public void EnginePlacement_DisablesFirstPolicyAfterRestoringItsValue()
        {
            string script = LoadWebViewAsset("app.js");
            int valueIndex = script.IndexOf("setValue(\"#first-policy\", control.firstPolicy ?? \"\");", StringComparison.Ordinal);
            int disabledIndex = script.IndexOf("setDisabled(\"#first-policy\", !control.firstPolicyEnabled);", StringComparison.Ordinal);

            Assert.True(valueIndex >= 0, "The first-policy value must be restored from state.");
            Assert.True(disabledIndex > valueIndex, "The host state must disable first-policy only after restoring its value.");
        }

        [Fact]
        public void HostStatus_DistinguishesCompatibleModeFromConfirmedCommunication()
        {
            string html = LoadWebViewAsset("index.html");
            string script = LoadWebViewAsset("app.js");

            Assert.Contains("宿主模式已启动", html);
            Assert.Contains("LizzieYzy-Next 棋盘同步工具", html);
            Assert.Contains("shell.connected ? \"宿主通信正常\" : \"宿主模式已启动\"", script);
            Assert.DoesNotContain("当前通过 LizzieYzy-Next 启动", html);
        }

        [Fact]
        public void SyncAndAnalysisActions_RenderIndependentHostState()
        {
            string script = LoadWebViewAsset("app.js");

            Assert.Contains("control.quickSyncActive ? \"停止快速同步\" : \"快速同步\"", script);
            Assert.Contains("control.continuousSyncActive ? \"停止持续同步\" : \"持续同步\"", script);
            Assert.Contains("control.analysisRunning ? \"暂停分析\" : \"继续分析\"", script);
            Assert.Contains("!control.analysisRunning && !control.analysisStateAvailable", script);
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
    }
}
