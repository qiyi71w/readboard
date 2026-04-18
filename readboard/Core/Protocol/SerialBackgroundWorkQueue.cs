using System;
using System.Collections.Generic;
using System.Threading;

namespace readboard
{
    internal sealed class SerialBackgroundWorkQueue
    {
        private readonly object syncRoot = new object();
        private readonly Queue<Action> workItems = new Queue<Action>();
        private readonly AutoResetEvent workAvailableEvent = new AutoResetEvent(false);
        private readonly string workerName;
        private Thread workerThread;
        private bool stopRequested;

        public SerialBackgroundWorkQueue(string workerName)
        {
            if (string.IsNullOrWhiteSpace(workerName))
                throw new ArgumentException("workerName");
            this.workerName = workerName;
        }

        public bool TryEnqueue(Action workItem)
        {
            if (workItem == null)
                throw new ArgumentNullException("workItem");

            lock (syncRoot)
            {
                if (stopRequested)
                    return false;

                workItems.Enqueue(workItem);
                EnsureWorkerStarted();
            }

            workAvailableEvent.Set();
            return true;
        }

        public void Stop()
        {
            lock (syncRoot)
            {
                if (stopRequested)
                    return;

                stopRequested = true;
                workItems.Clear();
            }

            workAvailableEvent.Set();
        }

        private void EnsureWorkerStarted()
        {
            if (workerThread != null)
                return;

            workerThread = new Thread(RunLoop);
            workerThread.IsBackground = true;
            workerThread.Name = workerName;
            workerThread.Start();
        }

        private void RunLoop()
        {
            while (true)
            {
                Action workItem = TakeNextWorkItem();
                if (workItem == null)
                    return;
                try
                {
                    workItem();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError(ex.ToString());
                }
            }
        }

        private Action TakeNextWorkItem()
        {
            while (true)
            {
                lock (syncRoot)
                {
                    if (workItems.Count > 0)
                        return workItems.Dequeue();
                    if (stopRequested)
                        return null;
                }

                workAvailableEvent.WaitOne();
            }
        }
    }
}
