using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using readboard;
using Readboard.VerificationTests.Support;
using Xunit;

namespace Readboard.VerificationTests.Protocol
{
    public sealed partial class AutoPlayFaultSequenceTests
    {
        private const string EofChildMarker = "READBOARD_ISSUE87_EOF_CHILD";

        [Fact]
        public async Task HostEof_WithPendingRecognition_StopsWithoutInvalidOutboundWrites()
        {
            if (System.Environment.GetEnvironmentVariable(EofChildMarker) == "1")
            {
                await RunRealHostEofSequence();
                return;
            }

            // An uncaught production worker exception must fail this case, not kill unrelated cases.
            string dotnet = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (string.IsNullOrEmpty(dotnet))
            {
                string root = System.Environment.GetEnvironmentVariable("DOTNET_ROOT");
                dotnet = string.IsNullOrEmpty(root) ? "dotnet.exe" : Path.Combine(root, "dotnet.exe");
            }
            string assembly = typeof(AutoPlayFaultSequenceTests).Assembly.Location;
            var start = new ProcessStartInfo(dotnet)
            {
                WorkingDirectory = Path.GetDirectoryName(assembly),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            start.Environment[EofChildMarker] = "1";
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(assembly);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(AutoPlayFaultSequenceTests).FullName
                + "." + nameof(HostEof_WithPendingRecognition_StopsWithoutInvalidOutboundWrites));
            start.ArgumentList.Add("/Logger:console;verbosity=detailed");
            using Process child = Process.Start(start);
            Task<string> stdout = child.StandardOutput.ReadToEndAsync();
            Task<string> stderr = child.StandardError.ReadToEndAsync();
            try
            {
                await VerificationCompletion.WaitAsync(child.WaitForExitAsync(), "Real EOF child did not terminate.");
            }
            finally
            {
                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync();
                }
                output.WriteLine(await stdout);
                output.WriteLine(await stderr);
            }
            Assert.True(child.ExitCode == 0,
                "Real host EOF sequence failed in the isolated child (exit " + child.ExitCode + "). See event/wire transcript above.");
        }

        private async Task RunRealHostEofSequence()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Task<TcpClient> accepted = listener.AcceptTcpClientAsync();
            var h = new AutoPlayFaultSequenceHarness(new TcpTransport(((IPEndPoint)listener.LocalEndpoint).Port));
            UnhandledExceptionEventHandler traceFailure = (_, args) =>
            {
                Console.Error.WriteLine("Real EOF unhandled exception: " + args.ExceptionObject);
                Console.Error.WriteLine(h.Transcript);
            };
            AppDomain.CurrentDomain.UnhandledException += traceFailure;
            try
            {
                using TcpClient peer = await VerificationCompletion.WaitAsync(accepted, "Loopback host did not accept the real transport.");
                using var reader = new StreamReader(peer.GetStream(), Encoding.UTF8);
                h.EnableAndStart();
                h.Samples.Release(1);
                h.Samples.Wait(2);
                Assert.Equal(new[] { "play>black>0 0 0" }, h.AutoPlayWire);

                h.Record("host half-closes send direction while recognition step2 is pending");
                peer.Client.Shutdown(SocketShutdown.Send);
                string line;
                while ((line = await VerificationCompletion.WaitAsync(reader.ReadLineAsync(), "Production EOF reader did not close the peer stream.")) != null)
                    h.Record("host received: " + line);
                h.WaitForTransportDisconnected();
                Assert.False(h.Coordinator.IsProtocolSessionActive);
                Assert.False(h.Coordinator.StartedSync);
                Assert.False(h.Coordinator.KeepSync);
                h.Record("host receive EOF confirms production reader processed actual EOF; started="
                    + h.Coordinator.StartedSync + " keep=" + h.Coordinator.KeepSync);
                string[] wireAtDisconnect = h.Wire;
                h.AtUi("request auto-play after disconnect", () => h.Runtime.RequestAutoPlay());

                h.Samples.Release(2);
                h.WaitForKeepSyncStopped();
                Assert.False(h.Coordinator.StartedSync);
                Assert.False(h.Coordinator.KeepSync);
                Assert.False(h.Environment.HasActiveSyncOperation);
                Assert.Equal(wireAtDisconnect, h.Wire);
                Assert.Equal(new[] { "play>black>0 0 0" }, h.AutoPlayWire);
            }
            finally
            {
                try { h.Dispose(); }
                finally
                {
                    AppDomain.CurrentDomain.UnhandledException -= traceFailure;
                    output.WriteLine(h.Transcript);
                }
            }
        }
    }
}
