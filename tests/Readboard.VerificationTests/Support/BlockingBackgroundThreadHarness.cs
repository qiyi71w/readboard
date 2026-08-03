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
            VerificationCompletion.Wait(startedEvent, "Blocking worker did not start.");
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
                VerificationCompletion.Join(workerThread, "Blocking worker did not exit after release.");
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
