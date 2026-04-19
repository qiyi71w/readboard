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

        public event EventHandler<string> MessageReceived;

        public bool IsConnected
        {
            get { return true; }
        }

        public void Start()
        {
            EnsureConsoleAllocated();
            lock (syncRoot)
            {
                if (running)
                    throw new InvalidOperationException("Pipe transport is already started.");
                inputReader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8, false, ReaderBufferSize);
                running = true;
                readThread = CreateReadThread();
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
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine(line);
        }

        public void SendError(string message)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Error.WriteLine("error: " + message);
        }

        public void Dispose()
        {
            Stop();
        }

        private void ReadLoop()
        {
            StreamReader reader = inputReader;
            IntPtr currentThreadHandle = IntPtr.Zero;
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
                ReadMessages(reader);
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
            finally
            {
                ReleaseReadThreadHandle(currentThreadHandle);
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
            while (running)
            {
                string line = reader.ReadLine();
                if (line == null)
                    return;
                RaiseMessageReceived(line);
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
            if (handle == IntPtr.Zero)
                return false;
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
