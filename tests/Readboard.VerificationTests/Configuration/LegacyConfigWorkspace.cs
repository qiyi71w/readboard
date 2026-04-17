using System;
using System.IO;

namespace Readboard.VerificationTests
{
    internal sealed class LegacyConfigWorkspace : IDisposable
    {
        private const string MainFileName = "config_readboard.txt";
        private const string OtherFileName = "config_readboard_others.txt";

        private LegacyConfigWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; private set; }

        public static LegacyConfigWorkspace Create()
        {
            string rootPath = Path.Combine(
                Path.GetTempPath(),
                "readboard-verification-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return new LegacyConfigWorkspace(rootPath);
        }

        public void CopyLegacyFixtures()
        {
            File.Copy(
                VerificationFixtureLocator.FixturePath(Path.Combine("config", "legacy", MainFileName)),
                PathFor(MainFileName));

            File.Copy(
                VerificationFixtureLocator.FixturePath(Path.Combine("config", "legacy", OtherFileName)),
                PathFor(OtherFileName));
        }

        public string PathFor(string fileName)
        {
            return Path.Combine(RootPath, fileName);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }
}
