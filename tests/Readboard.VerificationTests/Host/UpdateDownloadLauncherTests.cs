using System;
using System.IO;
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

            Assert.Contains("launchOptions.TransportKind == TransportKind.Pipe", methodSlice);
            Assert.Contains("sessionCoordinator.IsProtocolSessionActive", methodSlice);
            Assert.Contains("hostedUpdateSupported", methodSlice);
            Assert.Contains("HostedInstallAvailable = hostedInstallAvailable", methodSlice);
            Assert.Contains("PrepareHostedUpdateAsync = PrepareHostedUpdatePackageAsync", methodSlice);
            Assert.Contains("NotifyHostedUpdateReady = NotifyHostedUpdateReady", methodSlice);
            Assert.Contains("activeHostedUpdateDialog = formUpdate;", methodSlice);
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
