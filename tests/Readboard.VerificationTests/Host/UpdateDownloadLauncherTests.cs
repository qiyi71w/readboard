using System;
using System.IO;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class UpdateDownloadLauncherTests
    {
        [Fact]
        public void WebViewUpdate_WiresHostedInstallStagesAndResponseTimeout()
        {
            string source = LoadReadboardSource("MainForm.WebView.Update.cs");
            string installSlice = GetMethodSlice(source, "internal async Task InstallWebViewUpdateAsync()");
            string closeSlice = GetMethodSlice(source, "internal void CloseWebViewUpdate()");

            Assert.Contains("webViewUpdateState.Status != \"available\"", installSlice);
            Assert.Contains("CanOfferWebViewHostedInstall(", installSlice);
            Assert.Contains("downloader.DownloadAsync(", installSlice);
            Assert.Contains("new HostedUpdatePackageVerifier().Verify", installSlice);
            Assert.Contains("sessionCoordinator.SendReadboardUpdateReady", installSlice);
            Assert.Contains("webViewHostedUpdateResponseTimer.Start();", installSlice);
            Assert.Contains("WebViewHostedUpdateResponseTimeoutMilliseconds = 15000", source);
            Assert.Contains("webViewUpdateOperationId++", closeSlice);
        }

        [Fact]
        public void HostedUpdateLanguageFiles_LocalizePackageAndHostStages()
        {
            string programSource = LoadReadboardSource("Program.cs");
            string cnSource = LoadReadboardSource("language_cn.txt");
            string enSource = LoadReadboardSource("language_en.txt");
            string jpSource = LoadReadboardSource("language_jp.txt");
            string krSource = LoadReadboardSource("language_kr.txt");

            foreach (string key in new[]
            {
                "Update_downloadingPackage",
                "Update_verifyingPackage",
                "Update_notifyingHost",
                "Update_hostInstalling"
            })
            {
                Assert.Contains("langItems[\"" + key + "\"]", programSource);
                Assert.Contains(key + "=", cnSource);
                Assert.Contains(key + "=", enSource);
                Assert.Contains(key + "=", jpSource);
                Assert.Contains(key + "=", krSource);
            }

            Assert.DoesNotContain("Download and Install", jpSource);
            Assert.DoesNotContain("Waiting for Host Install", jpSource);
            Assert.DoesNotContain("Host installation", jpSource);
            Assert.DoesNotContain("Download and Install", krSource);
            Assert.DoesNotContain("Waiting for Host Install", krSource);
            Assert.DoesNotContain("Host installation", krSource);
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
                {
                    depth++;
                }
                else if (source[index] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(start, index - start + 1);
                    }
                }
            }

            throw new InvalidOperationException("Could not parse method body: " + signature);
        }
    }
}
