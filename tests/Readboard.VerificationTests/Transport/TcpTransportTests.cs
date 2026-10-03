using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using Xunit;
using readboard;
using Readboard.VerificationTests.Support;

namespace Readboard.VerificationTests.Transport
{
    public sealed class TcpTransportTests
    {
        [Fact]
        public async Task Start_ExchangesInboundAndOutboundLines()
        {
            using LoopbackServer server = await LoopbackServer.StartAsync();
            using TcpTransport transport = new TcpTransport(server.Port);
            List<string> messages = new List<string>();
            TaskCompletionSource<string> received = new TaskCompletionSource<string>();
            transport.MessageReceived += (_, line) =>
            {
                messages.Add(line);
                received.TrySetResult(line);
            };

            transport.Start();
            await VerificationCompletion.WaitAsync(
                server.WaitForClientAsync(),
                "TCP client did not connect.");
            await VerificationCompletion.WaitAsync(
                server.WriteLineAsync("place 3 4"),
                "TCP server could not send the inbound line.");
            Assert.Equal(
                "place 3 4",
                await VerificationCompletion.WaitAsync(
                    received.Task,
                    "TCP transport did not receive the inbound line."));

            transport.Send("ready");
            transport.SendError("boom");

            Assert.Equal(
                "ready",
                await VerificationCompletion.WaitAsync(
                    server.ReadLineAsync(),
                    "TCP server did not receive the outbound line."));
            Assert.Equal(
                "error: boom",
                await VerificationCompletion.WaitAsync(
                    server.ReadLineAsync(),
                    "TCP server did not receive the outbound error."));
            Assert.True(transport.IsConnected);

            transport.Stop();

            Assert.False(transport.IsConnected);
            Assert.Equal(new[] { "place 3 4" }, messages);
        }

        [Fact]
        public void Stop_DoesNotBlockOnReadThreadJoinDuringShutdown()
        {
            using TcpTransport transport = new TcpTransport(9527);
            using BlockingBackgroundThreadHarness harness = BlockingBackgroundThreadHarness.Start("TcpTransportReadThread");
            SetPrivateField(transport, "readThread", harness.Thread);

            AssertTransportStopReturnsWithoutWaiting(transport, harness);
        }
        [Fact]
        public async Task HostEof_RaisesDisconnectedExactlyOnce_AndInvalidatesConnection()
        {
            using LoopbackServer server = await LoopbackServer.StartAsync();
            using TcpTransport transport = new TcpTransport(server.Port);

            int disconnectedCount = 0;
            bool isConnectedDuringCallback = true;
            TaskCompletionSource<bool> disconnectedSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            transport.Disconnected += (_, _) =>
            {
                Interlocked.Increment(ref disconnectedCount);
                isConnectedDuringCallback = transport.IsConnected;
                disconnectedSignal.TrySetResult(true);
            };

            transport.Start();
            await VerificationCompletion.WaitAsync(
                server.WaitForClientAsync(),
                "TCP client did not connect.");
            Assert.True(transport.IsConnected);

            server.ShutdownSend();

            await VerificationCompletion.WaitAsync(
                disconnectedSignal.Task,
                "Disconnected event was not raised on host EOF.");

            Assert.Equal(1, disconnectedCount);
            Assert.False(isConnectedDuringCallback);
            Assert.False(transport.IsConnected);

            transport.Send("stale outbound after EOF");
            transport.SendError("stale error after EOF");

            string line = await VerificationCompletion.WaitAsync(
                server.ReadLineOrNullAsync(),
                "Server did not receive peer EOF.");
            Assert.Null(line);

            transport.Stop();
            transport.Dispose();
            Assert.Equal(1, disconnectedCount);
        }

        [Fact]
        public async Task ExplicitStopAndDispose_DoNotRaiseDisconnectedEvent()
        {
            using LoopbackServer server = await LoopbackServer.StartAsync();
            using TcpTransport transport = new TcpTransport(server.Port);

            int disconnectedCount = 0;
            transport.Disconnected += (_, _) =>
            {
                Interlocked.Increment(ref disconnectedCount);
            };

            transport.Start();
            await VerificationCompletion.WaitAsync(
                server.WaitForClientAsync(),
                "TCP client did not connect.");
            Assert.True(transport.IsConnected);

            transport.Stop();
            transport.Dispose();

            Assert.False(transport.IsConnected);
            Assert.Equal(0, disconnectedCount);
        }

        [Fact]
        public async Task PeerReset_TriggersDisconnectedAndPreventsStaleWrites()
        {
            using LoopbackServer server = await LoopbackServer.StartAsync();
            using TcpTransport transport = new TcpTransport(server.Port);

            int disconnectedCount = 0;
            bool isConnectedDuringCallback = true;
            TaskCompletionSource<bool> disconnectedSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            transport.Disconnected += (_, _) =>
            {
                Interlocked.Increment(ref disconnectedCount);
                isConnectedDuringCallback = transport.IsConnected;
                disconnectedSignal.TrySetResult(true);
            };

            transport.Start();
            await VerificationCompletion.WaitAsync(
                server.WaitForClientAsync(),
                "TCP client did not connect.");
            Assert.True(transport.IsConnected);

            server.Reset();

            transport.Send("trigger write failure on reset peer");

            await VerificationCompletion.WaitAsync(
                disconnectedSignal.Task,
                "Disconnected event was not raised on connection reset / write failure.");

            Assert.Equal(1, disconnectedCount);
            Assert.False(isConnectedDuringCallback);
            Assert.False(transport.IsConnected);

            transport.Send("subsequent send after reset");
            transport.Stop();
            Assert.Equal(1, disconnectedCount);
        }

        [Fact]
        public async Task StaleReaderFromPreviousConnection_DoesNotDisconnectNewConnection()
        {
            using LoopbackServer server = await LoopbackServer.StartAsync();
            using TcpTransport transport = new TcpTransport(server.Port);

            int disconnectedCount = 0;
            transport.Disconnected += (_, _) =>
            {
                Interlocked.Increment(ref disconnectedCount);
            };

            transport.Start();
            await VerificationCompletion.WaitAsync(
                server.WaitForClientAsync(),
                "TCP client did not connect.");

            TcpClient client1 = (TcpClient)typeof(TcpTransport).GetField("client", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(transport);
            NetworkStream stream1 = (NetworkStream)typeof(TcpTransport).GetField("stream", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(transport);

            transport.Stop();

            transport.Start();
            await VerificationCompletion.WaitAsync(
                server.WaitForClientAsync(),
                "TCP client did not reconnect.");
            Assert.True(transport.IsConnected);

            MethodInfo method = typeof(TcpTransport).GetMethod("TryTransitionDisconnected", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(transport, new object[] { client1, stream1 });

            Assert.True(transport.IsConnected);
            Assert.Equal(0, disconnectedCount);

            transport.Stop();
        }


        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(target, value);
        }

        private static void AssertTransportStopReturnsWithoutWaiting(
            TcpTransport transport,
            BlockingBackgroundThreadHarness harness)
        {
            ManualResetEventSlim stopCompleted = new ManualResetEventSlim(false);
            Exception stopException = null;
            Thread stopThread = new Thread(new ThreadStart(delegate
            {
                try
                {
                    transport.Stop();
                }
                catch (Exception ex)
                {
                    stopException = ex;
                }
                finally
                {
                    stopCompleted.Set();
                }
            }));
            stopThread.IsBackground = true;
            stopThread.Name = "TcpTransportTests.Stop";
            stopThread.Start();

            try
            {
                VerificationCompletion.Wait(
                    stopCompleted,
                    "TcpTransport.Stop must return without joining a blocked read thread.");
                Assert.Null(stopException);
                Assert.True(harness.Thread.IsAlive);
            }
            finally
            {
                harness.Release();
                VerificationCompletion.Wait(
                    stopCompleted,
                    "TcpTransport.Stop did not finish after the read thread was released.");
                VerificationCompletion.Join(
                    stopThread,
                    "TcpTransport.Stop worker did not exit.");
                stopCompleted.Dispose();
            }
        }

        private sealed class LoopbackServer : IDisposable
        {
            private readonly TcpListener listener;
            private Task<TcpClient> acceptTask;
            private TcpClient client;
            private StreamReader reader;
            private StreamWriter writer;

            private LoopbackServer(TcpListener listener)
            {
                this.listener = listener;
                acceptTask = listener.AcceptTcpClientAsync();
            }

            public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

            public static Task<LoopbackServer> StartAsync()
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                return Task.FromResult(new LoopbackServer(listener));
            }

            public async Task WaitForClientAsync()
            {
                client = await acceptTask;
                NetworkStream stream = client.GetStream();
                reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
                writer = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\r\n"
                };
                acceptTask = listener.AcceptTcpClientAsync();
            }

            public void ShutdownSend()
            {
                client?.Client.Shutdown(SocketShutdown.Send);
            }

            public void Reset()
            {
                if (client != null)
                {
                    client.Client.LingerState = new LingerOption(true, 0);
                    client.Client.Close();
                }
            }

            public async Task<string> ReadLineOrNullAsync()
            {
                if (reader == null)
                    return null;
                return await reader.ReadLineAsync();
            }

            public async Task<string> ReadLineAsync()
            {
                string line = await reader.ReadLineAsync();
                return line ?? string.Empty;
            }

            public Task WriteLineAsync(string line)
            {
                return writer.WriteLineAsync(line);
            }

            public void Dispose()
            {
                writer?.Dispose();
                reader?.Dispose();
                client?.Dispose();
                listener.Stop();
            }
        }
    }
}
