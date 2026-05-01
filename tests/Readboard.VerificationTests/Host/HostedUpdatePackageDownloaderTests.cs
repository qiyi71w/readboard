using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class HostedUpdatePackageDownloaderTests
    {
        [Fact]
        public async Task DownloadAsync_SavesPackageUnderVersionDirectory()
        {
            using (var workspace = new DownloadWorkspace())
            {
                Uri capturedUri = null;
                string capturedPath = null;
                HostedUpdatePackageDownloader downloader = new HostedUpdatePackageDownloader(
                    workspace.RootPath,
                    (downloadUri, destinationPath) =>
                    {
                        capturedUri = downloadUri;
                        capturedPath = destinationPath;
                        return File.WriteAllTextAsync(destinationPath, "payload");
                    });

                string resultPath = await downloader.DownloadAsync(
                    "v3.0.2",
                    "readboard-github-release-v3.0.2.zip",
                    "https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip");

                Assert.Equal(
                    "https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip",
                    capturedUri.AbsoluteUri);
                Assert.NotNull(capturedPath);
                Assert.NotEqual(resultPath, capturedPath);
                Assert.Equal(
                    Path.Combine(workspace.RootPath, "v3.0.2", "readboard-github-release-v3.0.2.zip"),
                    resultPath);
                Assert.True(File.Exists(resultPath));
                Assert.Equal("payload", File.ReadAllText(resultPath));
            }
        }

        [Fact]
        public async Task DownloadAsync_CleansUpTemporaryFileWhenDownloadFails()
        {
            using (var workspace = new DownloadWorkspace())
            {
                HostedUpdatePackageDownloader downloader = new HostedUpdatePackageDownloader(
                    workspace.RootPath,
                    async (downloadUri, destinationPath) =>
                    {
                        await File.WriteAllTextAsync(destinationPath, "partial");
                        throw new InvalidOperationException("download failed");
                    });

                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => downloader.DownloadAsync(
                        "v3.0.2",
                        "readboard-github-release-v3.0.2.zip",
                        "https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip"));

                Assert.Equal("download failed", exception.Message);
                Assert.Empty(Directory.GetFiles(workspace.RootPath, "*", SearchOption.AllDirectories));
            }
        }

        private sealed class DownloadWorkspace : IDisposable
        {
            public DownloadWorkspace()
            {
                RootPath = Path.Combine(
                    Path.GetTempPath(),
                    "readboard-hosted-update-download-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(RootPath);
            }

            public string RootPath { get; }

            public void Dispose()
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, true);
                }
            }
        }
    }
}
