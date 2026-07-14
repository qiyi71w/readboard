using System;
using System.IO;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class UpdateDownloadLauncherTests
    {
        private const string PromotedSha256 =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        [Fact]
        public void WebViewUpdate_WiresPromotedHashHostedInstallStagesAndResponseTimeout()
        {
            string source = LoadReadboardSource("MainForm.WebView.Update.cs");
            string installSlice = GetMethodSlice(source, "internal async Task InstallWebViewUpdateAsync()");
            string closeSlice = GetMethodSlice(source, "internal void CloseWebViewUpdate()");

            Assert.Contains("webViewUpdateState.Status != \"available\"", installSlice);
            Assert.Contains("CanOfferWebViewHostedInstall(", installSlice);
            Assert.Contains("result.AssetSha256", installSlice);
            Assert.Contains("new HostedUpdatePackageVerifier().Verify", installSlice);
            Assert.Contains("sessionCoordinator.SendReadboardUpdateReady", installSlice);
            Assert.Contains("webViewHostedUpdateResponseTimer.Start();", installSlice);
            Assert.Contains("WebViewHostedUpdateResponseTimeoutMilliseconds = 15000", source);
            Assert.Contains("webViewUpdateOperationId++", closeSlice);
        }

        [Theory]
        [InlineData("readboard-github-release-v3.0.9.zip", "v3.0.9", false, true)]
        [InlineData("readboard-github-release-v3.0.9.zip", "v3.0.9", true, true)]
        [InlineData("readboard-webview2-v3.1.0.zip", "v3.1.0", false, false)]
        [InlineData("readboard-webview2-v3.1.0.zip", "v3.1.0", true, true)]
        [InlineData("readboard-v3.1.0.zip", "v3.1.0", true, false)]
        [InlineData("readboard-webview2-v3.1.1.zip", "v3.1.0", true, false)]
        public void HostedInstallEligibility_RequiresV2ForWebView2Packages(
            string assetName,
            string versionTag,
            bool packageV2Supported,
            bool expected)
        {
            var result = CreateResult(versionTag, assetName, PromotedSha256);

            Assert.Equal(
                expected,
                MainForm.CanOfferWebViewHostedInstall(
                    TransportKind.Pipe,
                    true,
                    true,
                    packageV2Supported,
                    result));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData(" ", false)]
        [InlineData(PromotedSha256, true)]
        public void HostedInstallEligibility_RequiresPromotedSha256(
            string assetSha256,
            bool expected)
        {
            UpdateCheckResult result = CreateResult(
                "v3.1.0",
                "readboard-webview2-v3.1.0.zip",
                assetSha256);

            Assert.Equal(
                expected,
                MainForm.CanOfferWebViewHostedInstall(
                    TransportKind.Pipe,
                    true,
                    true,
                    true,
                    result));
        }

        [Fact]
        public void WebViewUpdate_HandlesEveryChannelResultAndRetiredNotice()
        {
            string source = LoadReadboardSource("MainForm.WebView.Update.cs");
            string resultSlice = GetMethodSlice(source, "private void ApplyWebViewUpdateCheckResult(UpdateCheckResult result)");
            string checkSlice = GetMethodSlice(source, "internal async Task CheckForWebViewUpdateAsync()");

            Assert.Contains("UpdateCheckStatus.UpdateAvailable", resultSlice);
            Assert.Contains("UpdateCheckStatus.UpToDate", resultSlice);
            Assert.Contains("UpdateCheckStatus.OutsideChannel", resultSlice);
            Assert.Contains("UpdateCheckStatus.NoMatchingChannel", resultSlice);
            Assert.Contains("UpdateCheckStatus.Failed", resultSlice);
            Assert.DoesNotContain("ActivateWebViewManualDownloadFallback", resultSlice);
            Assert.DoesNotContain("ActivateWebViewManualDownloadFallback", checkSlice);
            Assert.Contains("Status = \"check-failed\"", source);
            Assert.Contains("Update_retiredFinalVersion", source);
            Assert.Contains("Update_upToDateRetired", source);
            Assert.Contains("Update_outsideChannel", source);
            Assert.Contains("Update_noMatchingChannel", source);
            Assert.Contains("Update_newerVersionRequiresWindows", source);
        }

        [Fact]
        public void WebViewAssets_SeedTheV31CandidateVersion()
        {
            string html = LoadReadboardSource("WebView\\index.html");
            string script = LoadReadboardSource("WebView\\app.js");

            Assert.DoesNotContain("v3.0.8", html);
            Assert.DoesNotContain("v3.0.8", script);
            Assert.Contains("v3.1.0", html);
            Assert.Contains("v3.1.0", script);
        }

        [Fact]
        public void HostedUpdateLanguageFiles_LocalizePackageHostAndChannelStates()
        {
            string programSource = LoadReadboardSource("Program.cs");
            string[] languageSources =
            {
                LoadReadboardSource("language_cn.txt"),
                LoadReadboardSource("language_en.txt"),
                LoadReadboardSource("language_jp.txt"),
                LoadReadboardSource("language_kr.txt")
            };

            foreach (string key in new[]
            {
                "Update_downloadingPackage",
                "Update_verifyingPackage",
                "Update_notifyingHost",
                "Update_hostInstalling",
                "Update_retiredFinalVersion",
                "Update_upToDateRetired",
                "Update_outsideChannel",
                "Update_noMatchingChannel",
                "Update_newerVersionRequiresWindows"
            })
            {
                Assert.Contains("langItems[\"" + key + "\"]", programSource);
                foreach (string languageSource in languageSources)
                    Assert.Contains(key + "=", languageSource);
            }
        }

        private static UpdateCheckResult CreateResult(
            string versionTag,
            string assetName,
            string assetSha256)
        {
            return new UpdateCheckResult
            {
                Tag = versionTag,
                AssetName = assetName,
                AssetDownloadUrl =
                    "https://github.com/qiyi71w/readboard/releases/download/" + versionTag + "/" + assetName,
                AssetSha256 = assetSha256,
                ReleaseUrl = "https://github.com/qiyi71w/readboard/releases/tag/" + versionTag
            };
        }

        private static string LoadReadboardSource(string fileName)
        {
            return File.ReadAllText(Path.Combine(
                VerificationFixtureLocator.RepositoryRoot(),
                "readboard",
                fileName));
        }

        private static string GetMethodSlice(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing method signature: " + signature);

            int braceStart = source.IndexOf('{', start);
            Assert.True(braceStart >= 0, "Missing method body: " + signature);

            int depth = 0;
            for (int index = braceStart; index < source.Length; index++)
            {
                if (source[index] == '{')
                    depth++;
                else if (source[index] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, index - start + 1);
                }
            }

            throw new InvalidOperationException("Could not parse method body: " + signature);
        }
    }
}
