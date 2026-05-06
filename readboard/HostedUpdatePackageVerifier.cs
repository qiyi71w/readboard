using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace readboard
{
    internal sealed class HostedUpdatePackageVerifier
    {
        private static readonly string[] RequiredFiles =
        {
            "readboard.exe",
            "readboard.dll",
            "readboard.runtimeconfig.json",
            "readboard.deps.json",
            "language_cn.txt"
        };

        public void Verify(string versionTag, string zipPath)
        {
            if (string.IsNullOrWhiteSpace(versionTag))
            {
                throw new ArgumentException("Version tag is required.", nameof(versionTag));
            }

            if (string.IsNullOrWhiteSpace(zipPath))
            {
                throw new ArgumentException("Zip path is required.", nameof(zipPath));
            }

            string expectedFileName = "readboard-github-release-" + versionTag + ".zip";
            string actualFileName = Path.GetFileName(zipPath);
            if (!string.Equals(actualFileName, expectedFileName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Hosted update package file name must be " + expectedFileName + ".");
            }

            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                VerifyArchiveEntries(archive);
            }
        }

        private static void VerifyArchiveEntries(ZipArchive archive)
        {
            var fileEntries = new List<string>();
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string normalizedEntryName = NormalizeAndValidateEntryName(entry.FullName);
                if (normalizedEntryName.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                fileEntries.Add(normalizedEntryName);
            }

            if (fileEntries.Count == 0)
            {
                throw new InvalidOperationException("Hosted update package contains no files.");
            }

            bool useZipRoot = fileEntries.All(entry => entry.IndexOf('/') < 0);
            string commonTopLevelDirectory = null;
            if (!useZipRoot)
            {
                commonTopLevelDirectory = FindCommonTopLevelDirectory(fileEntries);
                if (commonTopLevelDirectory == null)
                {
                    throw new InvalidOperationException(
                        "Hosted update package files must be at the zip root or under one common top-level directory.");
                }
            }

            var presentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string entryName in fileEntries)
            {
                string relativeEntryName = useZipRoot
                    ? entryName
                    : entryName.Substring(commonTopLevelDirectory.Length + 1);
                if (relativeEntryName.IndexOf('/') >= 0)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(relativeEntryName))
                {
                    presentFiles.Add(relativeEntryName);
                }
            }

            string[] missingFiles = RequiredFiles
                .Where(requiredFile => !presentFiles.Contains(requiredFile))
                .ToArray();
            if (missingFiles.Length > 0)
            {
                throw new InvalidOperationException(
                    "Hosted update package is missing required files: " +
                    string.Join(", ", missingFiles) + ".");
            }
        }

        private static string FindCommonTopLevelDirectory(IReadOnlyList<string> fileEntries)
        {
            string commonTopLevelDirectory = null;
            foreach (string entryName in fileEntries)
            {
                int separatorIndex = entryName.IndexOf('/');
                if (separatorIndex <= 0)
                {
                    return null;
                }

                string topLevelDirectory = entryName.Substring(0, separatorIndex);
                if (commonTopLevelDirectory == null)
                {
                    commonTopLevelDirectory = topLevelDirectory;
                    continue;
                }

                if (!string.Equals(
                    commonTopLevelDirectory,
                    topLevelDirectory,
                    StringComparison.Ordinal))
                {
                    return null;
                }
            }

            return commonTopLevelDirectory;
        }

        private static string NormalizeAndValidateEntryName(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                throw new InvalidOperationException("Hosted update package contains an empty entry name.");
            }

            if (IsRootedEntry(entryName))
            {
                throw new InvalidOperationException(
                    "Hosted update package contains an unsafe rooted entry: " + entryName);
            }

            string normalizedEntryName = entryName.Replace('\\', '/');
            string[] segments = normalizedEntryName.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string segment in segments)
            {
                if (segment == "..")
                {
                    throw new InvalidOperationException(
                        "Hosted update package contains an unsafe path traversal entry: " + entryName);
                }
            }

            return normalizedEntryName;
        }

        private static bool IsRootedEntry(string entryName)
        {
            if (entryName.StartsWith("/", StringComparison.Ordinal) ||
                entryName.StartsWith("\\", StringComparison.Ordinal) ||
                entryName.StartsWith("//", StringComparison.Ordinal) ||
                entryName.StartsWith("\\\\", StringComparison.Ordinal))
            {
                return true;
            }

            return entryName.Length >= 2 &&
                char.IsLetter(entryName[0]) &&
                entryName[1] == ':';
        }
    }
}
