using System;

namespace readboard
{
    internal static class SupportedWindowsVersion
    {
        internal static readonly Version Minimum = new Version(10, 0, 17763);

        internal static bool IsSupported(Version windowsVersion)
        {
            return windowsVersion != null && windowsVersion.CompareTo(Minimum) >= 0;
        }

        internal static bool EnsureSupported(
            Func<Version> windowsVersionProvider,
            Action showUnsupportedPrompt)
        {
            if (windowsVersionProvider == null)
                throw new ArgumentNullException("windowsVersionProvider");
            if (showUnsupportedPrompt == null)
                throw new ArgumentNullException("showUnsupportedPrompt");

            if (IsSupported(windowsVersionProvider()))
                return true;

            showUnsupportedPrompt();
            return false;
        }
    }
}
