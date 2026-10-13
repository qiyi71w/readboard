using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace readboard
{
    internal sealed class IndependentUpdateRequest
    {
        public int SchemaVersion { get; set; } = 1;
        public string OperationId { get; set; }
        public string SourceVersion { get; set; }
        public string TargetTag { get; set; }
        public string AssetName { get; set; }
        public string PackagePath { get; set; }
        public string ExpectedSha256 { get; set; }
        public string InstallDirectory { get; set; }
        public int SourceProcessId { get; set; }
        public DateTime SourceProcessStartTimeUtc { get; set; }
        public string Language { get; set; }
    }

    internal sealed class IndependentUpdateProgress
    {
        public int SchemaVersion { get; set; } = 1;
        public string OperationId { get; set; }
        public string Phase { get; set; }
        public bool RestartAdopted { get; set; }
    }

    internal sealed class IndependentUpdateResult
    {
        public int SchemaVersion { get; set; } = 1;
        public string OperationId { get; set; }
        public string State { get; set; }
        public string ActualVersion { get; set; }
        public string InstallDirectory { get; set; }
        public string Diagnostic { get; set; }
        public string BackupDirectory { get; set; }
    }

    internal sealed class IndependentUpdateActive
    {
        public int SchemaVersion { get; set; } = 1;
        public string OperationId { get; set; }
    }

    internal sealed class IndependentUpdateData
    {
        public int SchemaVersion { get; set; } = 1;
        public string OperationId { get; set; }
        public string[] Directories { get; set; } = Array.Empty<string>();
        public string[] Files { get; set; } = Array.Empty<string>();
    }

    internal sealed class IndependentUpdatePayload
    {
        public int SchemaVersion { get; set; } = 1;
        public string Version { get; set; }
        public Dictionary<string, string> Files { get; set; }
    }

    internal static class IndependentUpdateFiles
    {
        internal const string RunnerName = "ReadBoard.Updater.exe";
        internal const string ManifestName = "update-payload.json";
        internal static readonly string[] ConfigurationFiles =
        {
            "config.readboard.json", "config_readboard.txt", "config_readboard_others.txt"
        };
        internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true
        };

        internal static void ValidateRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Contains('\\') || path.StartsWith('/') || path.EndsWith('/'))
                throw new InvalidDataException("Unsafe relative path: " + path);
            foreach (string part in path.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(' ') || part.EndsWith('.')
                    || part.Any(c => c < 32 || "<>:\"|?*~".Contains(c))
                    || Regex.IsMatch(part.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])$", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("Unsafe Windows path: " + path);
            }
        }

        internal static string CanonicalDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
                || path.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0 || path.StartsWith(@"\\"))
                throw new InvalidDataException("A local absolute directory is required.");
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (full == Path.GetPathRoot(full)) throw new InvalidDataException("An installation cannot be a volume root.");
            string relative = full.Substring(Path.GetPathRoot(full).Length).Replace('\\', '/');
            ValidateRelativePath(relative);
            AssertNoReparse(full);
            return full;
        }

        internal static bool IsWithin(string root, string path)
        {
            return Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
                + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        internal static void AssertNoReparse(string path)
        {
            string current = Path.GetFullPath(path);
            while (current != null)
            {
                if ((File.Exists(current) || Directory.Exists(current))
                    && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse points are not allowed: " + current);
                current = Path.GetDirectoryName(current);
            }
        }

        internal static string InstallKey(string install)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalDirectory(install).ToUpperInvariant()))).ToLowerInvariant();
        }

        internal static string RecordRoot(string install)
        {
            string canonical = CanonicalDirectory(install);
            return Path.Combine(Path.GetDirectoryName(canonical), ".readboard-update-" + InstallKey(canonical));
        }

        internal static string OperationDirectory(string install, string operationId)
        {
            ValidateOperationId(operationId);
            return Path.Combine(RecordRoot(install), operationId);
        }

        internal static void ValidateOperationId(string id)
        {
            if (id == null || !Regex.IsMatch(id, "^[0-9a-f]{32}$")) throw new InvalidDataException("Invalid update operation ID.");
        }

        internal static void ValidateRequest(IndependentUpdateRequest request)
        {
            if (request == null || request.SchemaVersion != 1) throw new InvalidDataException("Unsupported update request.");
            ValidateOperationId(request.OperationId);
            if (!Regex.IsMatch(request.TargetTag ?? "", @"^v[0-9]+\.[0-9]+\.[0-9]+$")
                || !Regex.IsMatch(request.SourceVersion ?? "", @"^v?[0-9]+\.[0-9]+\.[0-9]+$")
                || request.AssetName != "readboard-webview2-" + request.TargetTag + ".zip"
                || !Regex.IsMatch(request.ExpectedSha256 ?? "", "^[0-9a-f]{64}$")
                || request.SourceProcessId <= 0 || request.SourceProcessStartTimeUtc.Kind != DateTimeKind.Utc
                || request.SourceProcessStartTimeUtc == DateTime.MinValue
                || !new[] { "cn", "en", "jp", "kr" }.Contains(request.Language))
                throw new InvalidDataException("Invalid update request identity.");
            string install = CanonicalDirectory(request.InstallDirectory);
            if (!string.Equals(install, request.InstallDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installation path is not canonical.");
            string package = Path.GetFullPath(request.PackagePath);
            if (!Path.IsPathFullyQualified(request.PackagePath) || Path.GetFileName(package) != request.AssetName
                || IsWithin(install, package)) throw new InvalidDataException("Invalid package location.");
            AssertNoReparse(package);
        }

        internal static T Read<T>(string path)
        {
            AssertNoReparse(path);
            byte[] bytes = File.ReadAllBytes(path);
            using (JsonDocument document = JsonDocument.Parse(bytes))
            {
                ValidateJsonObject(document.RootElement);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Update record must be a JSON object.");
                foreach (var property in typeof(T).GetProperties())
                    if (!document.RootElement.TryGetProperty(JsonNamingPolicy.CamelCase.ConvertName(property.Name), out _))
                        throw new InvalidDataException("Missing update record field: " + property.Name);
            }
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty update record.");
        }

        private static void ValidateJsonObject(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON field: " + property.Name);
                    ValidateJsonObject(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (JsonElement item in element.EnumerateArray()) ValidateJsonObject(item);
        }

        internal static void Publish<T>(string path, T record)
        {
            AssertNoReparse(path);
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, record, JsonOptions);
                    stream.Flush(true);
                }
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static void AssertLaunchable(string install, string preparingOperationId = null)
        {
            string root = RecordRoot(install);
            AssertNoReparse(root);
            string activePath = Path.Combine(root, "active.json");
            if (Directory.Exists(root))
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(root))
                {
                    if (entry == activePath || Path.GetFileName(entry) == preparingOperationId) continue;
                    ValidateOperationId(Path.GetFileName(entry));
                    var prior = Read<IndependentUpdateResult>(Path.Combine(entry, "result.json"));
                    if (prior.SchemaVersion != 1 || prior.OperationId != Path.GetFileName(entry)
                        || !new[] { "installed", "failedUnchanged", "rolledBack", "cancelled" }.Contains(prior.State))
                        throw new InvalidDataException("Unsettled update transaction: " + entry);
                }
            }
            if (!File.Exists(activePath))
            {
                if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any(entry => Path.GetFileName(entry) != preparingOperationId))
                    throw new InvalidDataException("Unsettled update records require recovery: " + root);
                return;
            }
            var active = Read<IndependentUpdateActive>(activePath);
            if (active.SchemaVersion != 1) throw new InvalidDataException("Unknown update transaction.");
            string directory = OperationDirectory(install, active.OperationId);
            var request = Read<IndependentUpdateRequest>(Path.Combine(directory, "request.json"));
            ValidateRequest(request);
            if (request.OperationId != active.OperationId || !string.Equals(request.InstallDirectory, CanonicalDirectory(install), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update request does not belong to this installation.");
            var result = Read<IndependentUpdateResult>(Path.Combine(directory, "result.json"));
            if (result.SchemaVersion != 1 || result.OperationId != active.OperationId
                || result.InstallDirectory != request.InstallDirectory
                || !new[] { "installed", "failedUnchanged", "rolledBack", "cancelled" }.Contains(result.State)
                || result.ActualVersion == null || ReadVersion(install) != result.ActualVersion)
                throw new InvalidDataException("Update result is not launchable; recovery required: " + directory);
        }

        internal static string ReadVersion(string directory)
        {
            string dll = Path.Combine(directory, "readboard.dll");
            if (!File.Exists(dll)) return null;
            string version = FileVersionInfo.GetVersionInfo(dll).ProductVersion?.Split('+')[0];
            return Regex.IsMatch(version ?? "", @"^v?[0-9]+\.[0-9]+\.[0-9]+$") ? "v" + version.TrimStart('v') : null;
        }

        internal static void CopyTree(string source, string target)
        {
            AssertNoReparse(source);
            AssertNoReparse(target);
            Directory.CreateDirectory(target);
            foreach (string entry in Directory.EnumerateFileSystemEntries(source))
            {
                AssertNoReparse(entry);
                string destination = Path.Combine(target, Path.GetFileName(entry));
                if (Directory.Exists(entry)) CopyTree(entry, destination);
                else File.Copy(entry, destination, false);
            }
        }

        internal static void ProbeWritable(string directory)
        {
            AssertNoReparse(directory);
            string probe = Path.Combine(directory, ".readboard-probe-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        }

        internal static void VerifyPayload(string directory, string targetTag)
        {
            var manifest = Read<IndependentUpdatePayload>(Path.Combine(directory, ManifestName));
            if (manifest.SchemaVersion != 1 || manifest.Version != targetTag || manifest.Files == null || manifest.Files.Count == 0)
                throw new InvalidDataException("Invalid release payload identity.");
            string[] required = { "readboard.exe", "readboard.dll", "readboard.deps.json", "readboard.runtimeconfig.json",
                "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll", "WebView/index.html", "WebView/app.js", "WebView/styles.css",
                "language_cn.txt", "language_en.txt", "language_jp.txt", "language_kr.txt", "updater/" + RunnerName,
                "updater/ReadBoard.Updater.dll", "updater/ReadBoard.Updater.deps.json", "updater/ReadBoard.Updater.runtimeconfig.json",
                "updater/coreclr.dll", "updater/hostfxr.dll", "updater/hostpolicy.dll", "updater/System.Private.CoreLib.dll", "updater/System.Windows.Forms.dll" };
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in manifest.Files)
            {
                ValidateRelativePath(item.Key);
                if (!paths.Add(item.Key) || !Regex.IsMatch(item.Value ?? "", "^[0-9a-f]{64}$"))
                    throw new InvalidDataException("Invalid payload file identity.");
                string path = Path.Combine(directory, item.Key.Replace('/', Path.DirectorySeparatorChar));
                AssertNoReparse(path);
                using var stream = File.OpenRead(path);
                if (Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() != item.Value)
                    throw new InvalidDataException("Release payload hash mismatch: " + item.Key);
            }
            if (required.Any(name => !paths.Contains(name)) || ReadVersion(directory) != targetTag)
                throw new InvalidDataException("Release payload is incomplete or has another version.");
            VerifyRuntimeDependencies(directory, "readboard");
            VerifyRuntimeDependencies(Path.Combine(directory, "updater"), "ReadBoard.Updater");
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                AssertNoReparse(file);
                string relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
                if (relative != ManifestName && !paths.Contains(relative))
                    throw new InvalidDataException("Unlisted release file: " + relative);
            }
        }

        internal static void VerifyRuntimeDependencies(string directory, string assemblyName)
        {
            using var runtime = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, assemblyName + ".runtimeconfig.json")));
            JsonElement options = runtime.RootElement.GetProperty("runtimeOptions");
            if (options.TryGetProperty("framework", out _) || options.TryGetProperty("frameworks", out _)
                || !options.TryGetProperty("includedFrameworks", out JsonElement included)
                || !included.EnumerateArray().Any(item => item.GetProperty("name").GetString() == "Microsoft.NETCore.App")
                || !included.EnumerateArray().Any(item => item.GetProperty("name").GetString() == "Microsoft.WindowsDesktop.App"))
                throw new InvalidDataException("The release runtime must be self-contained.");
            using var dependencies = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, assemblyName + ".deps.json")));
            string target = dependencies.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString();
            if (target == null || !target.EndsWith("/win-x64", StringComparison.Ordinal))
                throw new InvalidDataException("The release runtime must target win-x64.");
            JsonElement packages = dependencies.RootElement.GetProperty("targets").GetProperty(target);
            foreach (JsonProperty package in packages.EnumerateObject())
            {
                foreach (string group in new[] { "runtime", "native", "resources" })
                {
                    if (!package.Value.TryGetProperty(group, out JsonElement assets)) continue;
                    foreach (JsonProperty asset in assets.EnumerateObject())
                    {
                        ValidateRelativePath(asset.Name);
                        string relative = group == "resources"
                            ? asset.Value.GetProperty("locale").GetString() + "/" + Path.GetFileName(asset.Name)
                            : Path.GetFileName(asset.Name);
                        ValidateRelativePath(relative);
                        string path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
                        AssertNoReparse(path);
                        if (!File.Exists(path)) throw new InvalidDataException("Missing runtime dependency: " + relative);
                    }
                }
            }
        }
    }
}
