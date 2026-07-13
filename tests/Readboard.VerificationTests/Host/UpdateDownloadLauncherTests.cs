using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class UpdateDownloadLauncherTests
    {
        [Fact]
        public void CreateDownloadStartInfo_OpensUrlThroughShell()
        {
            Uri downloadUri = new Uri("https://github.com/qiyi71w/readboard/releases/tag/v3.0.1");

            var startInfo = FormUpdate.CreateDownloadStartInfo(downloadUri);

            Assert.Equal(downloadUri.AbsoluteUri, startInfo.FileName);
            Assert.True(startInfo.UseShellExecute);
        }

        [Fact]
        public void CreateDownloadStartInfo_ThrowsOnNullUri()
        {
            ArgumentNullException exception =
                Assert.Throws<ArgumentNullException>(() => FormUpdate.CreateDownloadStartInfo(null));

            Assert.Equal("downloadUri", exception.ParamName);
        }

        [Fact]
        public void DownloadClick_UsesShellLauncherAndLogsFailures()
        {
            string source = LoadReadboardSource("FormUpdate.cs");
            string clickSlice = GetMethodSlice(source, "private async void btnDownload_Click(object sender, EventArgs e)");
            string manualSlice = GetMethodSlice(source, "private void OpenManualDownload()");
            string openSlice = GetMethodSlice(source, "internal static void OpenDownloadUri(Uri downloadUri)");

            Assert.Contains("if (CanUseHostedInstall())", clickSlice);
            Assert.Contains("await BeginHostedInstallAsync();", clickSlice);
            Assert.Contains("OpenManualDownload();", clickSlice);
            Assert.Contains("OpenDownloadUri(downloadUri);", manualSlice);
            Assert.Contains("catch (Exception exception)", manualSlice);
            Assert.Contains("Trace.TraceError(exception.ToString());", manualSlice);
            Assert.Contains("using (Process process = Process.Start(CreateDownloadStartInfo(downloadUri)))", openSlice);
        }

        [Fact]
        public void BuildHostedUpdateMessage_AppendsManualFallback()
        {
            string message = FormUpdate.BuildHostedUpdateMessage(
                "Host installation failed.",
                "bad zip",
                "Falling back to manual download.",
                "Default fallback");

            Assert.Equal(
                "Host installation failed." + Environment.NewLine +
                "bad zip" + Environment.NewLine +
                "Falling back to manual download.",
                message);
        }

        [Fact]
        public void ShowUpdateAvailable_WiresHostedInstallModelThroughPipeCapabilityAndAsset()
        {
            string source = LoadReadboardSource("Form1.cs");
            string methodSlice = GetMethodSlice(source, "private void ShowUpdateAvailable(UpdateCheckResult result)");
            string prepareSlice = GetMethodSlice(source, "private async Task<string> PrepareHostedUpdatePackageAsync(UpdateDialogModel model)");

            Assert.Contains("launchOptions.TransportKind == TransportKind.Pipe", methodSlice);
            Assert.Contains("sessionCoordinator.IsProtocolSessionActive", methodSlice);
            Assert.Contains("hostedUpdateSupported", methodSlice);
            Assert.Contains("HostedInstallAvailable = hostedInstallAvailable", methodSlice);
            Assert.Contains("HostedAssetSha256 = result.AssetSha256", methodSlice);
            Assert.Contains("DownloadingPackageStatusText = getLangStr(\"Update_downloadingPackage\")", methodSlice);
            Assert.Contains("VerifyingPackageStatusText = getLangStr(\"Update_verifyingPackage\")", methodSlice);
            Assert.Contains("NotifyingHostStatusText = getLangStr(\"Update_notifyingHost\")", methodSlice);
            Assert.Contains("HostInstallingStatusText = getLangStr(\"Update_hostInstalling\")", methodSlice);
            Assert.Contains("PrepareHostedUpdateAsync = PrepareHostedUpdatePackageAsync", methodSlice);
            Assert.Contains("NotifyHostedUpdateReady = NotifyHostedUpdateReady", methodSlice);
            Assert.Contains("activeHostedUpdateDialog = formUpdate;", methodSlice);
            Assert.Contains("model.ReportHostedUpdateStatus?.Invoke(", prepareSlice);
            Assert.Contains("model.VerifyingPackageStatusText,", prepareSlice);
            Assert.Contains("\"Verifying update package...\"", prepareSlice);
            Assert.Contains("model.HostedAssetSha256", prepareSlice);
            Assert.True(
                prepareSlice.IndexOf("downloader.DownloadAsync(", StringComparison.Ordinal) <
                prepareSlice.IndexOf("new HostedUpdatePackageVerifier().Verify", StringComparison.Ordinal));
        }

        [Fact]
        public void HostedUpdateDialog_ReportsVisiblePackageAndHostStages()
        {
            string formSource = LoadReadboardSource("FormUpdate.cs");
            string designerSource = LoadReadboardSource("FormUpdate.Designer.cs");
            string beginSlice = GetMethodSlice(formSource, "private async Task BeginHostedInstallAsync()");
            string installingSlice = GetMethodSlice(formSource, "internal void MarkHostedInstalling()");

            Assert.Contains("lblHostedUpdateStatus", designerSource);
            Assert.Contains("model.ReportHostedUpdateStatus = UpdateHostedStatus;", formSource);
            Assert.Contains("UpdateHostedStatus(model.DownloadingPackageStatusText, DefaultDownloadingPackageStatusText);", beginSlice);
            Assert.Contains("model.NotifyingHostStatusText", beginSlice);
            Assert.Contains("DefaultNotifyingHostStatusText", beginSlice);
            Assert.Contains("UpdateHostedStatus(model.WaitingForHostInstallText, DefaultWaitingForHostInstallText);", beginSlice);
            Assert.Contains("await PrepareAndNotifyHostedUpdateAsync(", beginSlice);
            Assert.Contains("UpdateHostedStatus(model.HostInstallingStatusText, DefaultHostInstallingStatusText);", installingSlice);
        }

        [Fact]
        public async Task PrepareAndNotifyHostedUpdateAsync_DoesNotNotifyWhenPreparationFails()
        {
            bool notified = false;
            var model = new UpdateDialogModel
            {
                HostedReleaseTag = "v3.0.2",
                PrepareHostedUpdateAsync = ignored =>
                    Task.FromException<string>(new InvalidOperationException("SHA-256 mismatch")),
                NotifyHostedUpdateReady = (tag, path) => notified = true
            };

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => FormUpdate.PrepareAndNotifyHostedUpdateAsync(model, null));

            Assert.Equal("SHA-256 mismatch", exception.Message);
            Assert.False(notified);
        }

        [Fact]
        public async Task PrepareAndNotifyHostedUpdateAsync_NotifiesOnlyAfterPreparationCompletes()
        {
            var stages = new List<string>();
            var model = new UpdateDialogModel
            {
                HostedReleaseTag = "v3.0.2",
                PrepareHostedUpdateAsync = ignored =>
                {
                    stages.Add("prepare");
                    return Task.FromResult("package.zip");
                },
                NotifyHostedUpdateReady = (tag, path) => stages.Add("notify")
            };

            await FormUpdate.PrepareAndNotifyHostedUpdateAsync(
                model,
                () =>
                {
                    stages.Add("before-notify");
                    return true;
                });

            Assert.Equal(new[] { "prepare", "before-notify", "notify" }, stages);
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

        [Fact]
        public void ManualUpdateResultsExposeRetiredOutsideAndNoChannelSemantics()
        {
            string source = LoadReadboardSource("Form1.cs");
            string availableSlice = GetMethodSlice(
                source,
                "private void ShowUpdateAvailable(UpdateCheckResult result)");
            string resultSlice = GetMethodSlice(
                source,
                "private void HandleUpdateCheckResult(UpdateCheckResult result)");
            string upToDateSlice = GetMethodSlice(
                source,
                "private void ShowUpdateUpToDate(UpdateCheckResult result)");
            string outsideSlice = GetMethodSlice(
                source,
                "private string BuildOutsideChannelMessage(UpdateCheckResult result)");

            Assert.Contains("Update_retiredFinalVersion", availableSlice);
            Assert.Contains("UpdateCheckStatus.OutsideChannel", resultSlice);
            Assert.Contains("Update_outsideChannel", outsideSlice);
            Assert.Contains("UpdateCheckStatus.NoMatchingChannel", resultSlice);
            Assert.Contains("Update_noMatchingChannel", resultSlice);
            Assert.Contains("Update_upToDateRetired", upToDateSlice);
            Assert.Contains("Update_retiredFinalVersion", outsideSlice);
            Assert.Contains("Update_newerVersionRequiresWindows", source);
        }

        [Fact]
        public void ManualUpdateResultLanguageKeysExistInEveryLanguage()
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
                "Update_retiredFinalVersion",
                "Update_upToDateRetired",
                "Update_outsideChannel",
                "Update_noMatchingChannel",
                "Update_newerVersionRequiresWindows"
            })
            {
                Assert.Contains("langItems[\"" + key + "\"]", programSource);
                foreach (string languageSource in languageSources)
                {
                    Assert.Contains(key + "=", languageSource);
                }
            }
        }

        private static string LoadReadboardSource(string fileName)
        {
            string directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                string candidate = Path.Combine(directory, "readboard", fileName);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            string assemblyDirectory = Path.GetDirectoryName(typeof(FormUpdate).Assembly.Location);
            string fallback = Path.Combine(assemblyDirectory ?? string.Empty, fileName);
            if (File.Exists(fallback))
            {
                return File.ReadAllText(fallback);
            }

            throw new FileNotFoundException("Could not locate readboard source file.", fileName);
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
