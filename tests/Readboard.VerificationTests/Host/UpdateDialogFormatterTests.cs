using System;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Host
{
    public sealed class UpdateDialogFormatterTests
    {
        [Theory]
        [InlineData("2.0.2", "v2.0.2")]
        [InlineData("v2.0.1", "v2.0.1")]
        [InlineData("2.0.1-beta.1", "v2.0.1-beta.1")]
        [InlineData("release candidate", "release candidate")]
        public void FormatVersion_NormalizesSemanticDisplay(string rawValue, string expected)
        {
            Assert.Equal(expected, UpdateDialogFormatter.FormatVersion(rawValue));
        }

        [Fact]
        public void FormatReleaseDate_UsesUtcTimestampAndLabel()
        {
            DateTime publishedAt = new DateTime(2026, 4, 20, 23, 42, 0, DateTimeKind.Utc);

            string formatted = UpdateDialogFormatter.FormatReleaseDate(publishedAt);

            Assert.Equal("2026-04-20 23:42 UTC", formatted);
        }

        [Fact]
        public void FormatReleaseNotes_ConvertsGitHubMarkdownToReadablePlainText()
        {
            string markdown = string.Join(
                "\n",
                "## What's Changed",
                "",
                "* Add current move number support",
                "* Improve wait behavior",
                "",
                "**Full Changelog**: [v2.0.0...v2.0.1](https://github.com/qiyi71w/readboard/compare/v2.0.0...v2.0.1)");

            string formatted = UpdateDialogFormatter.FormatReleaseNotes(markdown);

            Assert.Equal(
                string.Join(
                    Environment.NewLine,
                    "What's Changed",
                    string.Empty,
                    "- Add current move number support",
                    "- Improve wait behavior",
                    string.Empty,
                    "Full Changelog: v2.0.0...v2.0.1 (https://github.com/qiyi71w/readboard/compare/v2.0.0...v2.0.1)"),
                formatted);
        }

        [Fact]
        public void FormatReleaseNotes_ReturnsNullForWhitespaceOnlyInput()
        {
            Assert.Null(UpdateDialogFormatter.FormatReleaseNotes("  \r\n  "));
        }
    }
}
