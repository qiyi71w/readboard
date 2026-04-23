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
