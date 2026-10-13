using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host;

// This executable entrypoint is used only by the process regression below; the
// ordinary xUnit runner still loads this assembly through testhost.
internal static class UpdaterPipeProcessFixture
{
    internal record Identity(int Pid, DateTime Start, string Desktop);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, int size, out int needed);
    internal static string Desktop()
    {
        var name = new StringBuilder(256);
        if (!GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, name.Capacity * 2, out _))
            throw new System.ComponentModel.Win32Exception();
        return name.ToString();
    }
    public static int Main(string[] args)
    {
        if (args.Length != 2) return 2;
        string root = args[1];
        if (args[0] == "--request")
        {
            using var current = Process.GetCurrentProcess();
            File.WriteAllText(Path.Combine(root, "child.json"), JsonSerializer.Serialize(new Identity(current.Id, current.StartTime.ToUniversalTime(), Desktop())));
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!File.Exists(Path.Combine(root, "stop")) && DateTime.UtcNow < deadline) Thread.Sleep(20);
            return File.Exists(Path.Combine(root, "stop")) ? 0 : 3;
        }
        if (args[0] != "--parent") return 2;
        using var runner = IndependentUpdateCommitter.StartRunner(AppContext.BaseDirectory, root);
        // A hosted source stays alive while its stdin is open; its independent
        // updater must not keep the host's stdout reader alive after source exit.
        return Console.ReadLine() == "exit" ? 0 : 4;
    }
}

public sealed class IndependentUpdateProcessTests
{
    [Fact]
    public async Task RunnerOutlivesSourceWithoutRetainingHostStandardPipes()
    {
        string root = Path.Combine(Path.GetTempPath(), "readboard-pipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string executable = Path.ChangeExtension(typeof(UpdaterPipeProcessFixture).Assembly.Location, ".exe");
        string runner = Path.Combine(AppContext.BaseDirectory, IndependentUpdateFiles.RunnerName);
        Process child = null;
        Process parent = null;
        try
        {
            // Renamed apphost still loads this test assembly beside it. No updater
            // handshake or installer outcome is simulated by this pipe-only fixture.
            File.Copy(executable, runner, false);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("--parent"); start.ArgumentList.Add(root);
            parent = Process.Start(start);
            Task<string> stdout = parent.StandardOutput.ReadToEndAsync();
            Task<string> stderr = parent.StandardError.ReadToEndAsync();
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!File.Exists(Path.Combine(root, "child.json")) && DateTime.UtcNow < deadline) await Task.Delay(20);
            var identity = JsonSerializer.Deserialize<UpdaterPipeProcessFixture.Identity>(File.ReadAllText(Path.Combine(root, "child.json")));
            child = Process.GetProcessById(identity.Pid);
            Assert.Equal(identity.Start, child.StartTime.ToUniversalTime());
            Assert.Equal(UpdaterPipeProcessFixture.Desktop(), identity.Desktop);
            Assert.False(parent.HasExited);
            Assert.False(stdout.IsCompleted);
            await parent.StandardInput.WriteLineAsync("exit"); await parent.StandardInput.FlushAsync();
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, parent.ExitCode);
            Assert.Equal(string.Empty, await stdout.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(string.Empty, await stderr.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(child.HasExited);
        }
        finally
        {
            File.WriteAllText(Path.Combine(root, "stop"), string.Empty);
            if (child != null) { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); child.Dispose(); }
            if (parent != null) { if (!parent.HasExited) { parent.StandardInput.Close(); await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); } parent.Dispose(); }
            File.Delete(runner);
            Directory.Delete(root, true);
        }
    }
}
