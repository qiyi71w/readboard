using System;
using Microsoft.Web.WebView2.Core;

namespace readboard
{
    internal enum WebViewRuntimeAvailability
    {
        Available,
        Missing,
        Outdated
    }

    internal sealed class WebViewRuntimeProbeResult
    {
        private WebViewRuntimeProbeResult(
            WebViewRuntimeAvailability availability,
            string availableVersion)
        {
            Availability = availability;
            AvailableVersion = availableVersion;
        }

        public WebViewRuntimeAvailability Availability { get; private set; }
        public string AvailableVersion { get; private set; }

        public static WebViewRuntimeProbeResult Available(string availableVersion)
        {
            return new WebViewRuntimeProbeResult(
                WebViewRuntimeAvailability.Available,
                availableVersion);
        }

        public static WebViewRuntimeProbeResult Missing()
        {
            return new WebViewRuntimeProbeResult(WebViewRuntimeAvailability.Missing, null);
        }

        public static WebViewRuntimeProbeResult Outdated(string availableVersion)
        {
            return new WebViewRuntimeProbeResult(
                WebViewRuntimeAvailability.Outdated,
                availableVersion);
        }
    }

    internal static class WebViewRuntimeRequirement
    {
        // ICoreWebView2Settings9 / non-client region support. Raise only when a newer unguarded WebView2 API is adopted.
        public const string MinimumVersion = "123.0.2420.47";

        internal static WebViewRuntimeProbeResult ProbeInstalled()
        {
            return Probe(
                CoreWebView2Environment.GetAvailableBrowserVersionString,
                CoreWebView2Environment.CompareBrowserVersions);
        }

        internal static WebViewRuntimeProbeResult Probe(
            Func<string> getAvailableVersion,
            Func<string, string, int> compareVersions)
        {
            if (getAvailableVersion == null)
                throw new ArgumentNullException(nameof(getAvailableVersion));

            string availableVersion;
            try
            {
                availableVersion = getAvailableVersion();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                return WebViewRuntimeProbeResult.Missing();
            }

            return Evaluate(availableVersion, compareVersions);
        }

        internal static WebViewRuntimeProbeResult Evaluate(
            string availableVersion,
            Func<string, string, int> compareVersions)
        {
            if (compareVersions == null)
                throw new ArgumentNullException(nameof(compareVersions));

            if (string.IsNullOrWhiteSpace(availableVersion))
                return WebViewRuntimeProbeResult.Missing();

            try
            {
                if (compareVersions(availableVersion, MinimumVersion) >= 0)
                    return WebViewRuntimeProbeResult.Available(availableVersion);
                return WebViewRuntimeProbeResult.Outdated(availableVersion);
            }
            catch (Exception)
            {
                return WebViewRuntimeProbeResult.Outdated(availableVersion);
            }
        }

        internal static SemanticMessage CreateOutdatedMessage(string availableVersion)
        {
            return SemanticMessage.Create(
                "WebViewRuntime_outdatedMessage",
                availableVersion,
                MinimumVersion);
        }
    }
}
