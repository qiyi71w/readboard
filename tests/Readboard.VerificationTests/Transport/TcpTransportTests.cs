using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using readboard;

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
