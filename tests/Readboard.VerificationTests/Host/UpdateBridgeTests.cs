using System;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class UpdateBridgeTests
    {
        [Fact]
        public void HostedInstall_RequiresPipeActiveCapabilityAndCompleteAsset()
        {
            UpdateCheckResult result = CreateResult();

            Assert.True(MainForm.CanOfferWebViewHostedInstall(
                TransportKind.Pipe,
                true,
                true,
                result));
            Assert.False(MainForm.CanOfferWebViewHostedInstall(
                TransportKind.Tcp,
                true,
                true,
                result));
            Assert.False(MainForm.CanOfferWebViewHostedInstall(
                TransportKind.Pipe,
                false,
                true,
                result));
            Assert.False(MainForm.CanOfferWebViewHostedInstall(
                TransportKind.Pipe,
                true,
                false,
                result));

            result.AssetDownloadUrl = null;
            Assert.False(MainForm.CanOfferWebViewHostedInstall(
                TransportKind.Pipe,
                true,
                true,
                result));
        }

        [Fact]
        public void ManualDownload_UsesFixedRepositoryReleasePage()
        {
            Uri uri = MainForm.GetWebViewManualDownloadUri();
            var startInfo = MainForm.CreateWebViewDownloadStartInfo(uri);

            Assert.Equal(
                "https://github.com/qiyi71w/readboard/releases/latest",
                uri.AbsoluteUri);
            Assert.Equal(uri.AbsoluteUri, startInfo.FileName);
            Assert.True(startInfo.UseShellExecute);
        }

        [Theory]
        [InlineData("https://github.com/qiyi71w/readboard/releases/tag/v3.0.2", "https://github.com/qiyi71w/readboard/releases/tag/v3.0.2")]
        [InlineData("https://example.com/qiyi71w/readboard/releases/tag/v3.0.2", "https://github.com/qiyi71w/readboard/releases/latest")]
        [InlineData("http://github.com/qiyi71w/readboard/releases/tag/v3.0.2", "https://github.com/qiyi71w/readboard/releases/latest")]
        public void ManualDownload_UsesCheckedReleaseUrlOnlyWithinRepository(string releaseUrl, string expected)
        {
            UpdateCheckResult result = new UpdateCheckResult { ReleaseUrl = releaseUrl };

            Assert.Equal(expected, MainForm.ResolveWebViewManualDownloadUri(result).AbsoluteUri);
        }

        [Fact]
        public void DownloadStartInfo_RejectsNullUri()
        {
            Assert.Throws<ArgumentNullException>(
                () => MainForm.CreateWebViewDownloadStartInfo(null));
        }

        private static UpdateCheckResult CreateResult()
        {
            return new UpdateCheckResult
            {
                Tag = "v3.0.2",
                AssetName = "readboard-github-release-v3.0.2.zip",
                AssetDownloadUrl = "https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip",
                ReleaseUrl = "https://github.com/qiyi71w/readboard/releases/tag/v3.0.2"
            };
        }
    }
}
