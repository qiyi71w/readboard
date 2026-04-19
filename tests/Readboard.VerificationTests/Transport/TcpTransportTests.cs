using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
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
            await server.WaitForClientAsync();
            await server.WriteLineAsync("place 3 4");
            Assert.Equal("place 3 4", await received.Task.WaitAsync(TimeSpan.FromSeconds(2)));

            transport.Send("ready");
            transport.SendError("boom");

            Assert.Equal("ready", await server.ReadLineAsync());
            Assert.Equal("error: boom", await server.ReadLineAsync());
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

            Stopwatch stopwatch = Stopwatch.StartNew();
            transport.Stop();
            stopwatch.Stop();

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromMilliseconds(250),
                "TCP transport shutdown should not wait for the read thread to finish.");
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(target, value);
        }

        private sealed class LoopbackServer : IDisposable
        {
            private readonly TcpListener listener;
            private readonly Task<TcpClient> acceptTask;
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
