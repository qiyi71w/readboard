using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace readboard
{
    internal sealed class IndependentUpdateRecoveryPlan
    {
        public int SchemaVersion { get; set; } = 1;
        public string OperationId { get; set; }
        public string RequestSha256 { get; set; }
        public string DataSha256 { get; set; }
        public Dictionary<string, string> OriginalFiles { get; set; }
        public Dictionary<string, string> PreparedFiles { get; set; }
        public bool RestartAdopted { get; set; }
        public string RestartRegistrationId { get; set; }
        public int? HostProcessId { get; set; }
        public DateTime? HostProcessStartTimeUtc { get; set; }
    }

    internal sealed class IndependentUpdatePreparation
    {
        public int SchemaVersion { get; set; } = 1;
        public string OperationId { get; set; }
        public string RequestSha256 { get; set; }
        public string DataSha256 { get; set; }
        public string InstallDirectory { get; set; }
        public Dictionary<string, string> ImmutableFiles { get; set; }
    }

    // The updater is the only writer of active/progress/result records. All calls run
    // on its one installation thread: the named mutex is thread-affine on Windows.
    internal sealed class IndependentUpdateInstaller
    {
        // Validation may observe/pause a durable boundary; production has no observer.
        internal static Action<string> DurableCheckpoint;
        private readonly Action<string, string> moveDirectory;
        private readonly Action<IndependentUpdateRequest> waitForExit;

        internal IndependentUpdateInstaller(Action<string, string> moveDirectory = null,
            Action<IndependentUpdateRequest> waitForExit = null)
        {
            this.moveDirectory = moveDirectory ?? Directory.Move;
            this.waitForExit = waitForExit ?? WaitForSourceExit;
        }

        internal IndependentUpdateResult Execute(string requestPath, Func<bool> acceptCommit,
            Action committed, Action<string> report)
        {
            var request = IndependentUpdateFiles.Read<IndependentUpdateRequest>(requestPath);
            IndependentUpdateFiles.ValidateRequest(request);
            string operation = IndependentUpdateFiles.OperationDirectory(request.InstallDirectory, request.OperationId);
            if (!string.Equals(Path.GetFullPath(requestPath), Path.Combine(operation, "request.json"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Request is outside its transaction directory.");
            string root = IndependentUpdateFiles.RecordRoot(request.InstallDirectory);
            string staging = Path.Combine(Path.GetDirectoryName(request.InstallDirectory), ".readboard-staging-" + request.OperationId);
            string backup = Path.Combine(Path.GetDirectoryName(request.InstallDirectory), ".readboard-backup-" + request.OperationId);
            using var mutex = new Mutex(false, @"Global\ReadBoard.Update." + IndependentUpdateFiles.InstallKey(request.InstallDirectory));
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Another update owns this installation.");
            bool oldMoved = false;
            bool claimed = false;
            IndependentUpdateAdoption adoption = null;
            try
            {
                if (File.Exists(Path.Combine(operation, "progress.json"))
                    || File.Exists(Path.Combine(operation, "result.json"))
                    || File.Exists(Path.Combine(operation, "preparation.json")))
                    return Recover(requestPath, request, operation, staging, backup, report);
                IndependentUpdateFiles.AssertLaunchable(request.InstallDirectory, request.OperationId);
                IndependentUpdateFiles.ProbeWritable(request.InstallDirectory);
                IndependentUpdateFiles.ProbeWritable(Path.GetDirectoryName(request.InstallDirectory));
                IndependentUpdateFiles.ProbeReplaceableInstallation(request.InstallDirectory);
                IndependentUpdateFiles.AssertNoReparse(operation);
                if (Directory.Exists(staging) || Directory.Exists(backup)) throw new IOException("Update staging or backup already exists.");
                // Bind the live source before accepting a request, including the actual executable path.
                using (var source = Process.GetProcessById(request.SourceProcessId))
                {
                    if (source.StartTime.ToUniversalTime() != request.SourceProcessStartTimeUtc
                        || !string.Equals(source.MainModule.FileName, Path.Combine(request.InstallDirectory, "readboard.exe"), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Source process identity does not match this installation.");
                }
                var data = IndependentUpdateFiles.Read<IndependentUpdateData>(Path.Combine(operation, "data.json"));
                ValidateData(request, data, staging);
                IndependentUpdateFiles.Publish(Path.Combine(operation, "preparation.json"), new IndependentUpdatePreparation
                {
                    OperationId = request.OperationId, InstallDirectory = request.InstallDirectory,
                    RequestSha256 = HashFile(requestPath), DataSha256 = HashFile(Path.Combine(operation, "data.json")),
                    ImmutableFiles = Snapshot(request.InstallDirectory, data)
                });
                IndependentUpdateFiles.Publish(Path.Combine(root, "active.json"), new IndependentUpdateActive { OperationId = request.OperationId });
                claimed = true;
                DurableCheckpoint?.Invoke("claimed");
                Phase("preparing");
                ExtractPromotedPackage(request, staging);
                IndependentUpdateFiles.VerifyPayload(staging, request.TargetTag);
                ProbeNextUpdater(staging);
                ValidateData(request, data, staging);
                Phase("prepared");
                if (!acceptCommit()) return Finish("cancelled", "Preparation cancelled; the original installation is unchanged.");
                string adoptionPath = Path.Combine(operation, "adoption.json");
                // Missing adoption preserves standalone installer callers; no host is inferred.
                adoption = File.Exists(adoptionPath) ? IndependentUpdateRestart.ReadAdoption(requestPath, request) : null;
                Phase("committed");
                // A missing acknowledgement never authorizes the source to exit. Waiting
                // below then fails unchanged; a lost reply cannot turn into a live swap.
                committed();
                Phase("waitingForExit");
                waitForExit(request);
                AssertFilesReleased(request.InstallDirectory);
                Phase("preservingData");
                PreserveFinalData(request, data, staging);
                // Freeze both complete directory identities only after source exit and
                // final user-data migration. This record is never advanced or rewritten.
                IndependentUpdateFiles.Publish(Path.Combine(operation, "recovery.json"), new IndependentUpdateRecoveryPlan
                {
                    OperationId = request.OperationId,
                    RequestSha256 = HashFile(requestPath),
                    DataSha256 = HashFile(Path.Combine(operation, "data.json")),
                    OriginalFiles = Snapshot(request.InstallDirectory), PreparedFiles = Snapshot(staging),
                    RestartAdopted = adoption?.RegistrationId != null,
                    RestartRegistrationId = adoption?.RegistrationId, HostProcessId = adoption?.HostProcessId,
                    HostProcessStartTimeUtc = adoption?.HostProcessStartTimeUtc
                });
                Phase("movingOld");
                moveDirectory(request.InstallDirectory, backup);
                oldMoved = true;
                Phase("movingNew");
                moveDirectory(staging, request.InstallDirectory);
                Phase("installed");
                return Finish("installed", "");
            }
            catch (Exception exception)
            {
                if (!claimed) throw;
                string diagnostic = exception.Message;
                if (!oldMoved) return Finish("failedUnchanged", diagnostic);
                try
                {
                    Phase("rollingBack");
                    // A failed move must leave the destination absent; never delete an
                    // unexpected directory or the only retained old installation.
                    if (Directory.Exists(request.InstallDirectory))
                        throw new IOException("Installation destination is occupied; backup retained.");
                    moveDirectory(backup, request.InstallDirectory);
                    oldMoved = false;
                    if (IndependentUpdateFiles.ReadVersion(request.InstallDirectory) != "v" + request.SourceVersion.TrimStart('v')
                        || !File.Exists(Path.Combine(request.InstallDirectory, "readboard.exe")))
                        throw new IOException("Restored installation identity could not be verified.");
                    return Finish("rolledBack", diagnostic);
                }
                catch (Exception recovery)
                {
                    return Finish("rollbackFailed", diagnostic + Environment.NewLine + recovery.Message);
                }
            }
            finally { mutex.ReleaseMutex(); }

            void Phase(string phase)
            {
                IndependentUpdateFiles.Publish(Path.Combine(operation, "progress.json"), new IndependentUpdateProgress
                {
                    OperationId = request.OperationId, Phase = phase,
                    RestartAdopted = adoption?.RegistrationId != null,
                    RestartRegistrationId = adoption?.RegistrationId,
                    HostProcessId = adoption?.HostProcessId,
                    HostProcessStartTimeUtc = adoption?.HostProcessStartTimeUtc
                });
                report(phase);
                DurableCheckpoint?.Invoke(phase);
            }

            IndependentUpdateResult Finish(string state, string diagnostic)
            {
                var result = new IndependentUpdateResult
                {
                    OperationId = request.OperationId,
                    State = state,
                    ActualVersion = state == "rollbackFailed" ? null : IndependentUpdateFiles.ReadVersion(request.InstallDirectory),
                    InstallDirectory = request.InstallDirectory,
                    Diagnostic = diagnostic,
                    BackupDirectory = Directory.Exists(backup) ? backup : null
                };
                IndependentUpdateFiles.Publish(Path.Combine(operation, "result.json"), result);
                report(state);
                return result;
            }
        }

        private IndependentUpdateResult Recover(string requestPath, IndependentUpdateRequest request,
            string operation, string staging, string backup, Action<string> report)
        {
            string activePath = Path.Combine(Path.GetDirectoryName(operation), "active.json");
            var active = File.Exists(activePath) ? IndependentUpdateFiles.Read<IndependentUpdateActive>(activePath) : null;
            bool ownsActive = active?.SchemaVersion == 1 && active.OperationId == request.OperationId;
            if (!ownsActive) IndependentUpdateFiles.AssertLaunchable(request.InstallDirectory, request.OperationId);
            string planPath = Path.Combine(operation, "recovery.json");
            var plan = File.Exists(planPath) ? IndependentUpdateFiles.Read<IndependentUpdateRecoveryPlan>(planPath) : null;
            if (plan != null && (plan.SchemaVersion != 1 || plan.OperationId != request.OperationId
                || plan.RequestSha256 != HashFile(requestPath) || plan.DataSha256 != HashFile(Path.Combine(operation, "data.json"))))
                throw new InvalidDataException("Recovery plan no longer binds the immutable request and data ownership.");
            string preparationPath = Path.Combine(operation, "preparation.json");
            var preparation = File.Exists(preparationPath) ? IndependentUpdateFiles.Read<IndependentUpdatePreparation>(preparationPath) : null;
            if (preparation != null && (preparation.SchemaVersion != 1 || preparation.OperationId != request.OperationId
                || preparation.InstallDirectory != request.InstallDirectory || preparation.RequestSha256 != HashFile(requestPath)
                || preparation.DataSha256 != HashFile(Path.Combine(operation, "data.json"))))
                throw new InvalidDataException("Preparation no longer binds the immutable request and installation.");
            if (!ownsActive && (plan != null || preparation == null || File.Exists(Path.Combine(operation, "progress.json"))
                || File.Exists(Path.Combine(operation, "result.json"))))
                throw new InvalidDataException("Recovery does not own the active operation.");
            string resultPath = Path.Combine(operation, "result.json");
            if (File.Exists(resultPath))
            {
                var prior = IndependentUpdateFiles.Read<IndependentUpdateResult>(resultPath);
                if (prior.SchemaVersion != 1 || prior.OperationId != request.OperationId
                    || prior.InstallDirectory != request.InstallDirectory
                    || !new[] { "installed", "failedUnchanged", "rolledBack", "rollbackFailed", "cancelled" }.Contains(prior.State)
                    || (prior.BackupDirectory != null && prior.BackupDirectory != backup)
                    || (new[] { "installed", "rolledBack" }.Contains(prior.State) && plan == null)
                    || (prior.State == "installed" && prior.ActualVersion != request.TargetTag)
                    || (new[] { "failedUnchanged", "rolledBack", "cancelled" }.Contains(prior.State)
                        && prior.ActualVersion != "v" + request.SourceVersion.TrimStart('v'))
                    || (prior.State != "rollbackFailed" && (prior.ActualVersion == null
                        || prior.ActualVersion != IndependentUpdateFiles.ReadVersion(request.InstallDirectory))))
                    throw new InvalidDataException("Invalid terminal recovery record.");
                if (plan == null && prior.State != "rollbackFailed") ValidateUnchanged();
                report(prior.State);
                return prior;
            }
            string progressPath = Path.Combine(operation, "progress.json");
            var progress = File.Exists(progressPath) ? IndependentUpdateFiles.Read<IndependentUpdateProgress>(progressPath) : null;
            if (plan == null)
            {
                ValidateUnchanged();
                if (progress != null && (progress.SchemaVersion != 1 || progress.OperationId != request.OperationId
                    || !new[] { "preparing", "prepared", "committed", "waitingForExit", "preservingData" }.Contains(progress.Phase)))
                    throw new InvalidDataException("Unknown early recovery phase.");
                bool committed = progress != null && new[] { "committed", "waitingForExit", "preservingData" }.Contains(progress.Phase);
                IndependentUpdateAdoption adoption = committed && File.Exists(Path.Combine(operation, "adoption.json"))
                    ? IndependentUpdateRestart.ReadAdoption(requestPath, request) : null;
                if (progress != null && (progress.RestartAdopted != (adoption?.RegistrationId != null)
                    || progress.RestartRegistrationId != adoption?.RegistrationId || progress.HostProcessId != adoption?.HostProcessId
                    || progress.HostProcessStartTimeUtc != adoption?.HostProcessStartTimeUtc))
                    throw new InvalidDataException("Early recovery restart identity does not match.");
                if (IndependentUpdateRestart.IsAlive(request.SourceProcessId, request.SourceProcessStartTimeUtc))
                    throw new IOException("The original ReadBoard process is still running; recovery cannot settle it.");
                var unchanged = new IndependentUpdateResult
                {
                    OperationId = request.OperationId, State = "failedUnchanged",
                    ActualVersion = IndependentUpdateFiles.ReadVersion(request.InstallDirectory),
                    InstallDirectory = request.InstallDirectory,
                    Diagnostic = "The updater stopped before replacing the original installation.", BackupDirectory = null
                };
                if (!ownsActive) IndependentUpdateFiles.Publish(activePath, new IndependentUpdateActive { OperationId = request.OperationId });
                IndependentUpdateFiles.Publish(resultPath, unchanged);
                report(unchanged.State);
                return unchanged;
            }
            if (progress == null || progress.SchemaVersion != 1 || progress.OperationId != request.OperationId
                || !new[] { "preservingData", "movingOld", "movingNew", "installed", "rollingBack" }.Contains(progress.Phase)
                || progress.RestartAdopted != plan.RestartAdopted
                || progress.RestartRegistrationId != plan.RestartRegistrationId
                || progress.HostProcessId != plan.HostProcessId || progress.HostProcessStartTimeUtc != plan.HostProcessStartTimeUtc)
                throw new InvalidDataException("Recovery plan or committed identity does not match.");
            if (plan.RestartAdopted)
            {
                var adoption = IndependentUpdateRestart.ReadAdoption(requestPath, request);
                if (adoption.RegistrationId == null || adoption.RegistrationId != plan.RestartRegistrationId
                    || adoption.HostProcessId != plan.HostProcessId || adoption.HostProcessStartTimeUtc != plan.HostProcessStartTimeUtc)
                    throw new InvalidDataException("Recovery restart identity does not match.");
            }
            else if (plan.RestartRegistrationId != null || plan.HostProcessId != null || plan.HostProcessStartTimeUtc != null)
                throw new InvalidDataException("Recovery contains an unadopted restart identity.");
            if (IndependentUpdateRestart.IsAlive(request.SourceProcessId, request.SourceProcessStartTimeUtc))
                throw new IOException("The original ReadBoard process is still running; recovery cannot replace it.");

            bool hasInstall = Directory.Exists(request.InstallDirectory);
            bool hasBackup = Directory.Exists(backup);
            bool hasStaging = Directory.Exists(staging);
            string state;
            if (hasInstall && !hasBackup && hasStaging && progress.Phase != "installed" && progress.Phase != "rollingBack")
            {
                MatchSnapshot(request.InstallDirectory, plan.OriginalFiles);
                MatchSnapshot(staging, plan.PreparedFiles);
                AssertFilesReleased(request.InstallDirectory);
                SetPhase("movingOld");
                moveDirectory(request.InstallDirectory, backup);
                SetPhase("movingNew");
                moveDirectory(staging, request.InstallDirectory);
                state = "installed";
            }
            else if (!hasInstall && hasBackup && hasStaging)
            {
                MatchSnapshot(backup, plan.OriginalFiles);
                MatchSnapshot(staging, plan.PreparedFiles);
                SetPhase("movingNew");
                moveDirectory(staging, request.InstallDirectory);
                state = "installed";
            }
            else if (hasInstall && hasBackup && !hasStaging)
            {
                MatchSnapshot(backup, plan.OriginalFiles);
                MatchSnapshot(request.InstallDirectory, plan.PreparedFiles);
                state = "installed";
            }
            else if (hasInstall && !hasBackup && progress.Phase == "rollingBack")
            {
                MatchSnapshot(request.InstallDirectory, plan.OriginalFiles);
                state = "rolledBack";
            }
            else throw new InvalidDataException("Directory ownership cannot be proven; all recovery material was retained.");

            string expected = state == "installed" ? request.TargetTag : "v" + request.SourceVersion.TrimStart('v');
            if (IndependentUpdateFiles.ReadVersion(request.InstallDirectory) != expected)
                throw new InvalidDataException("Recovered directory version does not match the immutable plan.");
            SetPhase(state == "installed" ? "installed" : "rollingBack");
            var result = new IndependentUpdateResult
            {
                OperationId = request.OperationId, State = state, ActualVersion = expected,
                InstallDirectory = request.InstallDirectory, Diagnostic = "",
                BackupDirectory = Directory.Exists(backup) ? backup : null
            };
            IndependentUpdateFiles.Publish(resultPath, result);
            report(state);
            return result;

            void ValidateUnchanged()
            {
                if (preparation?.ImmutableFiles == null || preparation.ImmutableFiles.Count == 0
                    || Directory.Exists(backup) || File.Exists(backup) || !Directory.Exists(request.InstallDirectory)
                    || IndependentUpdateFiles.ReadVersion(request.InstallDirectory) != "v" + request.SourceVersion.TrimStart('v'))
                    throw new InvalidDataException("Original installation ownership cannot be proven.");
                var data = IndependentUpdateFiles.Read<IndependentUpdateData>(Path.Combine(operation, "data.json"));
                if (data.SchemaVersion != 1 || data.OperationId != request.OperationId || data.Directories == null || data.Files == null)
                    throw new InvalidDataException("Invalid original data ownership.");
                var actual = Snapshot(request.InstallDirectory, data);
                if (actual.Count != preparation.ImmutableFiles.Count
                    || preparation.ImmutableFiles.Any(item => !actual.TryGetValue(item.Key, out string hash) || hash != item.Value))
                    throw new InvalidDataException("Original immutable payload changed; recovery material was retained.");
            }

            void SetPhase(string phase)
            {
                progress.Phase = phase;
                IndependentUpdateFiles.Publish(Path.Combine(operation, "progress.json"), progress);
                report(phase);
            }
        }

        private static string HashFile(string path)
        {
            IndependentUpdateFiles.AssertNoReparse(path);
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static Dictionary<string, string> Snapshot(string directory, IndependentUpdateData mutableData = null)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Visit(directory);
            return files;
            void Visit(string current)
            {
                IndependentUpdateFiles.AssertNoReparse(current);
                foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                {
                    IndependentUpdateFiles.AssertNoReparse(entry);
                    string relative = Path.GetRelativePath(directory, entry).Replace('\\', '/');
                    IndependentUpdateFiles.ValidateRelativePath(relative);
                    if (mutableData != null && (IndependentUpdateFiles.ConfigurationFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)
                        || mutableData.Files.Contains(relative, StringComparer.OrdinalIgnoreCase)
                        || mutableData.Directories.Any(path => relative.Equals(path, StringComparison.OrdinalIgnoreCase)
                            || relative.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)))) continue;
                    if (Directory.Exists(entry)) { files.Add(relative + "/", ""); Visit(entry); }
                    else files.Add(relative, HashFile(entry));
                }
            }
        }

        private static void MatchSnapshot(string directory, Dictionary<string, string> expected)
        {
            if (expected == null || expected.Count == 0) throw new InvalidDataException("Missing directory identity.");
            var actual = Snapshot(directory);
            if (actual.Count != expected.Count || expected.Any(item => !actual.TryGetValue(item.Key, out string hash) || hash != item.Value))
                throw new InvalidDataException("Recovery directory evidence differs from the frozen plan: " + directory);
        }

        private static void ProbeNextUpdater(string staging)
        {
            string directory = Path.Combine(staging, "updater");
            var start = new ProcessStartInfo(Path.Combine(directory, IndependentUpdateFiles.RunnerName))
            { UseShellExecute = false, WorkingDirectory = directory, CreateNoWindow = true };
            start.ArgumentList.Add("--probe");
            using var process = Process.Start(start) ?? throw new IOException("Next updater could not start.");
            if (!process.WaitForExit(15000))
            {
                process.Kill();
                process.WaitForExit();
                throw new IOException("Next updater did not finish its runtime probe.");
            }
            if (process.ExitCode != 0) throw new IOException("Next updater runtime probe failed.");
        }

        private static void ExtractPromotedPackage(IndependentUpdateRequest request, string staging)
        {
            // Keep the verified bytes open without write/delete sharing through extraction.
            using var package = new FileStream(request.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant() != request.ExpectedSha256)
                throw new InvalidDataException("Package bytes no longer match the promoted SHA-256.");
            package.Position = 0;
            using var archive = new ZipArchive(package, ZipArchiveMode.Read);
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                string name = entry.FullName;
                bool directory = name.EndsWith('/');
                IndependentUpdateFiles.ValidateRelativePath(directory ? name.TrimEnd('/') : name);
                if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                    || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("Archive links are forbidden.");
                if (!directory && !entries.TryAdd(name, entry)) throw new InvalidDataException("Duplicate archive path.");
            }
            string prefix = entries.ContainsKey("readboard.exe") ? "" : "readboard/";
            if (entries.Count == 0 || entries.Keys.Any(name => !name.StartsWith(prefix, StringComparison.Ordinal)))
                throw new InvalidDataException("Invalid release package layout.");
            Directory.CreateDirectory(staging);
            foreach (var item in entries)
            {
                string relative = item.Key.Substring(prefix.Length);
                IndependentUpdateFiles.ValidateRelativePath(relative);
                string destination = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!IndependentUpdateFiles.IsWithin(staging, destination)) throw new InvalidDataException("Archive path escapes staging.");
                IndependentUpdateFiles.AssertNoReparse(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using var source = item.Value.Open();
                using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                source.CopyTo(target);
            }
        }

        private static void ValidateData(IndependentUpdateRequest request, IndependentUpdateData data, string staging)
        {
            if (data.SchemaVersion != 1 || data.OperationId != request.OperationId || data.Directories == null || data.Files == null)
                throw new InvalidDataException("Invalid user data ownership record.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string relative in data.Directories.Concat(data.Files))
            {
                IndependentUpdateFiles.ValidateRelativePath(relative);
                if (!names.Add(relative) || File.Exists(Path.Combine(staging, relative)) || Directory.Exists(Path.Combine(staging, relative))
                    || IndependentUpdateFiles.ConfigurationFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("User data overlaps release payload: " + relative);
                IndependentUpdateFiles.AssertNoReparse(Path.Combine(request.InstallDirectory, relative));
            }
        }

        private static void PreserveFinalData(IndependentUpdateRequest request, IndependentUpdateData data, string staging)
        {
            foreach (string name in IndependentUpdateFiles.ConfigurationFiles)
            {
                string old = Path.Combine(request.InstallDirectory, name);
                string target = Path.Combine(staging, name);
                IndependentUpdateFiles.AssertNoReparse(old);
                if (File.Exists(target)) File.Delete(target);
                if (File.Exists(old)) File.Copy(old, target, false);
            }
            foreach (string directory in data.Directories)
            {
                string old = Path.Combine(request.InstallDirectory, directory);
                if (Directory.Exists(old)) IndependentUpdateFiles.CopyTree(old, Path.Combine(staging, directory));
            }
            foreach (string file in data.Files)
            {
                string old = Path.Combine(request.InstallDirectory, file);
                if (File.Exists(old)) File.Copy(old, Path.Combine(staging, file), false);
            }
        }

        private static void WaitForSourceExit(IndependentUpdateRequest request)
        {
            Process source;
            try { source = Process.GetProcessById(request.SourceProcessId); }
            catch (ArgumentException) { return; }
            using (source)
            {
                if (source.StartTime.ToUniversalTime() != request.SourceProcessStartTimeUtc) return;
                if (!source.WaitForExit(30000)) throw new IOException("ReadBoard did not finish exiting; no files were replaced.");
            }
        }

        private static void AssertFilesReleased(string directory)
        {
            IndependentUpdateFiles.AssertNoReparse(directory);
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                IndependentUpdateFiles.AssertNoReparse(entry);
                if (Directory.Exists(entry)) AssertFilesReleased(entry);
                else using (new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            }
        }
    }
}
