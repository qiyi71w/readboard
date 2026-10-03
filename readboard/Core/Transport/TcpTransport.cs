using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace readboard
{
    internal sealed class TcpTransport : IReadBoardTransport
    {
        private const int ReaderBufferSize = 4096;

        private readonly int port;
        private readonly object syncRoot = new object();
        private TcpClient client;
        private NetworkStream stream;
        private Thread readThread;
        private volatile bool running;

        public TcpTransport(int port)
        {
            this.port = port;
        }

        public event EventHandler<string> MessageReceived;
        public event EventHandler Disconnected;

        public bool IsConnected
        {
            get
            {
                lock (syncRoot)
                {
                    return running && client != null && client.Connected && stream != null;
                }
            }
        }

        public void Start()
        {
            TcpClient tcpClient = new TcpClient("127.0.0.1", port);
            NetworkStream networkStream = tcpClient.GetStream();
            Thread thread;
            lock (syncRoot)
            {
                if (running)
                {
                    networkStream.Dispose();
                    tcpClient.Close();
                    throw new InvalidOperationException("TCP transport is already started.");
                }
                client = tcpClient;
                stream = networkStream;
                running = true;
                thread = CreateReadThread(tcpClient, networkStream);
                readThread = thread;
            }
            thread.Start();
        }

        public void Stop()
        {
            NetworkStream currentStream;
            TcpClient currentClient;
            Thread currentThread;
            lock (syncRoot)
            {
                running = false;
                currentStream = stream;
                currentClient = client;
                currentThread = readThread;
                stream = null;
                client = null;
                readThread = null;
            }
            if (currentStream != null)
                currentStream.Dispose();
            if (currentClient != null)
                currentClient.Close();
            TryJoinReadThread(currentThread);
        }

        public void Send(string line)
        {
            WriteLine(line);
        }

        public void SendError(string message)
        {
            WriteLine("error: " + message);
        }

        public void Dispose()
        {
            Stop();
        }

        private void ReadLoop(TcpClient activeClient, NetworkStream activeStream)
        {
            StreamReader reader;
            try
            {
                reader = new StreamReader(activeStream, Encoding.UTF8, false, ReaderBufferSize, leaveOpen: true);
            }
            catch (IOException)
            {
                TryTransitionDisconnected(activeClient, activeStream);
                return;
            }
            catch (SocketException)
            {
                TryTransitionDisconnected(activeClient, activeStream);
                return;
            }
            catch (ObjectDisposedException)
            {
                TryTransitionDisconnected(activeClient, activeStream);
                return;
            }

            try
            {
                ReadMessages(reader, activeClient, activeStream);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private void ReadMessages(StreamReader reader, TcpClient activeClient, NetworkStream activeStream)
        {
            while (running)
            {
                string line;
                try
                {
                    line = reader.ReadLine();
                }
                catch (IOException)
                {
                    TryTransitionDisconnected(activeClient, activeStream);
                    return;
                }
                catch (SocketException)
                {
                    TryTransitionDisconnected(activeClient, activeStream);
                    return;
                }
                catch (ObjectDisposedException)
                {
                    TryTransitionDisconnected(activeClient, activeStream);
                    return;
                }

                if (line == null)
                {
                    TryTransitionDisconnected(activeClient, activeStream);
                    return;
                }

                if (line.Length > 0)
                {
                    EventHandler<string> handler = MessageReceived;
                    if (handler != null)
                        handler(this, line);
                }
            }
        }

        private void WriteLine(string line)
        {
            TcpClient currentClient;
            NetworkStream currentStream;
            lock (syncRoot)
            {
                if (!running || stream == null)
                    return;
                currentClient = client;
                currentStream = stream;
            }

            byte[] buffer = Encoding.UTF8.GetBytes(line + "\r\n");
            try
            {
                currentStream.Write(buffer, 0, buffer.Length);
            }
            catch (IOException)
            {
                TryTransitionDisconnected(currentClient, currentStream);
            }
            catch (SocketException)
            {
                TryTransitionDisconnected(currentClient, currentStream);
            }
            catch (ObjectDisposedException)
            {
                TryTransitionDisconnected(currentClient, currentStream);
            }
        }

        private void TryTransitionDisconnected(TcpClient activeClient, NetworkStream activeStream)
        {
            NetworkStream streamToDispose = null;
            TcpClient clientToClose = null;
            bool shouldRaise = false;
            EventHandler handler = null;
            lock (syncRoot)
            {
                if (running && client == activeClient && stream == activeStream)
                {
                    running = false;
                    streamToDispose = stream;
                    clientToClose = client;
                    stream = null;
                    client = null;
                    readThread = null;
                    shouldRaise = true;
                    handler = Disconnected;
                }
            }

            if (!shouldRaise)
                return;

            if (streamToDispose != null)
                streamToDispose.Dispose();
            if (clientToClose != null)
                clientToClose.Close();

            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private Thread CreateReadThread(TcpClient activeClient, NetworkStream activeStream)
        {
            Thread thread = new Thread(() => ReadLoop(activeClient, activeStream));
            thread.IsBackground = true;
            return thread;
        }
        private static void TryJoinReadThread(Thread thread)
        {
            if (thread == null || Thread.CurrentThread == thread)
                return;
            thread.Join(0);
        }
    }
}
