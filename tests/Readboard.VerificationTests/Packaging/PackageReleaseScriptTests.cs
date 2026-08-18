using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
                Assert.Contains("BuildOutputDir", result.Output);
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
        public void SkipBuild_RejectsBundledFixedVersionWebView2Runtime()
        {
            using (PackagingWorkspace workspace = PackagingWorkspace.Create("v3.1.0"))
            {
                workspace.CreateBuildOutputs();
                workspace.WriteFile("WebView2Runtime\\msedgewebview2.exe");

                PackagingResult result = workspace.RunPackagingScript(skipZip: true);

                Assert.NotEqual(0, result.ExitCode);
                Assert.Contains("Fixed Version Runtime", result.Output);
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
                string releaseAppDirectory = Path.Combine(releaseDirectory, "readboard");
                Assert.True(File.Exists(Path.Combine(releaseAppDirectory, "readboard.exe")));
                Assert.False(File.Exists(Path.Combine(releaseDirectory, "readboard.exe")));
                Assert.False(
                    File.Exists(Path.Combine(releaseAppDirectory, "config_readboard_others.txt")));

                string releaseZipPath = Assert.Single(Directory.GetFiles(workspace.ReleaseRoot, "*.zip"));
                using (ZipArchive archive = ZipFile.OpenRead(releaseZipPath))
                {
                    Assert.Contains(archive.Entries, entry => entry.FullName == "readboard/readboard.exe");
                    Assert.DoesNotContain(archive.Entries, entry => entry.FullName == "readboard.exe");
                }
            }
        }

        [Fact]
        public void SkipBuild_SkipZip_ProducesReleaseDirectoryWithoutZipArtifact()
        {
            using (PackagingWorkspace workspace = PackagingWorkspace.Create())
            {
                workspace.CreateBuildOutputs();
                File.WriteAllText(Path.Combine(workspace.ReleaseRoot, workspace.ExpectedZipFileName), "stale zip");
                File.WriteAllText(Path.Combine(workspace.ReleaseRoot, workspace.ExpectedChecksumFileName), "stale checksum");

                PackagingResult result = workspace.RunPackagingScript(skipZip: true);

                Assert.True(result.ExitCode == 0, result.Output);
                string releaseDirectory = Assert.Single(Directory.GetDirectories(workspace.ReleaseRoot));
                Assert.True(File.Exists(Path.Combine(releaseDirectory, "readboard", "readboard.exe")));
                Assert.DoesNotContain(".zip", result.Output, StringComparison.OrdinalIgnoreCase);
                Assert.Empty(Directory.GetFiles(workspace.ReleaseRoot, "*.zip"));
                Assert.Empty(Directory.GetFiles(workspace.ReleaseRoot, "*.sha256"));
            }
        }

        [Fact]
        public void SkipBuild_Version30_UsesLegacyAssetName()
        {
            using (PackagingWorkspace workspace = PackagingWorkspace.Create("v3.0.9"))
            {
                workspace.CreateBuildOutputs();

                PackagingResult result = workspace.RunPackagingScript();

                Assert.True(result.ExitCode == 0, result.Output);
                Assert.True(File.Exists(Path.Combine(
                    workspace.ReleaseRoot,
                    "readboard-github-release-v3.0.9.zip")));
            }
        }

        [Fact]
        public void SkipBuild_Version31_UsesWebView2AssetName()
        {
            using (PackagingWorkspace workspace = PackagingWorkspace.Create("v3.1.0"))
            {
                workspace.CreateBuildOutputs();

                PackagingResult result = workspace.RunPackagingScript();

                Assert.True(result.ExitCode == 0, result.Output);
                Assert.True(File.Exists(Path.Combine(
                    workspace.ReleaseRoot,
                    "readboard-webview2-v3.1.0.zip")));
                Assert.True(File.Exists(Path.Combine(
                    workspace.ReleaseRoot,
                    "readboard-webview2-v3.1.0",
                    "readboard",
                    "WebView2Loader.dll")));
            }
        }

        [Fact]
        public void SkipBuild_WritesSha256SidecarMatchingZip()
        {
            using (PackagingWorkspace workspace = PackagingWorkspace.Create())
            {
                workspace.CreateBuildOutputs();

                PackagingResult result = workspace.RunPackagingScript();

                Assert.True(result.ExitCode == 0, result.Output);
                string zipPath = Path.Combine(workspace.ReleaseRoot, workspace.ExpectedZipFileName);
                string checksumPath = Path.Combine(workspace.ReleaseRoot, workspace.ExpectedChecksumFileName);
                string expectedHash;
                using (SHA256 sha256 = SHA256.Create())
                using (FileStream stream = File.OpenRead(zipPath))
                    expectedHash = Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();

                Assert.Equal(
                    expectedHash + "  " + workspace.ExpectedZipFileName,
                    File.ReadAllText(checksumPath).Trim());
                Assert.Contains("PackageSha256=" + expectedHash, result.Output);
                Assert.Contains("PackageChecksumFile=" + checksumPath, result.Output);
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
                string releaseExePath = Path.Combine(releaseDirectory, "readboard", "readboard.exe");
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
                string releaseAppDirectory = Path.Combine(releaseDirectory, "readboard");
                Assert.False(File.Exists(Path.Combine(releaseAppDirectory, "lw.dll")));
                Assert.False(File.Exists(Path.Combine(releaseAppDirectory, "Interop.lw.dll")));
                Assert.False(
                    File.Exists(Path.Combine(releaseAppDirectory, "MouseKeyboardActivityMonitor.dll")));
                Assert.False(File.Exists(Path.Combine(releaseAppDirectory, "readboard.exe.config")));
            }
        }

        private sealed class PackagingWorkspace : IDisposable
        {
            private PackagingWorkspace(string rootPath)
            {
                RootPath = rootPath;
                BuildOutputDir = Path.Combine(rootPath, "build");
                ReleaseRoot = Path.Combine(rootPath, "release");
                ScriptPath = Path.Combine(rootPath, "scripts", "package-readboard-release.local.ps1");
                Directory.CreateDirectory(BuildOutputDir);
                Directory.CreateDirectory(ReleaseRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath));
                File.Copy(
                    Path.Combine(
                        VerificationFixtureLocator.RepositoryRoot(),
                        "scripts",
                        "package-readboard-release.local.ps1"),
                    ScriptPath);
            }

            public string RootPath { get; private set; }
            public string BuildOutputDir { get; private set; }
            public string ReleaseRoot { get; private set; }
            public string ScriptPath { get; private set; }
            public string ExpectedZipFileName
            {
                get
                {
                    string assemblyInfoPath = Path.Combine(RootPath, "readboard", "Properties", "AssemblyInfo.cs");
                    string content = File.ReadAllText(assemblyInfoPath);
                    string token = "AssemblyInformationalVersion(\"";
                    int startIndex = content.IndexOf(token, StringComparison.Ordinal);
                    Assert.True(startIndex >= 0, "Expected AssemblyInformationalVersion in AssemblyInfo.cs.");
                    startIndex += token.Length;
                    int endIndex = content.IndexOf('"', startIndex);
                    Assert.True(endIndex > startIndex, "Expected closing quote for AssemblyInformationalVersion.");
                    string version = content.Substring(startIndex, endIndex - startIndex);
                    Version numericVersion = Version.Parse(version.TrimStart('v', 'V'));
                    string prefix = numericVersion < new Version(3, 1, 0)
                        ? "readboard-github-release-"
                        : "readboard-webview2-";
                    return prefix + version + ".zip";
                }
            }

            public string ExpectedChecksumFileName
            {
                get { return ExpectedZipFileName + ".sha256"; }
            }

            public static PackagingWorkspace Create(string version = "v3.0.9")
            {
                string rootPath = Path.Combine(
                    Path.GetTempPath(),
                    "readboard-package-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);
                PackagingWorkspace workspace = new PackagingWorkspace(rootPath);
                workspace.WriteAssemblyVersion(version);
                return workspace;
            }

            private void WriteAssemblyVersion(string version)
            {
                string path = Path.Combine(RootPath, "readboard", "Properties", "AssemblyInfo.cs");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "[assembly: AssemblyInformationalVersion(\"" + version + "\")]\r\n");
            }

            public void CreateBuildOutputs()
            {
                WriteFile("readboard.exe");
                WriteFile("readboard.dll");
                WriteFile("readboard.runtimeconfig.json");
                WriteFile("readboard.deps.json");
                WriteFile("language_cn.txt");
                WriteFile("language_en.txt");
                WriteFile("language_jp.txt");
                WriteFile("language_kr.txt");
                WriteFile("readme.rtf");
                WriteFile("readme_en.rtf");
                WriteFile("readme_jp.rtf");
                WriteFile("OpenCvSharp.dll");
                WriteFile("OpenCvSharp.Extensions.dll");
                WriteFile("OpenCvSharpExtern.dll");
                WriteFile("opencv_videoio_ffmpeg4100_64.dll");
                WriteFile("Microsoft.Web.WebView2.Core.dll");
                WriteFile("Microsoft.Web.WebView2.WinForms.dll");
                WriteFile("runtimes\\win-x64\\native\\WebView2Loader.dll");
                WriteFile("WebView\\index.html");
                WriteFile("WebView\\styles.css");
                WriteFile("WebView\\app.js");
                WriteFile("WebView\\lizziey.ico");
                WriteFile("WebView\\fonts\\InterVariable.woff2");
                WriteFile("WebView\\fonts\\LICENSE-Inter.txt");
            }

            public void SetBuildOutputTimestamp(string relativePath, DateTime timestampUtc)
            {
                string path = Path.Combine(BuildOutputDir, relativePath);
                Assert.True(File.Exists(path), "Expected build output file before setting timestamp: " + relativePath);
                File.SetLastWriteTimeUtc(path, timestampUtc);
            }

            public PackagingResult RunPackagingScript(bool skipZip = false)
            {
                ProcessStartInfo startInfo = new ProcessStartInfo("pwsh.exe")
                {
                    WorkingDirectory = RootPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false
                };
                startInfo.Environment["NO_COLOR"] = "1";
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-File");
                startInfo.ArgumentList.Add(ScriptPath);
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
