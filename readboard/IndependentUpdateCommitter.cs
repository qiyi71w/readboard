using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace readboard
{
    internal sealed class IndependentUpdateCommitter : IIndependentUpdateCommitter
    {
        private readonly Func<string> language;
        private readonly Func<IEnumerable<string>> userDirectories;
        private readonly IndependentUpdateRestartProtocol restart;
        private string preparedInstall;
        private string preparedOperation;

        internal IndependentUpdateCommitter(Func<string> language, Func<IEnumerable<string>> userDirectories,
            IndependentUpdateRestartProtocol restart = null)
        { this.language = language; this.userDirectories = userDirectories; this.restart = restart; }

        public async Task PrepareAsync(CancellationToken cancellationToken)
        {
            string install = IndependentUpdateFiles.CanonicalDirectory(Path.GetDirectoryName(Environment.ProcessPath));
            IndependentUpdateFiles.AssertLaunchable(install);
            IndependentUpdateFiles.ProbeWritable(install);
            IndependentUpdateFiles.ProbeWritable(Path.GetDirectoryName(install));
            IndependentUpdateFiles.ProbeReplaceableInstallation(install);
            if (preparedOperation != null) throw new InvalidOperationException("An updater is already prepared.");
            preparedInstall = install;
            preparedOperation = IndependentUpdateFiles.OperationDirectory(install, Guid.NewGuid().ToString("N"));
            string runnerDirectory = Path.Combine(preparedOperation, "runner");
            cancellationToken.ThrowIfCancellationRequested();
            var payload = IndependentUpdateFiles.Read<IndependentUpdatePayload>(Path.Combine(install, IndependentUpdateFiles.ManifestName));
            if (payload.SchemaVersion != 1 || payload.Version != IndependentUpdateFiles.ReadVersion(install) || payload.Files == null)
                throw new InvalidDataException("Installed updater payload identity is missing or invalid.");
            string[] required = { IndependentUpdateFiles.RunnerName, "ReadBoard.Updater.dll", "ReadBoard.Updater.deps.json",
                "ReadBoard.Updater.runtimeconfig.json", "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll",
                "System.Windows.Forms.dll", "language_cn.txt", "language_en.txt", "language_jp.txt", "language_kr.txt" };
            if (required.Any(name => !payload.Files.ContainsKey("updater/" + name)))
                throw new InvalidDataException("Installed updater runtime is incomplete.");
            Directory.CreateDirectory(runnerDirectory);
            foreach (var file in payload.Files.Where(item => item.Key.StartsWith("updater/", StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                IndependentUpdateFiles.ValidateRelativePath(file.Key);
                string source = Path.Combine(install, file.Key.Replace('/', Path.DirectorySeparatorChar));
                string target = Path.Combine(runnerDirectory, file.Key.Substring(8).Replace('/', Path.DirectorySeparatorChar));
                IndependentUpdateFiles.AssertNoReparse(source);
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant() != file.Value)
                    throw new InvalidDataException("Installed updater dependency failed verification: " + file.Key);
                input.Position = 0;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, cancellationToken);
            }
            IndependentUpdateFiles.VerifyRuntimeDependencies(runnerDirectory, "ReadBoard.Updater");
        }

        public void CleanupPreparation()
        {
            string operation = preparedOperation;
            preparedOperation = null;
            preparedInstall = null;
            // The updater owns records from its first durable preparation snapshot.
            if (operation != null && !File.Exists(Path.Combine(operation, "preparation.json"))
                && !File.Exists(Path.Combine(operation, "progress.json")) && Directory.Exists(operation))
                Directory.Delete(operation, true);
        }

        public async Task CommitAsync(HostedUpdateRequest packageRequest, string packagePath,
            Action persistConfiguration, CancellationToken cancellationToken)
        {
            string install = preparedInstall ?? throw new InvalidOperationException("Updater has not been prepared.");
            string operation = preparedOperation;
            string operationId = Path.GetFileName(operation);
            string runnerDirectory = Path.Combine(operation, "runner");
            Process runner = null;
            bool accepted = false;
            IndependentUpdateAdoption adoption = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string ownedPackage = Path.Combine(operation, packageRequest.AssetName);
                File.Copy(packagePath, ownedPackage, false);
                using var sourceProcess = Process.GetCurrentProcess();
                var request = new IndependentUpdateRequest
                {
                    OperationId = operationId, SourceVersion = AppReleaseVersion.GetCurrentVersion(),
                    TargetTag = packageRequest.VersionTag, AssetName = packageRequest.AssetName,
                    PackagePath = ownedPackage, ExpectedSha256 = packageRequest.ExpectedSha256,
                    InstallDirectory = install, SourceProcessId = sourceProcess.Id,
                    SourceProcessStartTimeUtc = sourceProcess.StartTime.ToUniversalTime(), Language = language()
                };
                IndependentUpdateFiles.ValidateRequest(request);
                var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // WebView2's default application-owned profile is next to the executable.
                directories.Add("readboard.exe.WebView2");
                directories.Add("debug-diagnostics");
                foreach (string directory in userDirectories())
                {
                    if (!string.IsNullOrWhiteSpace(directory)
                        && string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), install, StringComparison.OrdinalIgnoreCase))
                    {
                        // RollingFileSink owns only these streams and archive/capture
                        // directories when logging is configured at the product root.
                        files.UnionWith(new[] { "app.log", "trace.log", "crash.log" });
                        directories.UnionWith(new[] { "archive", "capture" });
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(directory) || !IndependentUpdateFiles.IsWithin(install, directory)) continue;
                    string relative = Path.GetRelativePath(install, directory).Replace('\\', '/');
                    IndependentUpdateFiles.ValidateRelativePath(relative);
                    directories.Add(relative);
                }
                string[] preserved = directories.Where(path => !directories.Any(parent => path.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase))).ToArray();
                IndependentUpdateFiles.Publish(Path.Combine(operation, "data.json"), new IndependentUpdateData
                { OperationId = operationId, Directories = preserved, Files = files.ToArray() });
                string requestPath = Path.Combine(operation, "request.json");
                IndependentUpdateFiles.Publish(requestPath, request);
                adoption = restart == null ? new IndependentUpdateAdoption { OperationId = operationId }
                    : await restart.PrepareAsync(requestPath, cancellationToken);
                using var pipe = new NamedPipeServerStream("ReadBoard.Update." + operationId, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                runner = StartRunner(runnerDirectory, requestPath);
                using var preparationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                preparationTimeout.CancelAfter(TimeSpan.FromMinutes(2));
                await pipe.WaitForConnectionAsync(preparationTimeout.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                string ready = await reader.ReadLineAsync(preparationTimeout.Token);
                if (ready != "PREPARED " + operationId) throw new IOException(DecodeError(ready));
                cancellationToken.ThrowIfCancellationRequested();
                persistConfiguration();
                cancellationToken.ThrowIfCancellationRequested();
                IndependentUpdateFiles.Publish(Path.Combine(operation, "adoption.json"), adoption);
                await writer.WriteLineAsync("COMMIT " + operationId);
                // Once sent, resolve the updater's durable decision even when the user
                // cancels concurrently. Never use cancellation as evidence of rejection.
                string acknowledgement = await reader.ReadLineAsync();
                if (acknowledgement != "ACCEPTED " + operationId) throw new IOException(DecodeError(acknowledgement));
                var progress = IndependentUpdateFiles.Read<IndependentUpdateProgress>(Path.Combine(operation, "progress.json"));
                if (progress.SchemaVersion != 1 || progress.OperationId != operationId
                    || progress.RestartAdopted != (adoption.RegistrationId != null)
                    || progress.RestartRegistrationId != adoption.RegistrationId
                    || progress.HostProcessId != adoption.HostProcessId
                    || progress.HostProcessStartTimeUtc != adoption.HostProcessStartTimeUtc
                    || !new[] { "committed", "waitingForExit" }.Contains(progress.Phase))
                    throw new InvalidDataException("Updater acceptance record does not match this operation.");
                accepted = true;
            }
            finally
            {
                if (!accepted) restart?.Cancel(operationId);
                if (runner != null)
                {
                    if (!accepted) await runner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35));
                    runner.Dispose();
                }
                // A runner that has written progress owns these records and its private
                // package. Preserve them across disposal and precommit error reporting.
                if (!accepted) CleanupPreparation();
            }
        }

        internal static Process StartRunner(string runnerDirectory, string requestPath)
        {
            // The GUI updater communicates only through its private named pipe. Shell
            // launch does not inherit the hosted source's standard handles, so the host
            // reader can observe source EOF while the independent updater remains alive.
            var start = new ProcessStartInfo(Path.Combine(runnerDirectory, IndependentUpdateFiles.RunnerName))
            { UseShellExecute = true, WorkingDirectory = runnerDirectory };
            start.ArgumentList.Add("--request");
            start.ArgumentList.Add(requestPath);
            return Process.Start(start) ?? throw new IOException("Updater could not start.");
        }

        private static string DecodeError(string line)
        {
            if (line != null && line.StartsWith("ERROR ", StringComparison.Ordinal))
                return JsonSerializer.Deserialize<string>(line.Substring(6));
            return "Updater did not acknowledge the installation; the original application remains running.";
        }
    }
}
