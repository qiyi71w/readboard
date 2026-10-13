using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Security.AccessControl;
using System.Security.Principal;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class IndependentUpdateTransactionTests
    {
        [Theory]
        [InlineData("../escape.dll")]
        [InlineData("C:/escape.dll")]
        [InlineData("file.dll:stream")]
        [InlineData("CON.txt")]
        [InlineData("dir/file. ")]
        [InlineData("dir//file")]
        [InlineData("dir/./file")]
        [InlineData("dir/COM1.dll")]
        public void WindowsAliasesCannotBecomePackagePaths(string path)
        {
            Assert.Throws<InvalidDataException>(() => readboard.IndependentUpdateFiles.ValidateRelativePath(path));
        }

        [Fact]
        public void InterruptedTransactionPreventsBusinessStartup()
        {
            string root = Path.Combine(Path.GetTempPath(), "readboard-update-test-" + Guid.NewGuid().ToString("N"));
            string install = Path.Combine(root, "app");
            try
            {
                Directory.CreateDirectory(install);
                string records = readboard.IndependentUpdateFiles.RecordRoot(install);
                Directory.CreateDirectory(records);
                File.WriteAllText(Path.Combine(records, "active.json"), "{broken");
                Assert.ThrowsAny<Exception>(() => readboard.IndependentUpdateFiles.AssertLaunchable(install));
                Assert.True(Directory.Exists(install));
            }
            finally { Directory.Delete(root, true); }
        }

        [Theory]
        [InlineData("movingOld")]
        [InlineData("movingNew")]
        [InlineData("installed")]
        public void NextRunConvergesOriginalOperationAndReplaysTerminalResult(string phase)
        {
            using var fixture = new RecoveryFixture(phase);
            var installer = new IndependentUpdateInstaller();
            var result = installer.Execute(fixture.RequestPath,
                () => throw new Exception("Recovery must not ask to install again."),
                () => throw new Exception("Recovery must not submit again."), _ => { });
            Assert.Equal("installed", result.State);
            Assert.Equal(fixture.Request.OperationId, result.OperationId);
            Assert.Equal("new program", File.ReadAllText(Path.Combine(fixture.Install, "readboard.exe")));
            Assert.Equal("saved preferences", File.ReadAllText(Path.Combine(fixture.Install, "config.readboard.json")));
            Assert.False(File.Exists(Path.Combine(fixture.Install, "obsolete.dll")));
            Assert.Equal("old dependency", File.ReadAllText(Path.Combine(fixture.Backup, "obsolete.dll")));
            byte[] terminal = File.ReadAllBytes(Path.Combine(fixture.Operation, "result.json"));
            Assert.Equal("installed", installer.Execute(fixture.RequestPath, () => false, () => { }, _ => { }).State);
            Assert.Equal(terminal, File.ReadAllBytes(Path.Combine(fixture.Operation, "result.json")));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void InvalidTerminalRecordBlocksReplayAndPreservesInstalledFiles(bool corruptJson)
        {
            using var fixture = new RecoveryFixture("installed");
            string path = Path.Combine(fixture.Operation, "result.json");
            if (corruptJson) File.WriteAllText(path, "{broken");
            else IndependentUpdateFiles.Publish(path, new IndependentUpdateResult
            {
                OperationId = Guid.NewGuid().ToString("N"), State = "installed",
                ActualVersion = fixture.Request.TargetTag, InstallDirectory = fixture.Install,
                Diagnostic = "", BackupDirectory = fixture.Backup
            });
            byte[] original = File.ReadAllBytes(path);
            Assert.ThrowsAny<Exception>(() => new IndependentUpdateInstaller().Execute(fixture.RequestPath, () => true, () => { }, _ => { }));
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal("new program", File.ReadAllText(Path.Combine(fixture.Install, "readboard.exe")));
            Assert.Equal("old program", File.ReadAllText(Path.Combine(fixture.Backup, "readboard.exe")));
        }

        [Theory]
        [InlineData("request")]
        [InlineData("missing-plan")]
        [InlineData("corrupt-plan")]
        public void TerminalReplayStillRequiresOriginalImmutableEvidence(string fault)
        {
            using var fixture = new RecoveryFixture("installed");
            var installer = new IndependentUpdateInstaller();
            installer.Execute(fixture.RequestPath, () => false, () => { }, _ => { });
            byte[] terminal = File.ReadAllBytes(Path.Combine(fixture.Operation, "result.json"));
            if (fault == "request") File.AppendAllText(fixture.RequestPath, " ");
            else if (fault == "missing-plan") File.Delete(Path.Combine(fixture.Operation, "recovery.json"));
            else File.WriteAllText(Path.Combine(fixture.Operation, "recovery.json"), "{broken");
            Assert.ThrowsAny<Exception>(() => installer.Execute(fixture.RequestPath, () => true, () => { }, _ => { }));
            Assert.Equal(terminal, File.ReadAllBytes(Path.Combine(fixture.Operation, "result.json")));
            Assert.True(Directory.Exists(fixture.Backup));
        }

        [Theory]
        [InlineData("plan-operation")]
        [InlineData("request")]
        [InlineData("corrupt-progress")]
        [InlineData("progress-operation")]
        [InlineData("directory")]
        public void UnprovenRecoveryPreservesOnlyBackupAndNeverPublishesSuccess(string fault)
        {
            using var fixture = new RecoveryFixture("movingNew");
            if (fault == "plan-operation")
            {
                fixture.Plan.OperationId = Guid.NewGuid().ToString("N");
                IndependentUpdateFiles.Publish(Path.Combine(fixture.Operation, "recovery.json"), fixture.Plan);
            }
            if (fault == "request") File.AppendAllText(fixture.RequestPath, " ");
            if (fault == "corrupt-progress") File.WriteAllText(Path.Combine(fixture.Operation, "progress.json"), "{broken");
            if (fault == "progress-operation")
                IndependentUpdateFiles.Publish(Path.Combine(fixture.Operation, "progress.json"), new IndependentUpdateProgress
                { OperationId = Guid.NewGuid().ToString("N"), Phase = "movingNew" });
            if (fault == "directory") File.WriteAllText(Path.Combine(fixture.Staging, "config.readboard.json"), "wrong preferences");
            Assert.ThrowsAny<Exception>(() => new IndependentUpdateInstaller().Execute(fixture.RequestPath, () => true, () => { }, _ => { }));
            Assert.False(Directory.Exists(fixture.Install));
            Assert.Equal("saved preferences", File.ReadAllText(Path.Combine(fixture.Backup, "config.readboard.json")));
            Assert.False(File.Exists(Path.Combine(fixture.Operation, "result.json")));
        }

        [Fact]
        public void LiveSourceBlocksRecoveryButReusedPidDoesNot()
        {
            using var fixture = new RecoveryFixture("movingNew", liveSource: true);
            Assert.Throws<IOException>(() => new IndependentUpdateInstaller().Execute(fixture.RequestPath, () => true, () => { }, _ => { }));
            Assert.False(Directory.Exists(fixture.Install));
            fixture.Request.SourceProcessStartTimeUtc = fixture.Request.SourceProcessStartTimeUtc.AddDays(-1);
            fixture.PublishRequest();
            Assert.Equal("installed", new IndependentUpdateInstaller().Execute(fixture.RequestPath, () => false, () => { }, _ => { }).State);
        }

        [Fact]
        public void ActiveOwnerCannotBePreemptedByAnotherRecoveryThread()
        {
            using var fixture = new RecoveryFixture("movingNew");
            using var held = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var owner = new Thread(() =>
            {
                using var mutex = new Mutex(false, @"Global\ReadBoard.Update." + IndependentUpdateFiles.InstallKey(fixture.Install));
                mutex.WaitOne(); held.Set(); release.Wait(); mutex.ReleaseMutex();
            });
            owner.Start(); held.Wait();
            try
            {
                Assert.Throws<IOException>(() => new IndependentUpdateInstaller().Execute(fixture.RequestPath, () => true, () => { }, _ => { }));
                Assert.False(Directory.Exists(fixture.Install));
                Assert.True(Directory.Exists(fixture.Backup));
            }
            finally { release.Set(); owner.Join(); }
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void ExistingInstallationDeleteRightsGatePreparationBeforeCommit(bool denyDelete, bool denyDeleteChild)
        {
            string root = Path.Combine(Path.GetTempPath(), "readboard-permissions-" + Guid.NewGuid().ToString("N"));
            string install = Path.Combine(root, "app");
            Directory.CreateDirectory(install);
            var parentInfo = new DirectoryInfo(root);
            var installInfo = new DirectoryInfo(install);
            DirectorySecurity parentAcl = parentInfo.GetAccessControl();
            DirectorySecurity installAcl = installInfo.GetAccessControl();
            using var identity = WindowsIdentity.GetCurrent();
            Process source = null;
            try
            {
                string executable = Path.Combine(install, "readboard.exe");
                File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), executable);
                string preferences = Path.Combine(install, "config.readboard.json");
                File.WriteAllText(preferences, "saved preferences");
                byte[] original = File.ReadAllBytes(executable);
                source = Process.Start(new ProcessStartInfo(executable)
                {
                    Arguments = "/d /q /c set /p readboardInput=", UseShellExecute = false,
                    WorkingDirectory = install,
                    RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true
                });
                string id = Guid.NewGuid().ToString("N");
                string operation = IndependentUpdateFiles.OperationDirectory(install, id);
                Directory.CreateDirectory(operation);
                string requestPath = Path.Combine(operation, "request.json");
                IndependentUpdateFiles.Publish(requestPath, new IndependentUpdateRequest
                {
                    OperationId = id, SourceVersion = "v3.1.9", TargetTag = "v3.2.0",
                    AssetName = "readboard-webview2-v3.2.0.zip", ExpectedSha256 = new string('a', 64),
                    PackagePath = Path.Combine(operation, "readboard-webview2-v3.2.0.zip"),
                    InstallDirectory = install, SourceProcessId = source.Id,
                    SourceProcessStartTimeUtc = source.StartTime.ToUniversalTime(), Language = "en"
                });
                if (denyDeleteChild) Deny(parentInfo, FileSystemRights.DeleteSubdirectoriesAndFiles);
                if (denyDelete) Deny(installInfo, FileSystemRights.Delete);
                bool accepted = false, committed = false, waited = false;
                var phases = new List<string>();
                var installer = new IndependentUpdateInstaller(waitForExit: _ => waited = true);
                IndependentUpdateResult Execute() => installer.Execute(requestPath,
                    () => { accepted = true; return true; }, () => committed = true,
                    phase => { phases.Add(phase); if (phase == "preparing") throw new IOException("Stop after permission admission."); });
                if (denyDelete && denyDeleteChild)
                {
                    Assert.Throws<UnauthorizedAccessException>(() => Execute());
                    Assert.Empty(phases);
                    Assert.False(File.Exists(Path.Combine(operation, "progress.json")));
                    Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(operation), "active.json")));
                }
                else
                {
                    Assert.Equal("failedUnchanged", Execute().State);
                    Assert.Equal(new[] { "preparing" }, phases);
                }
                Assert.False(accepted); Assert.False(committed); Assert.False(waited);
                Assert.False(source.HasExited);
                Assert.Equal(original, File.ReadAllBytes(executable));
                Assert.Equal("saved preferences", File.ReadAllText(preferences));
                source.StandardInput.Close(); source.WaitForExit();
                if (denyDelete && denyDeleteChild)
                { Restore(parentInfo, parentAcl); Restore(installInfo, installAcl); }
                string moved = Path.Combine(root, "renamed");
                Directory.Move(install, moved);
                Directory.Move(moved, install);
            }
            finally
            {
                try { Restore(parentInfo, parentAcl); Restore(installInfo, installAcl); }
                finally { if (source != null) { if (!source.HasExited) { source.Kill(); source.WaitForExit(); } source.Dispose(); } }
                Directory.Delete(root, true);
            }
            void Restore(DirectoryInfo directory, DirectorySecurity original)
            {
                var restored = new DirectorySecurity();
                restored.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
                directory.SetAccessControl(restored);
            }
            void Deny(DirectoryInfo directory, FileSystemRights rights)
            {
                DirectorySecurity acl = directory.GetAccessControl();
                acl.AddAccessRule(new FileSystemAccessRule(identity.User, rights, InheritanceFlags.None,
                    PropagationFlags.None, AccessControlType.Deny));
                directory.SetAccessControl(acl);
            }
        }

        private sealed class RecoveryFixture : IDisposable
        {
            private readonly string root = Path.Combine(Path.GetTempPath(), "readboard-recovery-" + Guid.NewGuid().ToString("N"));
            internal string Install, Staging, Backup, Operation, RequestPath;
            internal IndependentUpdateRequest Request;
            internal IndependentUpdateRecoveryPlan Plan;

            internal RecoveryFixture(string phase, bool liveSource = false)
            {
                Install = Path.Combine(root, "app");
                string id = Guid.NewGuid().ToString("N");
                Staging = Path.Combine(root, ".readboard-staging-" + id);
                Backup = Path.Combine(root, ".readboard-backup-" + id);
                Operation = IndependentUpdateFiles.OperationDirectory(Install, id);
                Directory.CreateDirectory(Operation);
                RequestPath = Path.Combine(Operation, "request.json");
                using var source = Process.GetCurrentProcess();
                string version = "v" + typeof(IndependentUpdateInstaller).Assembly.GetName().Version.ToString(3);
                Request = new IndependentUpdateRequest
                {
                    OperationId = id, SourceVersion = version, TargetTag = version,
                    AssetName = "readboard-webview2-" + version + ".zip", ExpectedSha256 = new string('a', 64),
                    PackagePath = Path.Combine(Operation, "readboard-webview2-" + version + ".zip"),
                    InstallDirectory = Install, SourceProcessId = source.Id,
                    SourceProcessStartTimeUtc = source.StartTime.ToUniversalTime().AddDays(liveSource ? 0 : -1), Language = "en"
                };
                string old = phase == "movingOld" ? Install : Backup;
                string next = phase == "installed" ? Install : Staging;
                Directory.CreateDirectory(old); Directory.CreateDirectory(next);
                File.Copy(typeof(IndependentUpdateInstaller).Assembly.Location, Path.Combine(old, "readboard.dll"));
                File.Copy(typeof(IndependentUpdateInstaller).Assembly.Location, Path.Combine(next, "readboard.dll"));
                File.WriteAllText(Path.Combine(old, "readboard.exe"), "old program");
                File.WriteAllText(Path.Combine(next, "readboard.exe"), "new program");
                File.WriteAllText(Path.Combine(old, "obsolete.dll"), "old dependency");
                File.WriteAllText(Path.Combine(old, "config.readboard.json"), "saved preferences");
                File.WriteAllText(Path.Combine(next, "config.readboard.json"), "saved preferences");
                IndependentUpdateFiles.Publish(Path.Combine(Operation, "data.json"), new IndependentUpdateData { OperationId = id });
                Plan = new IndependentUpdateRecoveryPlan
                {
                    OperationId = id, DataSha256 = Hash(Path.Combine(Operation, "data.json")),
                    OriginalFiles = Inventory(old), PreparedFiles = Inventory(next)
                };
                PublishRequest();
                IndependentUpdateFiles.Publish(Path.Combine(Path.GetDirectoryName(Operation), "active.json"), new IndependentUpdateActive { OperationId = id });
                IndependentUpdateFiles.Publish(Path.Combine(Operation, "progress.json"), new IndependentUpdateProgress { OperationId = id, Phase = phase });
            }
            internal void PublishRequest()
            {
                IndependentUpdateFiles.Publish(RequestPath, Request);
                Plan.RequestSha256 = Hash(RequestPath);
                IndependentUpdateFiles.Publish(Path.Combine(Operation, "recovery.json"), Plan);
            }
            private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
            private static Dictionary<string, string> Inventory(string directory)
            {
                var result = new Dictionary<string, string>();
                foreach (string file in Directory.GetFiles(directory)) result.Add(Path.GetFileName(file), Hash(file));
                return result;
            }
            public void Dispose() => Directory.Delete(root, true);
        }
    }
}
