using System;
using System.IO;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class WebViewUiPolishTests
    {
        [Fact]
        public void MovePlacementSelector_UsesApprovedTypographyAndSpacing()
        {
            string styles = LoadWebViewAsset("styles.css");

            Assert.Contains(".placement-row > b { font-size: 14px; }", styles);
            Assert.Contains(".placement-row { display: flex; min-width: 0; min-height: 37px; align-items: center; gap: 14px;", styles);
            Assert.Contains(".placement-row .segments label { min-height: 32px; padding: 6px 12px; font-size: 14px; }", styles);
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
