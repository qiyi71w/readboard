using System;
using System.Threading;
using System.Threading.Tasks;

namespace readboard
{
    internal sealed class HostedUpdateRequest
    {
        public HostedUpdateRequest(
            string versionTag,
            string assetName,
            string assetDownloadUrl,
            string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(versionTag))
                throw new ArgumentException("Version tag is required.", nameof(versionTag));
            if (versionTag.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException("Version tag must be a path component.", nameof(versionTag));
            if (string.IsNullOrWhiteSpace(assetName))
                throw new ArgumentException("Asset name is required.", nameof(assetName));
            if (assetName.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException("Asset name must be a file name.", nameof(assetName));
            if (string.IsNullOrWhiteSpace(assetDownloadUrl))
                throw new ArgumentException("Asset download URL is required.", nameof(assetDownloadUrl));
            if (string.IsNullOrWhiteSpace(expectedSha256))
                throw new ArgumentException("Expected SHA-256 is required.", nameof(expectedSha256));

            VersionTag = versionTag;
            AssetName = assetName;
            AssetDownloadUrl = assetDownloadUrl;
            ExpectedSha256 = expectedSha256;
        }

        public string VersionTag { get; }

        public string AssetName { get; }

        public string AssetDownloadUrl { get; }

        public string ExpectedSha256 { get; }

    }


    internal interface IHostedUpdatePackageDownloader
    {
        Task<string> DownloadAsync(HostedUpdateRequest request, CancellationToken cancellationToken);

        void Cleanup(HostedUpdateRequest request, string packagePath);
    }

    internal interface IHostedUpdatePackageVerifier
    {
        void Verify(HostedUpdateRequest request, string packagePath, CancellationToken cancellationToken);
    }

}
