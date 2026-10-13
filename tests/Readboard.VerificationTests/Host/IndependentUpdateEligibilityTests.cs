using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class IndependentUpdateEligibilityTests
    {
        private const string PromotedSha256 =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        [Theory]
        [InlineData("readboard-github-release-v3.0.9.zip", "v3.0.9", false)]
        [InlineData("readboard-webview2-v3.1.0.zip", "v3.1.0", true)]
        [InlineData("readboard-v3.1.0.zip", "v3.1.0", false)]
        [InlineData("readboard-webview2-v3.1.1.zip", "v3.1.0", false)]
        public void InstallEligibility_RequiresMatchingMainPackage(
            string assetName,
            string versionTag,
            bool expected)
        {
            var result = CreateResult(versionTag, assetName, PromotedSha256);

            Assert.Equal(
                expected,
                MainForm.CanOfferWebViewInstall(result));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData(" ", false)]
        [InlineData(PromotedSha256, true)]
        public void InstallEligibility_RequiresPromotedSha256(
            string assetSha256,
            bool expected)
        {
            UpdateCheckResult result = CreateResult(
                "v3.1.0",
                "readboard-webview2-v3.1.0.zip",
                assetSha256);

            Assert.Equal(
                expected,
                MainForm.CanOfferWebViewInstall(result));
        }

        private static UpdateCheckResult CreateResult(
            string versionTag,
            string assetName,
            string assetSha256)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.UpdateAvailable,
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
