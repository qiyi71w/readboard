using System;
using System.Reflection;

namespace readboard
{
    internal static class AppReleaseVersion
    {
        public static string GetCurrentVersion()
        {
            Assembly assembly = typeof(AppReleaseVersion).Assembly;
            string version = NormalizeVersionValue(ReadInformationalVersion(assembly));
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }

            version = NormalizeVersionValue(ReadFileVersion(assembly));
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }

            Version assemblyVersion = assembly.GetName().Version;
            if (assemblyVersion != null)
            {
                return FormatVersion(assemblyVersion);
            }

            throw new InvalidOperationException("Current assembly version is unavailable.");
        }

        private static string ReadInformationalVersion(Assembly assembly)
        {
            var attribute =
                (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    assembly,
                    typeof(AssemblyInformationalVersionAttribute));
            return attribute == null ? null : attribute.InformationalVersion;
        }

        private static string ReadFileVersion(Assembly assembly)
        {
            var attribute =
                (AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(
                    assembly,
                    typeof(AssemblyFileVersionAttribute));
            return attribute == null ? null : attribute.Version;
        }

        private static string NormalizeVersionValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string trimmedValue = value.Trim();
            if (trimmedValue.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                trimmedValue = trimmedValue.Substring(1);
            }

            Version parsedVersion;
            return TryCreateVersion(trimmedValue, out parsedVersion)
                ? FormatVersion(parsedVersion)
                : null;
        }

        private static bool TryCreateVersion(string value, out Version version)
        {
            version = null;
            try
            {
                version = new Version(value);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string FormatVersion(Version version)
        {
            int patch = version.Build < 0 ? 0 : version.Build;
            return string.Format("{0}.{1}.{2}", version.Major, version.Minor, patch);
        }
    }
}
