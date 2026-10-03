using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;

namespace readboard
{
    internal sealed class PipeTransport : IReadBoardTransport
    {
        private const uint DuplicateSameAccess = 2;
        private const int ReaderBufferSize = 1024;

        private readonly object syncRoot = new object();
        private StreamReader inputReader;
        private Thread readThread;
        private IntPtr readThreadHandle = IntPtr.Zero;
        private volatile bool running;
        private bool started;
        private int generation;

        public event EventHandler Disconnected;

        public event EventHandler<string> MessageReceived;

        public bool IsConnected
        {
            get
            {
                lock (syncRoot)
                {
                    return running;
                }
            }
        }

        public void Start()
        {
            EnsureConsoleAllocated();
            lock (syncRoot)
            {
                if (running)
                    throw new InvalidOperationException("Pipe transport is already started.");
                started = true;
                generation++;
                inputReader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8, false, ReaderBufferSize);
                running = true;
                readThread = CreateReadThread(generation);
                readThread.Start();
            }
        }

        public void Stop()
        {
            StreamReader currentReader;
            Thread currentThread;
            IntPtr currentReadThreadHandle;
            lock (syncRoot)
            {
                running = false;
                generation++;
                currentReader = inputReader;
                currentThread = readThread;
                currentReadThreadHandle = readThreadHandle;
                inputReader = null;
                readThread = null;
                readThreadHandle = IntPtr.Zero;
            }
            CancelPendingRead(currentReadThreadHandle);
            if (currentReader != null)
                currentReader.Dispose();
            TryJoinReadThread(currentThread);
            CloseThreadHandle(currentReadThreadHandle);
        }

        public void Send(string line)
        {
            int writeGen;
            bool wasStarted;
            lock (syncRoot)
            {
                if (started && !running)
                    return;
                wasStarted = started;
                writeGen = generation;
            }

            if (!wasStarted)
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.WriteLine(line);
                return;
            }

            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.WriteLine(line);
            }
            catch (IOException)
            {
                NotifyUnexpectedDisconnect(writeGen);
            }
        }

        public void SendError(string message)
        {
            int writeGen;
            bool wasStarted;
            lock (syncRoot)
            {
                if (started && !running)
                    return;
                wasStarted = started;
                writeGen = generation;
            }

            if (!wasStarted)
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.Error.WriteLine("error: " + message);
                return;
            }

            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.Error.WriteLine("error: " + message);
            }
            catch (IOException)
            {
                NotifyUnexpectedDisconnect(writeGen);
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void ReadLoop(int currentGen)
        {
            StreamReader reader;
            IntPtr currentThreadHandle = IntPtr.Zero;
            lock (syncRoot)
            {
                reader = inputReader;
            }
            if (reader == null)
                return;
            try
            {
                currentThreadHandle = DuplicateCurrentThreadHandle();
                if (!TryRegisterReadThreadHandle(currentThreadHandle))
                {
                    CloseThreadHandle(currentThreadHandle);
                    return;
                }
                ReadMessages(reader, currentGen);
            }
            finally
            {
                ReleaseReadThreadHandle(currentThreadHandle);
            }
        }

        private Thread CreateReadThread(int gen)
        {
            Thread thread = new Thread(() => ReadLoop(gen));
            thread.IsBackground = true;
            return thread;
        }

        private void ReadMessages(StreamReader reader, int gen)
        {
            while (true)
            {
                string line;
                try
                {
                    line = reader.ReadLine();
                }
                catch (IOException)
                {
                    NotifyUnexpectedDisconnect(gen);
                    return;
                }
                catch (ObjectDisposedException)
                {
                    NotifyUnexpectedDisconnect(gen);
                    return;
                }

                if (line == null)
                {
                    NotifyUnexpectedDisconnect(gen);
                    return;
                }

                if (!running)
                    return;

                RaiseMessageReceived(line);
            }
        }

        private void NotifyUnexpectedDisconnect(int expectedGen)
        {
            StreamReader currentReader = null;
            IntPtr currentReadThreadHandle = IntPtr.Zero;
            bool raiseDisconnected = false;
            EventHandler handler = null;

            lock (syncRoot)
            {
                if (running && generation == expectedGen)
                {
                    running = false;
                    generation++;
                    currentReader = inputReader;
                    currentReadThreadHandle = readThreadHandle;
                    inputReader = null;
                    readThread = null;
                    readThreadHandle = IntPtr.Zero;
                    raiseDisconnected = true;
                    handler = Disconnected;
                }
            }

            if (currentReadThreadHandle != IntPtr.Zero)
            {
                CancelPendingRead(currentReadThreadHandle);
                CloseThreadHandle(currentReadThreadHandle);
            }
            if (currentReader != null)
            {
                try
                {
                    currentReader.Dispose();
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }

            if (raiseDisconnected)
            {
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }

        private void RaiseMessageReceived(string line)
        {
            EventHandler<string> handler = MessageReceived;
            if (line.Length > 0 && handler != null)
                handler(this, line);
        }

        private static void TryJoinReadThread(Thread thread)
        {
            if (thread == null || Thread.CurrentThread == thread)
                return;
            thread.Join(0);
        }

        private bool TryRegisterReadThreadHandle(IntPtr handle)
        {
            lock (syncRoot)
            {
                if (!running || readThread == null || Thread.CurrentThread != readThread)
                    return false;
                readThreadHandle = handle;
                return true;
            }
        }

        private void ReleaseReadThreadHandle(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return;
            lock (syncRoot)
            {
                if (readThreadHandle == handle)
                {
                    readThreadHandle = IntPtr.Zero;
                    CloseThreadHandle(handle);
                }
            }
        }

        private static IntPtr DuplicateCurrentThreadHandle()
        {
            IntPtr duplicatedHandle;
            if (!DuplicateHandle(
                GetCurrentProcess(),
                GetCurrentThread(),
                GetCurrentProcess(),
                out duplicatedHandle,
                0,
                false,
                DuplicateSameAccess))
                return IntPtr.Zero;
            return duplicatedHandle;
        }

        private static void CancelPendingRead(IntPtr threadHandle)
        {
            if (threadHandle == IntPtr.Zero)
                return;
            CancelSynchronousIo(threadHandle);
        }

        private static void CloseThreadHandle(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
                CloseHandle(handle);
        }

        private static void EnsureConsoleAllocated()
        {
            AllocConsole();
            HideConsole();
        }

        private static void HideConsole()
        {
            string consoleTitle = Console.Title;
            IntPtr handle = FindWindow("ConsoleWindowClass", consoleTitle);
            if (handle != IntPtr.Zero)
                ShowWindow(handle, 0);
        }

        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DuplicateHandle(
            IntPtr sourceProcessHandle,
            IntPtr sourceHandle,
            IntPtr targetProcessHandle,
            out IntPtr targetHandle,
            uint desiredAccess,
            bool inheritHandle,
            uint options);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelSynchronousIo(IntPtr threadHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, uint command);
    }
}
