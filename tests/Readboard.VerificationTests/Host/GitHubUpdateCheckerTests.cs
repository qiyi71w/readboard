using System;
using System.Threading.Tasks;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class GitHubUpdateCheckerTests
    {
        private const string TypicalReleaseJson =
            "{\"tag_name\":\"v2.0.3\",\"name\":\"readboard v2.0.3\"," +
            "\"body\":\"Bug fixes and improvements.\"," +
            "\"html_url\":\"https://github.com/qiyi71w/readboard/releases/tag/v2.0.3\"," +
            "\"published_at\":\"2025-03-15T10:30:00Z\"}";

        private const string ReleaseWithMatchingAssetJson =
            "{\"tag_name\":\"v3.0.2\",\"name\":\"readboard v3.0.2\"," +
            "\"body\":\"Hosted update asset.\"," +
            "\"html_url\":\"https://github.com/qiyi71w/readboard/releases/tag/v3.0.2\"," +
            "\"published_at\":\"2026-05-01T10:30:00Z\"," +
            "\"assets\":[{" +
            "\"name\":\"readboard-github-release-v3.0.2.zip\"," +
            "\"browser_download_url\":\"https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip\"," +
            "\"size\":12345" +
            "}]}";

        [Fact]
        public void CheckAsync_ParsesTypicalReleaseResponse()
        {
            GitHubUpdateChecker checker = new GitHubUpdateChecker(
                () => "v1.0.0",
                () => Task.FromResult(TypicalReleaseJson));

            UpdateCheckResult result = checker.CheckAsync().Result;

            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.Equal("1.0.0", result.CurrentVersion);
            Assert.Equal("2.0.3", result.LatestVersion);
            Assert.Equal("https://github.com/qiyi71w/readboard/releases/tag/v2.0.3", result.ReleaseUrl);
            Assert.Equal("Bug fixes and improvements.", result.ReleaseNotes);
            Assert.NotNull(result.PublishedAt);
            Assert.Null(result.ErrorMessage);
        }

        [Fact]
        public void CheckAsync_ParsesMatchingReleaseAsset()
        {
            GitHubUpdateChecker checker = new GitHubUpdateChecker(
                () => "v3.0.1",
                () => Task.FromResult(ReleaseWithMatchingAssetJson));

            UpdateCheckResult result = checker.CheckAsync().Result;

            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.Equal("readboard-github-release-v3.0.2.zip", result.AssetName);
            Assert.Equal(
                "https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip",
                result.AssetDownloadUrl);
            Assert.Equal(12345L, result.AssetSize);
        }

        [Fact]
        public void CheckAsync_LeavesAssetFieldsEmptyWhenNoMatchingAssetExists()
        {
            string json =
                "{\"tag_name\":\"v3.0.2\"," +
                "\"html_url\":\"https://github.com/qiyi71w/readboard/releases/tag/v3.0.2\"," +
                "\"assets\":[{" +
                "\"name\":\"readboard-github-release-vv3.0.2.zip\"," +
                "\"browser_download_url\":\"https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-vv3.0.2.zip\"," +
                "\"size\":12345" +
                "}]}";
            GitHubUpdateChecker checker = new GitHubUpdateChecker(
                () => "v3.0.1",
                () => Task.FromResult(json));

            UpdateCheckResult result = checker.CheckAsync().Result;

            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.Equal("https://github.com/qiyi71w/readboard/releases/tag/v3.0.2", result.ReleaseUrl);
            Assert.Null(result.AssetName);
            Assert.Null(result.AssetDownloadUrl);
            Assert.Null(result.AssetSize);
        }

        [Fact]
        public void CheckAsync_LeavesAssetFieldsEmptyWhenMatchingAssetUrlIsNotHttps()
        {
            string json =
                "{\"tag_name\":\"v3.0.2\"," +
                "\"html_url\":\"https://github.com/qiyi71w/readboard/releases/tag/v3.0.2\"," +
                "\"assets\":[{" +
                "\"name\":\"readboard-github-release-v3.0.2.zip\"," +
                "\"browser_download_url\":\"http://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip\"," +
                "\"size\":12345" +
                "}]}";
            GitHubUpdateChecker checker = new GitHubUpdateChecker(
                () => "v3.0.1",
                () => Task.FromResult(json));

            UpdateCheckResult result = checker.CheckAsync().Result;

            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.Null(result.AssetName);
            Assert.Null(result.AssetDownloadUrl);
            Assert.Null(result.AssetSize);
        }

        [Fact]
        public void CheckAsync_ReturnsUpToDateWhenVersionsMatch()
        {
            GitHubUpdateChecker checker = new GitHubUpdateChecker(
                () => "v2.0.3",
                () => Task.FromResult(TypicalReleaseJson));

            UpdateCheckResult result = checker.CheckAsync().Result;

            Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
            Assert.Equal("2.0.3", result.CurrentVersion);
            Assert.Equal("2.0.3", result.LatestVersion);
        }

        [Fact]
        public void CheckAsync_HandlesNullOptionalFields()
        {
            string json =
                "{\"tag_name\":\"v1.0.0\",\"name\":null,\"body\":null," +
                "\"html_url\":\"https://example.com/release\",\"published_at\":null}";
            GitHubUpdateChecker checker = new GitHubUpdateChecker(
                () => "v1.0.0",
                () => Task.FromResult(json));

            UpdateCheckResult result = checker.CheckAsync().Result;

            Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
            Assert.Null(result.ReleaseNotes);
            Assert.Null(result.PublishedAt);
        }

        [Fact]
        public void CheckAsync_HandlesMissingOptionalFields()
        {
            string json =
                "{\"tag_name\":\"v1.0.0\"," +
                "\"html_url\":\"https://example.com/release\"}";
            GitHubUpdateChecker checker = new GitHubUpdateChecker(
                () => "v1.0.0",
                () => Task.FromResult(json));

            UpdateCheckResult result = checker.CheckAsync().Result;

            Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
            Assert.Null(result.ReleaseNotes);
            Assert.Null(result.PublishedAt);
        }

        [Fact]
        public void CheckAsync_ReportsFailureForEmptyResponse()
        {
            GitHubUpdateChecker checker = new GitHubUpdateChecker(
                () => "v1.0.0",
                () => Task.FromResult(""));

            UpdateCheckResult result = checker.CheckAsync().Result;

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.NotNull(result.ErrorMessage);
        }

        [Fact]
        public void CheckAsync_ReportsFailureForMissingRequiredField()
        {
            string json = "{\"name\":\"release\",\"html_url\":\"https://example.com\"}";
            GitHubUpdateChecker checker = new GitHubUpdateChecker(
                () => "v1.0.0",
                () => Task.FromResult(json));

            UpdateCheckResult result = checker.CheckAsync().Result;

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.Contains("tag_name", result.ErrorMessage);
        }

        [Fact]
        public void CheckAsync_ReportsFailureForInvalidJson()
        {
            GitHubUpdateChecker checker = new GitHubUpdateChecker(
                () => "v1.0.0",
                () => Task.FromResult("{broken json"));

            UpdateCheckResult result = checker.CheckAsync().Result;

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.NotNull(result.ErrorMessage);
        }

        [Fact]
        public void CheckAsync_ReportsFailureForNetworkException()
        {
            GitHubUpdateChecker checker = new GitHubUpdateChecker(
                () => "v1.0.0",
                () => { throw new Exception("Network error"); });

            UpdateCheckResult result = checker.CheckAsync().Result;

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.Equal("Network error", result.ErrorMessage);
        }
    }
}
