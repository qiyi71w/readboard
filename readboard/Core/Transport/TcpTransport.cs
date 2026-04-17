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
        private const int ReadThreadJoinTimeoutMs = 2000;

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

        public bool IsConnected
        {
            get
            {
                lock (syncRoot)
                {
                    return client != null && client.Connected && stream != null;
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
                thread = CreateReadThread();
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
            JoinReadThread(currentThread);
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

        private void ReadLoop()
        {
            NetworkStream currentStream = GetStream();
            if (currentStream == null)
                return;
            try
            {
                using (StreamReader reader = new StreamReader(currentStream, Encoding.UTF8, false, ReaderBufferSize))
                {
                    ReadMessages(reader);
                }
            }
            catch (IOException)
            {
                if (running)
                    throw;
            }
            catch (ObjectDisposedException)
            {
                if (running)
                    throw;
            }
        }

        private void WriteLine(string line)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(line + "\r\n");
            NetworkStream currentStream = GetStream();
            if (currentStream == null)
                return;
            currentStream.Write(buffer, 0, buffer.Length);
        }

        private NetworkStream GetStream()
        {
            lock (syncRoot)
            {
                return stream;
            }
        }

        private Thread CreateReadThread()
        {
            Thread thread = new Thread(ReadLoop);
            thread.IsBackground = true;
            return thread;
        }

        private void ReadMessages(StreamReader reader)
        {
            string line;
            while (running && (line = reader.ReadLine()) != null)
            {
                EventHandler<string> handler = MessageReceived;
                if (line.Length > 0 && handler != null)
                    handler(this, line);
            }
        }

        private static void JoinReadThread(Thread thread)
        {
            if (thread == null || Thread.CurrentThread == thread)
                return;
            thread.Join(ReadThreadJoinTimeoutMs);
        }
    }
}
