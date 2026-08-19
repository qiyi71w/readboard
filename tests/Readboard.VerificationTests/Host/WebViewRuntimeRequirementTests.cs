using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class WebViewRuntimeRequirementTests
    {
        [Fact]
        public void Requirement_IsIndependentOfSdkFullCompatVersion()
        {
            Assert.Equal("123.0.2420.47", WebViewRuntimeRequirement.MinimumVersion);

            string requirement = File.ReadAllText(Path.Combine(
                VerificationFixtureLocator.RepositoryRoot(),
                "readboard",
                "WebViewRuntimeRequirement.cs"));
            Assert.Contains("123.0.2420.47", requirement);
            Assert.DoesNotContain("150.0.4078.44", requirement);
            Assert.Contains("CoreWebView2Environment.GetAvailableBrowserVersionString", requirement);
            Assert.Contains("CoreWebView2Environment.CompareBrowserVersions", requirement);
            Assert.Contains("catch (WebView2RuntimeNotFoundException)", requirement);
        }

        [Fact]
        public void Startup_ProbesWebViewRuntimeRequirementBeforeHandshake()
        {
            string program = File.ReadAllText(Path.Combine(
                VerificationFixtureLocator.RepositoryRoot(),
                "readboard",
                "Program.cs"));
            int probe = program.IndexOf("EnsureWebViewRuntimeAvailable", StringComparison.Ordinal);
            int handshake = program.IndexOf("StartupProtocolHandshake.Run", StringComparison.Ordinal);
            int run = program.IndexOf("Application.Run(mainForm)", StringComparison.Ordinal);

            Assert.True(probe >= 0);
            Assert.True(handshake > probe);
            Assert.True(run > handshake);
        }

        [Fact]
        public void Probe_MissingRuntime_DoesNotTreatEmptyVersionAsAvailable()
        {
            WebViewRuntimeProbeResult missing = WebViewRuntimeRequirement.Probe(
                delegate { return null; },
                CompareIgnoringChannelSuffix);
            WebViewRuntimeProbeResult blank = WebViewRuntimeRequirement.Evaluate(
                "   ",
                CompareIgnoringChannelSuffix);

            Assert.Equal(WebViewRuntimeAvailability.Missing, missing.Availability);
            Assert.Null(missing.AvailableVersion);
            Assert.Equal(WebViewRuntimeAvailability.Missing, blank.Availability);
        }

        [Theory]
        [InlineData("122.0.2365.92")]
        [InlineData("123.0.2420.46")]
        [InlineData("120.0.2210.61 beta")]
        [InlineData("122.0.2365.92 canary")]
        public void Evaluate_OutdatedRuntime_KeepsVerbatimVersion(string availableVersion)
        {
            WebViewRuntimeProbeResult result = WebViewRuntimeRequirement.Evaluate(
                availableVersion,
                CompareIgnoringChannelSuffix);

            Assert.Equal(WebViewRuntimeAvailability.Outdated, result.Availability);
            Assert.Equal(availableVersion, result.AvailableVersion);
        }

        [Theory]
        [InlineData("123.0.2420.47")]
        [InlineData("123.0.2420.48")]
        [InlineData("124.0.2478.51")]
        [InlineData("123.0.2420.47 beta")]
        [InlineData("131.0.2903.112 stable")]
        public void Evaluate_MeetsRequirement_IsAvailable(string availableVersion)
        {
            WebViewRuntimeProbeResult result = WebViewRuntimeRequirement.Evaluate(
                availableVersion,
                CompareIgnoringChannelSuffix);

            Assert.Equal(WebViewRuntimeAvailability.Available, result.Availability);
            Assert.Equal(availableVersion, result.AvailableVersion);
        }

        [Fact]
        public void Evaluate_UncomparableVersion_IsOutdated()
        {
            WebViewRuntimeProbeResult result = WebViewRuntimeRequirement.Evaluate(
                "not-a-version",
                delegate { throw new ArgumentException("Unable to compare browser versions."); });

            Assert.Equal(WebViewRuntimeAvailability.Outdated, result.Availability);
            Assert.Equal("not-a-version", result.AvailableVersion);
        }

        [Fact]
        public void OutdatedMessage_UsesVersionFormatParametersNotComText()
        {
            SemanticMessage message = WebViewRuntimeRequirement.CreateOutdatedMessage(
                "120.0.2210.61 beta");

            Assert.Equal("WebViewRuntime_outdatedMessage", message.Key);
            Assert.Equal(new object[] { "120.0.2210.61 beta", "123.0.2420.47" }, message.Arguments);
            Assert.Null(message.DiagnosticDetail);

            string resolved = SemanticMessageResolver.Resolve(
                message,
                delegate { return "The current WebView Runtime is {0}. ReadBoard requires WebView Runtime Requirement {1}."; },
                delegate { return "当前 WebView Runtime 为 {0}。ReadBoard 的 WebView Runtime Requirement 为 {1}。"; });

            Assert.Equal(
                "The current WebView Runtime is 120.0.2210.61 beta. ReadBoard requires WebView Runtime Requirement 123.0.2420.47.",
                resolved);
            Assert.DoesNotContain("ICoreWebView2Settings9", resolved);
            Assert.DoesNotContain("Unable to cast COM object", resolved);
        }

        [Fact]
        public void OutdatedRuntimePrompt_RetryAndDownloadActionsReprobeUntilRequirementIsMet()
        {
            Queue<WebViewRuntimeProbeResult> probes = new Queue<WebViewRuntimeProbeResult>(
                new[]
                {
                    WebViewRuntimeProbeResult.Outdated("120.0.2210.61 beta"),
                    WebViewRuntimeProbeResult.Outdated("120.0.2210.61 beta"),
                    WebViewRuntimeProbeResult.Available("123.0.2420.47")
                });
            Queue<MainForm.WebViewRuntimePromptChoice> choices =
                new Queue<MainForm.WebViewRuntimePromptChoice>(new[]
                {
                    MainForm.WebViewRuntimePromptChoice.OpenDownload,
                    MainForm.WebViewRuntimePromptChoice.Retry
                });
            int openDownloadCount = 0;
            int exitCount = 0;
            List<WebViewRuntimeProbeResult> prompted = new List<WebViewRuntimeProbeResult>();

            bool available = MainForm.ResolveWebViewRuntimeAvailability(
                probes.Dequeue,
                delegate(WebViewRuntimeProbeResult result)
                {
                    prompted.Add(result);
                    return choices.Dequeue();
                },
                delegate { openDownloadCount++; },
                delegate { exitCount++; });

            Assert.True(available);
            Assert.Equal(1, openDownloadCount);
            Assert.Equal(0, exitCount);
            Assert.Empty(probes);
            Assert.Empty(choices);
            Assert.Equal(2, prompted.Count);
            Assert.All(prompted, result =>
            {
                Assert.Equal(WebViewRuntimeAvailability.Outdated, result.Availability);
                Assert.Equal("120.0.2210.61 beta", result.AvailableVersion);
            });
        }

        [Fact]
        public void OutdatedRuntimePrompt_ExitStopsStartupWithoutWebViewInit()
        {
            int probeCount = 0;
            int exitCount = 0;
            int webViewInitCount = 0;

            bool available = MainForm.ResolveWebViewRuntimeAvailability(
                delegate
                {
                    probeCount++;
                    return WebViewRuntimeProbeResult.Outdated("90.0.818.0");
                },
                delegate { return MainForm.WebViewRuntimePromptChoice.Exit; },
                delegate { },
                delegate { exitCount++; });

            Assert.False(available);
            Assert.Equal(1, probeCount);
            Assert.Equal(1, exitCount);
            Assert.Equal(0, webViewInitCount);
        }

        [Fact]
        public void AvailableRuntime_SkipsPromptAndContinuesStartup()
        {
            int promptCount = 0;
            int openDownloadCount = 0;
            int exitCount = 0;

            bool available = MainForm.ResolveWebViewRuntimeAvailability(
                delegate { return WebViewRuntimeProbeResult.Available("131.0.2903.112"); },
                delegate
                {
                    promptCount++;
                    return MainForm.WebViewRuntimePromptChoice.Exit;
                },
                delegate { openDownloadCount++; },
                delegate { exitCount++; });

            Assert.True(available);
            Assert.Equal(0, promptCount);
            Assert.Equal(0, openDownloadCount);
            Assert.Equal(0, exitCount);
        }

        [Fact]
        public void TryEnableNonClientRegionSupport_SwallowsInterfaceConversion()
        {
            InvalidCastException conversion = new InvalidCastException(
                "Unable to cast COM object of type 'System.__ComObject' to interface type 'Microsoft.Web.WebView2.Core.Raw.ICoreWebView2Settings9'.");
            COMException noInterface = new COMException(
                "ICoreWebView2Settings9 is not available.",
                unchecked((int)0x80004002));
            InvalidOperationException wrapped = new InvalidOperationException(
                "settings.IsNonClientRegionSupportEnabled is not available.",
                conversion);

            Assert.True(MainForm.IsWebViewInterfaceConversionException(conversion));
            Assert.True(MainForm.IsWebViewInterfaceConversionException(noInterface));
            Assert.True(MainForm.IsWebViewInterfaceConversionException(wrapped));
            Assert.False(MainForm.TryEnableNonClientRegionSupport(delegate { throw conversion; }));
            Assert.False(MainForm.TryEnableNonClientRegionSupport(delegate { throw noInterface; }));
            Assert.False(MainForm.TryEnableNonClientRegionSupport(delegate { throw wrapped; }));
        }

        [Fact]
        public void TryEnableNonClientRegionSupport_RethrowsOtherFailures()
        {
            FileNotFoundException missingPage = new FileNotFoundException("missing page");

            Assert.False(MainForm.IsWebViewInterfaceConversionException(missingPage));
            Assert.False(MainForm.IsWebViewInterfaceConversionException(
                new InvalidOperationException("CoreWebView2 is not initialized.")));
            Assert.Throws<FileNotFoundException>(delegate
            {
                MainForm.TryEnableNonClientRegionSupport(delegate { throw missingPage; });
            });
            Assert.True(MainForm.TryEnableNonClientRegionSupport(delegate { }));
        }

        [Fact]
        public void OutdatedAndMissingHeadings_AreIndependentInDefaultsAndLanguages()
        {
            string root = VerificationFixtureLocator.RepositoryRoot();
            string defaults = File.ReadAllText(Path.Combine(root, "readboard", "Program.cs"));
            string missingHeading = ReadDefaultLanguageValue(defaults, "WebViewRuntime_heading");
            string outdatedHeading = ReadDefaultLanguageValue(defaults, "WebViewRuntime_outdatedHeading");
            string missingMessage = ReadDefaultLanguageValue(defaults, "WebViewRuntime_message");
            string outdatedMessage = ReadDefaultLanguageValue(defaults, "WebViewRuntime_outdatedMessage");

            Assert.NotEqual(missingHeading, outdatedHeading);
            Assert.NotEqual(missingMessage, outdatedMessage);
            Assert.Contains("{0}", outdatedMessage);
            Assert.Contains("{1}", outdatedMessage);
            Assert.DoesNotContain("ICoreWebView2Settings9", outdatedMessage);

            foreach (string language in new[] { "cn", "en", "jp", "kr" })
            {
                string languageMissingHeading = ReadLanguageValue(root, language, "WebViewRuntime_heading");
                string languageOutdatedHeading = ReadLanguageValue(root, language, "WebViewRuntime_outdatedHeading");
                string languageMissingMessage = ReadLanguageValue(root, language, "WebViewRuntime_message");
                string languageOutdatedMessage = ReadLanguageValue(root, language, "WebViewRuntime_outdatedMessage");

                Assert.NotEqual(languageMissingHeading, languageOutdatedHeading);
                Assert.NotEqual(languageMissingMessage, languageOutdatedMessage);
                Assert.Contains("{0}", languageOutdatedMessage);
                Assert.Contains("{1}", languageOutdatedMessage);
            }
        }

        private static int CompareIgnoringChannelSuffix(string version1, string version2)
        {
            return ParseBrowserVersion(version1).CompareTo(ParseBrowserVersion(version2));
        }

        private static Version ParseBrowserVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("A browser version is required.", nameof(version));

            string numeric = version.Trim();
            int separator = numeric.IndexOf(' ');
            if (separator >= 0)
                numeric = numeric.Substring(0, separator);
            return new Version(numeric);
        }

        private static string ReadLanguageValue(string root, string language, string key)
        {
            string[] lines = File.ReadAllLines(Path.Combine(
                root,
                "readboard",
                "language_" + language + ".txt"));
            foreach (string line in lines)
            {
                if (line.StartsWith(key + "=", StringComparison.Ordinal))
                    return line.Substring(key.Length + 1);
            }

            throw new InvalidOperationException("Missing language value for " + key);
        }

        private static string ReadDefaultLanguageValue(string source, string key)
        {
            string token = "langItems[\"" + key + "\"] = \"";
            int start = source.IndexOf(token, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Missing default language value for " + key);
            start += token.Length;
            int end = source.IndexOf('"', start);
            return source.Substring(start, end - start);
        }
    }
}
