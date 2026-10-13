using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using readboard;

// Used only by explicitly isolated native acceptance runs. This test assembly is
// never copied into a product package. The hook supplies release metadata and the
// transport bytes; production hashing, ZIP validation, UI confirmation, config
// persistence, updater IPC, process exit and installation remain unchanged.
public static class StartupHook
{
    public static void Initialize()
    {
        string configuration = Environment.GetEnvironmentVariable("READBOARD_UPDATE_ACCEPTANCE");
        if (string.IsNullOrEmpty(configuration)
            || !string.Equals(Path.GetFileName(Environment.ProcessPath), "readboard.exe", StringComparison.OrdinalIgnoreCase)) return;
        IndependentUpdateNativeAcceptance.Arm(configuration);
    }
}

public static class IndependentUpdateNativeAcceptance
{
    public static void Arm(string configuration)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(configuration));
        string install = document.RootElement.GetProperty("installDirectory").GetString();
        string package = document.RootElement.GetProperty("packagePath").GetString();
        string tag = document.RootElement.GetProperty("targetTag").GetString();
        string sha = document.RootElement.GetProperty("sha256").GetString();
        string expectedSource = document.RootElement.GetProperty("sourceVersion").GetString();
        string evidence = document.RootElement.GetProperty("evidenceDirectory").GetString();
        if (!string.Equals(Path.GetDirectoryName(Environment.ProcessPath), install, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Acceptance hook is bound to another executable directory.");
        EventHandler handler = null;
        handler = (_, _) =>
        {
            MainForm form = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (form == null || !form.IsHandleCreated) return;
            Application.Idle -= handler;
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, "loaded-" + Environment.ProcessId + ".json"), JsonSerializer.Serialize(new
            {
                pid = Environment.ProcessId,
                creation = Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                version = AppReleaseVersion.GetCurrentVersion(),
                language = Program.language,
                preferences = Program.CurrentConfig
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (AppReleaseVersion.GetCurrentVersion().TrimStart('v') != expectedSource.TrimStart('v')) return;
            var downloader = new HostedUpdatePackageDownloader(Path.Combine(evidence, "downloads"), async (_, destination, token) =>
            {
                using var source = File.OpenRead(package);
                using var output = File.Create(destination);
                await source.CopyToAsync(output, token);
            });
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var observe = (Action<IndependentUpdateObservation>)Delegate.CreateDelegate(typeof(Action<IndependentUpdateObservation>), form,
                typeof(MainForm).GetMethod("OnIndependentUpdateObservation", flags));
            var journey = new IndependentUpdateJourney(downloader, new HostedUpdatePackageVerifier(),
                new IndependentUpdateCommitter(() => Program.language, () => new[] { Program.CurrentContext.Logging?.LogRoot }),
                form.PersistConfiguration, observe);
            typeof(MainForm).GetField("independentUpdateJourney", flags).SetValue(form, journey);
            string asset = Path.GetFileName(package);
            string manifest = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                channels = new[] { new { id = "main", status = "active", minimumWindowsVersion = "10.0.17763", latestTag = tag, assetName = asset, sha256 = sha } }
            });
            string release = JsonSerializer.Serialize(new
            {
                tag_name = tag, draft = false, prerelease = false,
                name = "ReadBoard " + tag, body = "Controlled local release for native acceptance.",
                html_url = "https://github.com/qiyi71w/readboard/releases/tag/" + tag,
                published_at = "2026-10-13T00:00:00Z",
                assets = new[] { new { name = asset, browser_download_url = "https://acceptance.invalid/" + asset, size = new FileInfo(package).Length } }
            });
            var checker = new GitHubUpdateChecker(AppReleaseVersion.GetCurrentVersion, () => Environment.OSVersion.Version,
                _ => Task.FromResult(manifest), (_, _) => Task.FromResult(release));
            typeof(MainForm).GetField("updateChecker", flags).SetValue(form, checker);
        };
        Application.Idle += handler;
    }

    // Deterministic filesystem fault seam; the source remains a real ReadBoard
    // process and the package/updater/runtime validation uses the product code.
    public static string ExecuteTransaction(string requestPath, string fault)
    {
        IndependentUpdateRequest request = IndependentUpdateFiles.Read<IndependentUpdateRequest>(requestPath);
        int moves = 0;
        Action<string, string> move = (source, target) =>
        {
            moves++;
            if ((fault == "rollback" || fault == "rollbackFailed") && moves == 2)
                throw new IOException("Acceptance: replacement move denied.");
            if (fault == "rollbackFailed" && moves == 3)
                throw new IOException("Acceptance: rollback move denied.");
            Directory.Move(source, target);
        };
        using FileStream locked = fault == "lock"
            ? new FileStream(Path.Combine(request.InstallDirectory, "lock-sentinel.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            : null;
        var installer = new IndependentUpdateInstaller(move);
        IndependentUpdateResult result = installer.Execute(requestPath, () => true, () =>
        {
            using Process source = Process.GetProcessById(request.SourceProcessId);
            if (source.StartTime.ToUniversalTime() != request.SourceProcessStartTimeUtc || !source.CloseMainWindow())
                throw new IOException("Acceptance source window could not close normally.");
        }, _ => { });
        return JsonSerializer.Serialize(result, IndependentUpdateFiles.JsonOptions);
    }

    public static void ValidatePayload(string directory, string tag) => IndependentUpdateFiles.VerifyPayload(directory, tag);
}
