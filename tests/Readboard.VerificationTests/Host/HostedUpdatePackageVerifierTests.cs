using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class HostedUpdatePackageVerifierTests
    {
        private static readonly string[] RequiredEntries =
        {
            "readboard.exe",
            "readboard.dll",
            "readboard.runtimeconfig.json",
            "readboard.deps.json",
            "language_cn.txt"
        };

        [Fact]
        public void Verify_AllowsFlatReleasePackage()
        {
            using (var workspace = new ZipWorkspace("readboard-github-release-v3.0.2.zip"))
            {
                workspace.CreateZip(RequiredEntries);

                new HostedUpdatePackageVerifier().Verify("v3.0.2", workspace.ZipPath);
            }
        }

        [Fact]
        public void Verify_AllowsSingleTopLevelDirectory()
        {
            using (var workspace = new ZipWorkspace("readboard-github-release-v3.0.2.zip"))
            {
                workspace.CreateZip(
                    "readboard-v3.0.2/readboard.exe",
                    "readboard-v3.0.2/readboard.dll",
                    "readboard-v3.0.2/readboard.runtimeconfig.json",
                    "readboard-v3.0.2/readboard.deps.json",
                    "readboard-v3.0.2/language_cn.txt");

                new HostedUpdatePackageVerifier().Verify("v3.0.2", workspace.ZipPath);
            }
        }

        [Fact]
        public void Verify_RejectsParentTraversalEntry()
        {
            using (var workspace = new ZipWorkspace("readboard-github-release-v3.0.2.zip"))
            {
                workspace.CreateZip(
                    "readboard.exe",
                    "readboard.dll",
                    "readboard.runtimeconfig.json",
                    "readboard.deps.json",
                    "language_cn.txt",
                    "../evil.txt");

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => new HostedUpdatePackageVerifier().Verify("v3.0.2", workspace.ZipPath));

                Assert.Contains("..", exception.Message);
            }
        }

        [Theory]
        [InlineData("/evil.txt")]
        [InlineData(@"C:\evil.txt")]
        [InlineData(@"\\server\share\evil.txt")]
        public void Verify_RejectsRootedEntries(string entryName)
        {
            using (var workspace = new ZipWorkspace("readboard-github-release-v3.0.2.zip"))
            {
                var entries = new List<string>(RequiredEntries) { entryName };
                workspace.CreateZip(entries.ToArray());

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => new HostedUpdatePackageVerifier().Verify("v3.0.2", workspace.ZipPath));

                Assert.Contains("unsafe", exception.Message, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void Verify_RejectsMissingRequiredFiles()
        {
            using (var workspace = new ZipWorkspace("readboard-github-release-v3.0.2.zip"))
            {
                workspace.CreateZip(
                    "readboard.dll",
                    "readboard.runtimeconfig.json",
                    "readboard.deps.json",
                    "language_cn.txt");

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => new HostedUpdatePackageVerifier().Verify("v3.0.2", workspace.ZipPath));

                Assert.Contains("readboard.exe", exception.Message);
            }
        }

        [Fact]
        public void Verify_RejectsRequiredFilesNestedBelowInstallRoot()
        {
            using (var workspace = new ZipWorkspace("readboard-github-release-v3.0.2.zip"))
            {
                workspace.CreateZip(
                    "readboard-v3.0.2/bin/readboard.exe",
                    "readboard-v3.0.2/bin/readboard.dll",
                    "readboard-v3.0.2/bin/readboard.runtimeconfig.json",
                    "readboard-v3.0.2/bin/readboard.deps.json",
                    "readboard-v3.0.2/bin/language_cn.txt");

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => new HostedUpdatePackageVerifier().Verify("v3.0.2", workspace.ZipPath));

                Assert.Contains("readboard.exe", exception.Message);
            }
        }

        [Fact]
        public void Verify_RejectsUnexpectedFileName()
        {
            using (var workspace = new ZipWorkspace("readboard-github-release-v9.9.9.zip"))
            {
                workspace.CreateZip(RequiredEntries);

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => new HostedUpdatePackageVerifier().Verify("v3.0.2", workspace.ZipPath));

                Assert.Contains("readboard-github-release-v3.0.2.zip", exception.Message);
            }
        }

        private sealed class ZipWorkspace : IDisposable
        {
            public ZipWorkspace(string fileName)
            {
                RootPath = Path.Combine(
                    Path.GetTempPath(),
                    "readboard-hosted-update-verifier-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(RootPath);
                ZipPath = Path.Combine(RootPath, fileName);
            }

            public string RootPath { get; }

            public string ZipPath { get; }

            public void CreateZip(params string[] entryNames)
            {
                using (FileStream stream = File.Create(ZipPath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    foreach (string entryName in entryNames)
                    {
                        ZipArchiveEntry entry = archive.CreateEntry(entryName);
                        using (StreamWriter writer = new StreamWriter(entry.Open()))
                        {
                            writer.Write("content");
                        }
                    }
                }
            }

            public void Dispose()
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, true);
                }
            }
        }
    }
}
