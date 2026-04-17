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
        public void SkipBuild_FailsWhenNativeRuntimeFilesAreMissing()
        {
            using (PackagingWorkspace workspace = PackagingWorkspace.Create())
            {
                workspace.CreateManagedBuildOutputs();

                PackagingResult result = workspace.RunPackagingScript();

                Assert.NotEqual(0, result.ExitCode);
                Assert.Contains("OpenCvSharpExtern.dll", result.Output);
            }
        }

        [Fact]
        public void SkipBuild_DoesNotSeedLegacyOtherConfigIntoReleasePackage()
        {
            using (PackagingWorkspace workspace = PackagingWorkspace.Create())
            {
                workspace.CreateManagedBuildOutputs();
                workspace.CreateNativeBuildOutputs();

                PackagingResult result = workspace.RunPackagingScript();

                Assert.Equal(0, result.ExitCode);
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
                workspace.CreateManagedBuildOutputs();
                workspace.CreateNativeBuildOutputs();
                File.WriteAllText(Path.Combine(workspace.ReleaseRoot, workspace.ExpectedZipFileName), "stale zip");

                PackagingResult result = workspace.RunPackagingScript(skipZip: true);

                Assert.Equal(0, result.ExitCode);
                string releaseDirectory = Assert.Single(Directory.GetDirectories(workspace.ReleaseRoot));
                Assert.True(File.Exists(Path.Combine(releaseDirectory, "readboard.exe")));
                Assert.DoesNotContain(".zip", result.Output, StringComparison.OrdinalIgnoreCase);
                Assert.Empty(Directory.GetFiles(workspace.ReleaseRoot, "*.zip"));
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

            public void CreateManagedBuildOutputs()
            {
                WriteFile("readboard.exe");
                WriteFile("readboard.exe.config");
                WriteFile("MouseKeyboardActivityMonitor.dll");
            }

            public void CreateNativeBuildOutputs()
            {
                WriteFile(Path.Combine("dll", "x86", "OpenCvSharpExtern.dll"));
                WriteFile(Path.Combine("dll", "x86", "opencv_ffmpeg400.dll"));
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
                startInfo.ArgumentList.Add("-MSBuildPath");
                startInfo.ArgumentList.Add(Environment.GetEnvironmentVariable("ComSpec") ?? "C:\\Windows\\System32\\cmd.exe");
                if (skipZip)
                    startInfo.ArgumentList.Add("-SkipZip");

                using (Process process = Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    return new PackagingResult(process.ExitCode, output);
                }
            }

            private void WriteFile(string relativePath)
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
            public PackagingResult(int exitCode, string output)
            {
                ExitCode = exitCode;
                Output = output ?? string.Empty;
            }

            public int ExitCode { get; private set; }
            public string Output { get; private set; }
        }
    }
}
