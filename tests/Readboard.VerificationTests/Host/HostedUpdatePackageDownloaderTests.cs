using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class HostedUpdatePackageDownloaderTests
    {
        private const string PayloadSha256 =
            "239f59ed55e737c77147cf55ad0c1b030b6d7ee748a7426952f9b852d5a935e5";
        private const string EmptySha256 =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

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
                    "https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip",
                    PayloadSha256);

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
        public async Task DownloadAsync_DeletesTemporaryFileWhenSha256DoesNotMatch()
        {
            using (var workspace = new DownloadWorkspace())
            {
                HostedUpdatePackageDownloader downloader = new HostedUpdatePackageDownloader(
                    workspace.RootPath,
                    (downloadUri, destinationPath) =>
                        File.WriteAllTextAsync(destinationPath, "payload"));

                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => downloader.DownloadAsync(
                        "v3.0.2",
                        "readboard-github-release-v3.0.2.zip",
                        "https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip",
                        "0000000000000000000000000000000000000000000000000000000000000000"));

                Assert.Contains("SHA-256", exception.Message);
                Assert.Empty(Directory.GetFiles(workspace.RootPath, "*", SearchOption.AllDirectories));
            }
        }

        [Fact]
        public async Task DownloadAndVerify_RejectsEmptyPromotedFileAfterSha256Passes()
        {
            using (var workspace = new DownloadWorkspace())
            {
                HostedUpdatePackageDownloader downloader = new HostedUpdatePackageDownloader(
                    workspace.RootPath,
                    (downloadUri, destinationPath) =>
                        File.WriteAllBytesAsync(destinationPath, Array.Empty<byte>()));

                string zipPath = await downloader.DownloadAsync(
                    "v3.0.2",
                    "readboard-github-release-v3.0.2.zip",
                    "https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip",
                    EmptySha256);

                Assert.Throws<InvalidDataException>(
                    () => new HostedUpdatePackageVerifier().Verify("v3.0.2", zipPath));
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
                        "https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip",
                        PayloadSha256));

                Assert.Equal("download failed", exception.Message);
                Assert.Empty(Directory.GetFiles(workspace.RootPath, "*", SearchOption.AllDirectories));
            }
        }

        [Fact]
        public async Task DownloadAsync_CancellationDeletesPartialAndFinalArtifacts()
        {
            using (var workspace = new DownloadWorkspace())
            {
                var gate = new TaskCompletionSource<object>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using (var cancellation = new CancellationTokenSource())
                {
                    string temporaryPath = null;
                    HostedUpdatePackageDownloader downloader = new HostedUpdatePackageDownloader(
                        workspace.RootPath,
                        async (downloadUri, destinationPath, cancellationToken) =>
                        {
                            temporaryPath = destinationPath;
                            await File.WriteAllTextAsync(destinationPath, "partial");
                            await gate.Task;
                        });

                    HostedUpdateRequest request = new HostedUpdateRequest(
                        "v3.0.2",
                        "readboard-github-release-v3.0.2.zip",
                        "https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip",
                        PayloadSha256);
                    string finalPath = Path.Combine(
                        workspace.RootPath,
                        request.VersionTag,
                        request.AssetName);
                    Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
                    File.WriteAllText(finalPath, "stale candidate");
                    Task<string> download = downloader.DownloadAsync(request, cancellation.Token);

                    cancellation.Cancel();
                    gate.TrySetResult(null);

                    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await download);

                    Assert.NotNull(temporaryPath);
                    Assert.False(File.Exists(temporaryPath));
                    Assert.False(File.Exists(finalPath));
                    Assert.Empty(Directory.GetFiles(workspace.RootPath, "*", SearchOption.AllDirectories));
                }
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
