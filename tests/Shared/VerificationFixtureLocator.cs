using System;
using System.IO;

namespace Readboard.VerificationTests
{
    internal static class VerificationFixtureLocator
    {
        private const string SolutionFileName = "readboard.sln";

        public static string RepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, SolutionFileName);
                if (File.Exists(candidate))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        public static string FixturePath(string relativePath)
        {
            return Path.Combine(RepositoryRoot(), "fixtures", relativePath);
        }
    }
}
