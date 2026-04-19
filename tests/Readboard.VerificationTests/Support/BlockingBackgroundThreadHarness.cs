using System;
using System.Threading;

namespace Readboard.VerificationTests.Support
{
    internal sealed class BlockingBackgroundThreadHarness : IDisposable
    {
        private readonly ManualResetEventSlim startedEvent = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim releaseEvent = new ManualResetEventSlim(false);
        private readonly Thread workerThread;

        private BlockingBackgroundThreadHarness(string name)
        {
            workerThread = new Thread(Run);
            workerThread.IsBackground = true;
            workerThread.Name = name;
            workerThread.Start();
            if (!startedEvent.Wait(TimeSpan.FromSeconds(1)))
                throw new InvalidOperationException("Blocking test thread did not start.");
        }

        public Thread Thread
        {
            get { return workerThread; }
        }

        public static BlockingBackgroundThreadHarness Start(string name)
        {
            return new BlockingBackgroundThreadHarness(name);
        }

        public void Release()
        {
            releaseEvent.Set();
        }

        public void Dispose()
        {
            Release();
            if (workerThread.IsAlive)
                workerThread.Join(TimeSpan.FromSeconds(1));
            startedEvent.Dispose();
            releaseEvent.Dispose();
        }

        private void Run()
        {
            startedEvent.Set();
            releaseEvent.Wait();
        }
    }
}
