using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace readboard
{
    internal sealed class HostedUpdatePackageDownloader
    {
        private const string GitHubAcceptHeader = "application/octet-stream";
        private const string GitHubUserAgent = "readboard-update-checker";
        private const int RequestTimeoutMilliseconds = 15000;

        private readonly string _packageRootDirectory;
        private readonly Func<Uri, string, Task> _downloadAsync;

        public HostedUpdatePackageDownloader()
            : this(GetDefaultPackageRootDirectory(), DownloadPackageAsync)
        {
        }

        internal HostedUpdatePackageDownloader(
            string packageRootDirectory,
            Func<Uri, string, Task> downloadAsync)
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

        public async Task<string> DownloadAsync(
            string versionTag,
            string assetName,
            string assetDownloadUrl)
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
                await _downloadAsync(downloadUri, tempPath).ConfigureAwait(false);

                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }

                File.Move(tempPath, finalPath);
                return finalPath;
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                throw;
            }
        }

        private static string GetDefaultPackageRootDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LizzieYzyNext",
                "readboard-updates");
        }

        private static async Task DownloadPackageAsync(Uri downloadUri, string destinationPath)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            using (handler)
            using (HttpClient client = CreateClient(handler))
            using (HttpResponseMessage response = await client.GetAsync(
                downloadUri,
                HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using (Stream responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (FileStream destinationStream = File.Create(destinationPath))
                {
                    await responseStream.CopyToAsync(destinationStream).ConfigureAwait(false);
                }
            }
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
