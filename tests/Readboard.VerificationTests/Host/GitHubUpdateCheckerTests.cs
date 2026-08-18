using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class GitHubUpdateCheckerTests
    {
        private const string LegacyAssetName = "readboard-github-release-v3.0.9.zip";
        private const string MainAssetName = "readboard-webview2-v3.1.0.zip";
        private const string PromotedSha256 =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        [Fact]
        public async Task CheckAsync_SelectsMainAtWindows10Version1809Boundary()
        {
            string requestedTag = null;
            GitHubUpdateChecker checker = CreateChecker(
                "v3.0.8",
                new Version(10, 0, 17763),
                BuildTwoChannelManifest(),
                tag =>
                {
                    requestedTag = tag;
                    return BuildRelease("v3.1.0", MainAssetName);
                });

            UpdateCheckResult result = await checker.CheckAsync();

            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.Equal("main", result.ChannelId);
            Assert.Equal("active", result.ChannelStatus);
            Assert.Equal("v3.1.0", requestedTag);
            Assert.Equal(MainAssetName, result.AssetName);
            Assert.Equal(PromotedSha256, result.AssetSha256);
        }

        [Fact]
        public async Task CheckAsync_SelectsLegacyChannelBeforeWindows10Version1809()
        {
            GitHubUpdateChecker checker = CreateChecker(
                "v3.0.8",
                new Version(10, 0, 17762),
                BuildTwoChannelManifest(),
                tag => BuildRelease(tag, LegacyAssetName));

            UpdateCheckResult result = await checker.CheckAsync();

            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.Equal("legacy-windows", result.ChannelId);
            Assert.Equal(LegacyAssetName, result.AssetName);
            Assert.Equal("3.1.0", result.IncompatibleNewerVersion);
            Assert.Equal("10.0.17763", result.IncompatibleMinimumWindowsVersion);
            Assert.Equal(
                "https://github.com/qiyi71w/readboard/releases/tag/v3.0.9",
                result.ReleaseUrl);
        }

        [Fact]
        public async Task CheckAsync_RetiredChannelStillOffersItsFinalPromotedVersion()
        {
            string manifest = BuildManifest(
                BuildChannel(
                    "legacy-windows",
                    "retired",
                    null,
                    "10.0.17763",
                    "v3.0.9",
                    LegacyAssetName));
            GitHubUpdateChecker checker = CreateChecker(
                "v3.0.8",
                new Version(6, 1, 7601),
                manifest,
                tag => BuildRelease(tag, LegacyAssetName));

            UpdateCheckResult result = await checker.CheckAsync();

            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.Equal("retired", result.ChannelStatus);
            Assert.Equal("3.0.9", result.LatestVersion);
        }

        [Fact]
        public async Task CheckAsync_DoesNotReportAnIncompatibleChannelUnlessItsVersionIsNewer()
        {
            string manifest = BuildManifest(
                BuildChannel(
                    "legacy-windows",
                    "active",
                    null,
                    "10.0.17763",
                    "v3.0.9",
                    LegacyAssetName),
                BuildChannel(
                    "main",
                    "active",
                    "10.0.17763",
                    null,
                    "v3.0.9",
                    LegacyAssetName));
            GitHubUpdateChecker checker = CreateChecker(
                "v3.0.9",
                new Version(6, 1, 7601),
                manifest,
                tag => BuildRelease(tag, LegacyAssetName));

            UpdateCheckResult result = await checker.CheckAsync();

            Assert.Null(result.IncompatibleNewerVersion);
            Assert.Null(result.IncompatibleMinimumWindowsVersion);
        }

        [Theory]
        [InlineData("v3.0.8", UpdateCheckStatus.UpdateAvailable)]
        [InlineData("v3.0.9", UpdateCheckStatus.UpToDate)]
        [InlineData("v3.0.10", UpdateCheckStatus.OutsideChannel)]
        public async Task CheckAsync_ReturnsDistinctResultForVersionPosition(
            string currentVersion,
            UpdateCheckStatus expectedStatus)
        {
            string manifest = BuildManifest(
                BuildChannel(
                    "legacy-windows",
                    "active",
                    null,
                    "10.0.17763",
                    "v3.0.9",
                    LegacyAssetName));
            GitHubUpdateChecker checker = CreateChecker(
                currentVersion,
                new Version(6, 1, 7601),
                manifest,
                tag => BuildRelease(tag, LegacyAssetName));

            UpdateCheckResult result = await checker.CheckAsync();

            Assert.Equal(expectedStatus, result.Status);
        }

        [Fact]
        public async Task CheckAsync_ReturnsNoMatchingChannelWithoutRequestingARelease()
        {
            bool releaseRequested = false;
            string manifest = BuildManifest(
                BuildChannel(
                    "future",
                    "active",
                    "11.0.0",
                    null,
                    "v4.0.0",
                    "future.zip"));
            GitHubUpdateChecker checker = CreateChecker(
                "v3.0.9",
                new Version(10, 0, 19045),
                manifest,
                tag =>
                {
                    releaseRequested = true;
                    return BuildRelease(tag, "future.zip");
                });

            UpdateCheckResult result = await checker.CheckAsync();

            Assert.Equal(UpdateCheckStatus.NoMatchingChannel, result.Status);
            Assert.False(releaseRequested);
        }

        [Fact]
        public async Task CheckAsync_RejectsOverlappingWindowsRanges()
        {
            string manifest = BuildManifest(
                BuildChannel("legacy", "active", null, "10.0.19000", "v3.0.9", "legacy.zip"),
                BuildChannel("main", "active", "10.0.17763", null, "v3.1.0", "main.zip"));

            UpdateCheckResult result = await CreateChecker(
                "v3.0.9",
                new Version(10, 0, 18000),
                manifest,
                tag => BuildRelease(tag, "main.zip")).CheckAsync();

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.Contains("overlap", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CheckAsync_RejectsUnknownManifestSchema()
        {
            string manifest = "{\"schemaVersion\":2,\"channels\":[]}";

            UpdateCheckResult result = await CreateChecker(
                "v3.0.9",
                new Version(10, 0, 19045),
                manifest,
                tag => BuildRelease(tag, MainAssetName)).CheckAsync();

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.Contains("schema", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CheckAsync_RejectsInvalidChannelStatus()
        {
            string manifest = BuildManifest(
                BuildChannel("main", "paused", null, null, "v3.1.0", MainAssetName));

            UpdateCheckResult result = await CreateChecker(
                "v3.0.9",
                new Version(10, 0, 19045),
                manifest,
                tag => BuildRelease(tag, MainAssetName)).CheckAsync();

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.Contains("status", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("active", "hash")]
        [InlineData("retired", "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
        [InlineData("active", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]
        public async Task CheckAsync_RejectsInvalidSha256ForInstallableChannel(
            string status,
            string sha256)
        {
            string manifest = BuildManifest(
                BuildChannel(
                    "legacy-windows",
                    status,
                    null,
                    "10.0.17763",
                    "v3.0.9",
                    LegacyAssetName,
                    sha256));

            UpdateCheckResult result = await CreateChecker(
                "v3.0.8",
                new Version(6, 1, 7601),
                manifest,
                tag => BuildRelease(tag, LegacyAssetName)).CheckAsync();

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.Contains("SHA-256", result.ErrorMessage);
        }

        [Theory]
        [InlineData("not-windows", "v3.1.0", "Windows version")]
        [InlineData("10.0.17763", "release-3.1", "semantic version")]
        public async Task CheckAsync_RejectsInvalidManifestVersions(
            string minimumWindowsVersion,
            string latestTag,
            string expectedError)
        {
            string manifest = BuildManifest(
                BuildChannel(
                    "main",
                    "active",
                    minimumWindowsVersion,
                    null,
                    latestTag,
                    MainAssetName));

            UpdateCheckResult result = await CreateChecker(
                "v3.0.9",
                new Version(10, 0, 19045),
                manifest,
                tag => BuildRelease(tag, MainAssetName)).CheckAsync();

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.Contains(expectedError, result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CheckAsync_RejectsMissingRequiredChannelField()
        {
            string manifest =
                "{\"schemaVersion\":1,\"channels\":[{" +
                "\"id\":\"main\",\"status\":\"active\"," +
                "\"latestTag\":\"v3.1.0\",\"sha256\":\"" + PromotedSha256 + "\"}]}";

            UpdateCheckResult result = await CreateChecker(
                "v3.0.9",
                new Version(10, 0, 19045),
                manifest,
                tag => BuildRelease(tag, MainAssetName)).CheckAsync();

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.Contains("assetName", result.ErrorMessage);
        }

        [Fact]
        public async Task CheckAsync_ManifestRequestFailureDoesNotRequestAnyRelease()
        {
            bool releaseRequested = false;
            GitHubUpdateChecker checker = new GitHubUpdateChecker(
                () => "v3.0.9",
                () => new Version(10, 0, 19045),
                token => Task.FromException<string>(new Exception("manifest unavailable")),
                (tag, token) =>
                {
                    releaseRequested = true;
                    return Task.FromResult(BuildRelease(tag, MainAssetName));
                });

            UpdateCheckResult result = await checker.CheckAsync();

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.Equal("manifest unavailable", result.ErrorMessage);
            Assert.False(releaseRequested);
        }

        [Fact]
        public async Task CheckAsync_ReleaseRequestFailureReturnsFailureWithoutAnotherSelectionPath()
        {
            int releaseRequestCount = 0;
            GitHubUpdateChecker checker = CreateChecker(
                "v3.0.9",
                new Version(10, 0, 19045),
                BuildTwoChannelManifest(),
                tag =>
                {
                    releaseRequestCount++;
                    throw new Exception("release unavailable");
                });

            UpdateCheckResult result = await checker.CheckAsync();

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.Equal("release unavailable", result.ErrorMessage);
            Assert.Equal(1, releaseRequestCount);
        }

        [Fact]
        public async Task CheckAsync_PropagatesCallerCancellation()
        {
            using var cancellation = new CancellationTokenSource();
            GitHubUpdateChecker checker = new GitHubUpdateChecker(
                () => "v3.0.9",
                () => new Version(10, 0, 19045),
                async token =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return BuildTwoChannelManifest();
                },
                (tag, token) => Task.FromResult(BuildRelease(tag, MainAssetName)));

            Task<UpdateCheckResult> check = checker.CheckAsync(cancellation.Token);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
        }

        [Theory]
        [InlineData("{\"tag_name\":\"v9.9.9\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://example.com\",\"assets\":[]}", "tag")]
        [InlineData("{\"tag_name\":\"v3.1.0\",\"draft\":true,\"prerelease\":false,\"html_url\":\"https://example.com\",\"assets\":[]}", "stable")]
        [InlineData("{\"tag_name\":\"v3.1.0\",\"draft\":false,\"prerelease\":true,\"html_url\":\"https://example.com\",\"assets\":[]}", "stable")]
        [InlineData("{\"tag_name\":\"v3.1.0\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://example.com\",\"assets\":[]}", "asset")]
        public async Task CheckAsync_RejectsReleaseThatDoesNotMatchPromotedChannel(
            string releaseJson,
            string expectedError)
        {
            UpdateCheckResult result = await CreateChecker(
                "v3.0.9",
                new Version(10, 0, 19045),
                BuildTwoChannelManifest(),
                tag => releaseJson).CheckAsync();

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.Contains(expectedError, result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        private static GitHubUpdateChecker CreateChecker(
            string currentVersion,
            Version windowsVersion,
            string manifestJson,
            Func<string, string> releaseJsonProvider)
        {
            return new GitHubUpdateChecker(
                () => currentVersion,
                () => windowsVersion,
                token => Task.FromResult(manifestJson),
                (tag, token) => Task.FromResult(releaseJsonProvider(tag)));
        }

        private static string BuildTwoChannelManifest()
        {
            return BuildManifest(
                BuildChannel(
                    "legacy-windows",
                    "active",
                    null,
                    "10.0.17763",
                    "v3.0.9",
                    LegacyAssetName),
                BuildChannel(
                    "main",
                    "active",
                    "10.0.17763",
                    null,
                    "v3.1.0",
                    MainAssetName));
        }

        private static string BuildManifest(params string[] channels)
        {
            return "{\"schemaVersion\":1,\"channels\":[" +
                string.Join(",", channels) + "]}";
        }

        private static string BuildChannel(
            string id,
            string status,
            string minimumWindowsVersion,
            string maximumWindowsVersionExclusive,
            string latestTag,
            string assetName,
            string sha256 = PromotedSha256)
        {
            var fields = new List<string>
            {
                "\"id\":\"" + id + "\"",
                "\"status\":\"" + status + "\"",
                "\"latestTag\":\"" + latestTag + "\"",
                "\"assetName\":\"" + assetName + "\"",
                "\"sha256\":\"" + sha256 + "\""
            };
            if (minimumWindowsVersion != null)
            {
                fields.Add("\"minimumWindowsVersion\":\"" + minimumWindowsVersion + "\"");
            }
            if (maximumWindowsVersionExclusive != null)
            {
                fields.Add(
                    "\"maximumWindowsVersionExclusive\":\"" +
                    maximumWindowsVersionExclusive + "\"");
            }

            return "{" + string.Join(",", fields) + "}";
        }

        private static string BuildRelease(string tag, string assetName)
        {
            return
                "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":false," +
                "\"name\":\"ReadBoard " + tag + "\",\"body\":\"Release notes.\"," +
                "\"html_url\":\"https://github.com/qiyi71w/readboard/releases/tag/" + tag + "\"," +
                "\"published_at\":\"2026-07-13T10:30:00Z\",\"assets\":[{" +
                "\"name\":\"" + assetName + "\"," +
                "\"browser_download_url\":\"https://github.com/qiyi71w/readboard/releases/download/" +
                tag + "/" + assetName + "\",\"size\":12345}]}";
        }
    }
}
