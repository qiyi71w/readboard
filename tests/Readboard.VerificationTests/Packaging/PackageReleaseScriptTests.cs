using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace Readboard.VerificationTests
{
    public sealed class PackageReleaseScriptTests
    {
        [Fact]
        public void SkipBuild_FailsWhenBuildOutputDirectoryDoesNotExist()
        {
            using (PackagingWorkspace workspace = PackagingWorkspace.Create())
            {
                Directory.Delete(workspace.BuildOutputDir, recursive: true);

                PackagingResult result = workspace.RunPackagingScript();

                Assert.NotEqual(0, result.ExitCode);
                Assert.Contains("发布输出目录", result.Output);
            }
        }

        [Fact]
        public void SkipBuild_FailsWhenRequiredBuildFilesAreMissing()
        {
            using (PackagingWorkspace workspace = PackagingWorkspace.Create())
            {
                workspace.WriteFile("readboard.exe");

                PackagingResult result = workspace.RunPackagingScript();

                Assert.NotEqual(0, result.ExitCode);
                Assert.Contains("readboard.dll", result.Output);
            }
        }

        [Fact]
        public void SkipBuild_DoesNotSeedLegacyOtherConfigIntoReleasePackage()
        {
            using (PackagingWorkspace workspace = PackagingWorkspace.Create())
            {
                workspace.CreateBuildOutputs();

                PackagingResult result = workspace.RunPackagingScript();

                Assert.True(result.ExitCode == 0, result.Output);
                string releaseDirectory = Assert.Single(Directory.GetDirectories(workspace.ReleaseRoot));
                Assert.False(File.Exists(Path.Combine(releaseDirectory, "config_readboard_others.txt")));
                Assert.Single(Directory.GetFiles(workspace.ReleaseRoot, "*.zip"));
            }
        }

        [Fact]
        public void SkipBuild_SkipZip_ProducesReleaseDirectoryWithoutZipArtifact()
        {
            using (PackagingWorkspace workspace = PackagingWorkspace.Create())
            {
                workspace.CreateBuildOutputs();
                File.WriteAllText(Path.Combine(workspace.ReleaseRoot, workspace.ExpectedZipFileName), "stale zip");

                PackagingResult result = workspace.RunPackagingScript(skipZip: true);

                Assert.True(result.ExitCode == 0, result.Output);
                string releaseDirectory = Assert.Single(Directory.GetDirectories(workspace.ReleaseRoot));
                Assert.True(File.Exists(Path.Combine(releaseDirectory, "readboard.exe")));
                Assert.DoesNotContain(".zip", result.Output, StringComparison.OrdinalIgnoreCase);
                Assert.Empty(Directory.GetFiles(workspace.ReleaseRoot, "*.zip"));
            }
        }

        [Fact]
        public void SkipBuild_SkipZip_RefreshesReleaseExeTimestampWithinPackagingWindow()
        {
            using (PackagingWorkspace workspace = PackagingWorkspace.Create())
            {
                workspace.CreateBuildOutputs();
                workspace.SetBuildOutputTimestamp("readboard.exe", new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));

                PackagingResult result = workspace.RunPackagingScript(skipZip: true);

                Assert.True(result.ExitCode == 0, result.Output);
                string releaseDirectory = Assert.Single(Directory.GetDirectories(workspace.ReleaseRoot));
                string releaseExePath = Path.Combine(releaseDirectory, "readboard.exe");
                DateTime releaseExeTimestampUtc = File.GetLastWriteTimeUtc(releaseExePath);
                Assert.InRange(
                    releaseExeTimestampUtc,
                    result.StartedAtUtc,
                    result.FinishedAtUtc);
            }
        }

        [Fact]
        public void SkipBuild_ReleaseDoesNotContainRemovedLegacyFiles()
        {
            using (PackagingWorkspace workspace = PackagingWorkspace.Create())
            {
                workspace.CreateBuildOutputs();

                PackagingResult result = workspace.RunPackagingScript(skipZip: true);

                Assert.True(result.ExitCode == 0, result.Output);
                string releaseDirectory = Assert.Single(Directory.GetDirectories(workspace.ReleaseRoot));
                Assert.False(File.Exists(Path.Combine(releaseDirectory, "lw.dll")));
                Assert.False(File.Exists(Path.Combine(releaseDirectory, "Interop.lw.dll")));
                Assert.False(File.Exists(Path.Combine(releaseDirectory, "MouseKeyboardActivityMonitor.dll")));
                Assert.False(File.Exists(Path.Combine(releaseDirectory, "readboard.exe.config")));
            }
        }

        private sealed class PackagingWorkspace : IDisposable
        {
            private PackagingWorkspace(string rootPath)
            {
                RootPath = rootPath;
                BuildOutputDir = Path.Combine(rootPath, "build");
                ReleaseRoot = Path.Combine(rootPath, "release");
                Directory.CreateDirectory(BuildOutputDir);
                Directory.CreateDirectory(ReleaseRoot);
            }

            public string RootPath { get; private set; }
            public string BuildOutputDir { get; private set; }
            public string ReleaseRoot { get; private set; }
            public string ExpectedZipFileName
            {
                get
                {
                    string assemblyInfoPath = Path.Combine(
                        VerificationFixtureLocator.RepositoryRoot(),
                        "readboard",
                        "Properties",
                        "AssemblyInfo.cs");
                    string content = File.ReadAllText(assemblyInfoPath);
                    string token = "AssemblyInformationalVersion(\"";
                    int startIndex = content.IndexOf(token, StringComparison.Ordinal);
                    Assert.True(startIndex >= 0, "Expected AssemblyInformationalVersion in AssemblyInfo.cs.");
                    startIndex += token.Length;
                    int endIndex = content.IndexOf('"', startIndex);
                    Assert.True(endIndex > startIndex, "Expected closing quote for AssemblyInformationalVersion.");
                    string version = content.Substring(startIndex, endIndex - startIndex);
                    return "readboard-github-release-" + version + ".zip";
                }
            }

            public static PackagingWorkspace Create()
            {
                string rootPath = Path.Combine(
                    Path.GetTempPath(),
                    "readboard-package-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);
                return new PackagingWorkspace(rootPath);
            }

            public void CreateBuildOutputs()
            {
                WriteFile("readboard.exe");
                WriteFile("readboard.dll");
                WriteFile("readboard.runtimeconfig.json");
            }

            public void SetBuildOutputTimestamp(string relativePath, DateTime timestampUtc)
            {
                string path = Path.Combine(BuildOutputDir, relativePath);
                Assert.True(File.Exists(path), "Expected build output file before setting timestamp: " + relativePath);
                File.SetLastWriteTimeUtc(path, timestampUtc);
            }

            public PackagingResult RunPackagingScript(bool skipZip = false)
            {
                string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
                string scriptPath = Path.Combine(repositoryRoot, "scripts", "package-readboard-release.local.ps1");
                ProcessStartInfo startInfo = new ProcessStartInfo("pwsh.exe")
                {
                    WorkingDirectory = repositoryRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-File");
                startInfo.ArgumentList.Add(scriptPath);
                startInfo.ArgumentList.Add("-SkipBuild");
                startInfo.ArgumentList.Add("-BuildOutputDir");
                startInfo.ArgumentList.Add(BuildOutputDir);
                startInfo.ArgumentList.Add("-ReleaseRoot");
                startInfo.ArgumentList.Add(ReleaseRoot);
                if (skipZip)
                    startInfo.ArgumentList.Add("-SkipZip");

                using (Process process = Process.Start(startInfo))
                {
                    DateTime startedAtUtc = DateTime.UtcNow;
                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    return new PackagingResult(process.ExitCode, output, startedAtUtc, DateTime.UtcNow);
                }
            }

            public void WriteFile(string relativePath)
            {
                string path = Path.Combine(BuildOutputDir, relativePath);
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(path, relativePath);
            }

            public void Dispose()
            {
                if (Directory.Exists(RootPath))
                    Directory.Delete(RootPath, recursive: true);
            }
        }

        private sealed class PackagingResult
        {
            public PackagingResult(int exitCode, string output, DateTime startedAtUtc, DateTime finishedAtUtc)
            {
                ExitCode = exitCode;
                Output = output ?? string.Empty;
                StartedAtUtc = startedAtUtc;
                FinishedAtUtc = finishedAtUtc;
            }

            public int ExitCode { get; private set; }
            public string Output { get; private set; }
            public DateTime StartedAtUtc { get; private set; }
            public DateTime FinishedAtUtc { get; private set; }
        }
    }
}
