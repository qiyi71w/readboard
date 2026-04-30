using System;
using System.IO;

namespace readboard
{
    internal static class BoardDebugDiagnosticsPaths
    {
        private const string DirectoryName = "debug-diagnostics";

        public static string GetRootDirectory(string baseDirectory)
        {
            string root = string.IsNullOrWhiteSpace(baseDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : baseDirectory;
            return Path.Combine(root, DirectoryName);
        }
    }
}
