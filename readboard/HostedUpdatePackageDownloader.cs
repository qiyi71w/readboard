using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace readboard
{
    internal sealed class HostedUpdatePackageDownloader : IHostedUpdatePackageDownloader
    {
        private const string GitHubAcceptHeader = "application/octet-stream";
        private const string GitHubUserAgent = "readboard-update-checker";
        private const int RequestTimeoutMilliseconds = 15000;

        private readonly string _packageRootDirectory;
        private readonly Func<Uri, string, CancellationToken, Task> _downloadAsync;

        public HostedUpdatePackageDownloader()
            : this(GetDefaultPackageRootDirectory(), DownloadPackageAsync)
        {
        }

        internal HostedUpdatePackageDownloader(
            string packageRootDirectory,
            Func<Uri, string, Task> downloadAsync)
            : this(
                packageRootDirectory,
                AdaptDownloadDelegate(downloadAsync))
        {
        }

        internal HostedUpdatePackageDownloader(
            string packageRootDirectory,
            Func<Uri, string, CancellationToken, Task> downloadAsync)
        {
            if (string.IsNullOrWhiteSpace(packageRootDirectory))
            {
                throw new ArgumentException("Package root directory is required.", nameof(packageRootDirectory));
            }

            if (downloadAsync == null)
            {
                throw new ArgumentNullException(nameof(downloadAsync));
            }

            _packageRootDirectory = packageRootDirectory;
            _downloadAsync = downloadAsync;
        }

        public Task<string> DownloadAsync(
            string versionTag,
            string assetName,
            string assetDownloadUrl,
            string expectedSha256)
        {
            return DownloadAsync(
                versionTag,
                assetName,
                assetDownloadUrl,
                expectedSha256,
                CancellationToken.None);
        }

        public Task<string> DownloadAsync(
            HostedUpdateRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return DownloadAsync(
                request.VersionTag,
                request.AssetName,
                request.AssetDownloadUrl,
                request.ExpectedSha256,
                cancellationToken);
        }

        private async Task<string> DownloadAsync(
            string versionTag,
            string assetName,
            string assetDownloadUrl,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(versionTag))
            {
                throw new ArgumentException("Version tag is required.", nameof(versionTag));
            }

            if (string.IsNullOrWhiteSpace(assetName))
            {
                throw new ArgumentException("Asset name is required.", nameof(assetName));
            }

            if (!string.Equals(assetName, Path.GetFileName(assetName), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Asset name must not contain directory separators.");
            }

            Uri downloadUri;
            if (!Uri.TryCreate(assetDownloadUrl, UriKind.Absolute, out downloadUri) ||
                !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Asset download URL must be an absolute HTTPS URL.");
            }

            string targetDirectory = Path.Combine(_packageRootDirectory, versionTag);
            Directory.CreateDirectory(targetDirectory);

            string finalPath = Path.Combine(targetDirectory, assetName);
            string tempPath = Path.Combine(
                targetDirectory,
                assetName + ".tmp-" + Guid.NewGuid().ToString("N"));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _downloadAsync(downloadUri, tempPath, cancellationToken).ConfigureAwait(false);
                string actualSha256 = await ComputeSha256Async(tempPath, cancellationToken).ConfigureAwait(false);

                if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Downloaded update package SHA-256 does not match the promoted package.");
                }

                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(tempPath, finalPath);
                cancellationToken.ThrowIfCancellationRequested();
                return finalPath;
            }
            catch
            {
                DeleteFileIfPresent(tempPath);
                if (cancellationToken.IsCancellationRequested)
                    DeletePackageArtifacts(finalPath);

                throw;
            }
        }

        public void Cleanup(HostedUpdateRequest request, string packagePath)
        {
            if (request == null)
                return;

            string finalPath = Path.Combine(
                Path.Combine(_packageRootDirectory, request.VersionTag),
                request.AssetName);
            DeletePackageArtifacts(finalPath);
            if (!string.IsNullOrWhiteSpace(packagePath) &&
                !string.Equals(packagePath, finalPath, StringComparison.OrdinalIgnoreCase))
                DeletePackageArtifacts(packagePath);
        }

        private static string GetDefaultPackageRootDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LizzieYzyNext",
                "readboard-updates");
        }

        private static Func<Uri, string, CancellationToken, Task> AdaptDownloadDelegate(
            Func<Uri, string, Task> downloadAsync)
        {
            if (downloadAsync == null)
                throw new ArgumentNullException(nameof(downloadAsync));

            return (downloadUri, destinationPath, cancellationToken) =>
                downloadAsync(downloadUri, destinationPath);
        }

        private static async Task DownloadPackageAsync(
            Uri downloadUri,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            using (handler)
            using (HttpClient client = CreateClient(handler))
            using (HttpResponseMessage response = await client.GetAsync(
                downloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using (Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                using (FileStream destinationStream = File.Create(destinationPath))
                {
                    await responseStream.CopyToAsync(destinationStream, 81920, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static async Task<string> ComputeSha256Async(
            string path,
            CancellationToken cancellationToken)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    sha256.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                }

                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return Convert.ToHexString(sha256.Hash).ToLowerInvariant();
            }
        }

        private static void DeletePackageArtifacts(string finalPath)
        {
            if (string.IsNullOrWhiteSpace(finalPath))
                return;

            DeleteFileIfPresent(finalPath);
            string directory = Path.GetDirectoryName(finalPath);
            string fileName = Path.GetFileName(finalPath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) ||
                !Directory.Exists(directory))
                return;

            foreach (string path in Directory.GetFiles(directory, fileName + ".tmp-*"))
                DeleteFileIfPresent(path);
        }

        private static void DeleteFileIfPresent(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;
            File.Delete(path);
        }

        private static HttpClient CreateClient(HttpClientHandler handler)
        {
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromMilliseconds(RequestTimeoutMilliseconds);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(GitHubAcceptHeader));
            client.DefaultRequestHeaders.UserAgent.ParseAdd(GitHubUserAgent);
            return client;
        }
    }
}
