using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class HostedUpdateEligibilityTests
    {
        private const string PromotedSha256 =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

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
    }
}
