using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using readboard;

namespace Readboard.VerificationTests
{
    public sealed class UiThreadInvokerTests
    {
        [Fact]
        public void Execute_UsesSynchronizerWhenInvokeIsRequired()
        {
            FakeSynchronizer synchronizer = new FakeSynchronizer(true);
            UiThreadInvoker invoker = new UiThreadInvoker(synchronizer);

            int result = invoker.Execute(() => 42);

            Assert.Equal(42, result);
            Assert.Equal(1, synchronizer.InvokeCallCount);
        }

        [Fact]
        public void Execute_RunsInlineWhenAlreadyOnOwningThread()
        {
            FakeSynchronizer synchronizer = new FakeSynchronizer(false);
            UiThreadInvoker invoker = new UiThreadInvoker(synchronizer);

            int result = invoker.Execute(() => 7);

            Assert.Equal(7, result);
            Assert.Equal(0, synchronizer.InvokeCallCount);
        }

        [Fact]
        public async Task ExecuteOrCancel_ThrowsWhenCancellationTripsWhilePending()
        {
            BlockingAsyncSynchronizer synchronizer = new BlockingAsyncSynchronizer();
            UiThreadInvoker invoker = new UiThreadInvoker(synchronizer);
            bool shutdownRequested = false;

            Task<int> resultTask = Task.Run(() =>
                invoker.ExecuteOrCancel(
                    () => 42,
                    () => shutdownRequested));

            Assert.True(synchronizer.InvocationQueued.Wait(TimeSpan.FromSeconds(1)));
            shutdownRequested = true;

            await Assert.ThrowsAsync<SnapshotCaptureCancelledException>(async () =>
                await resultTask.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.Equal(1, synchronizer.BeginInvokeCallCount);
            Assert.Equal(0, synchronizer.InvokeCallCount);
        }

        private sealed class FakeSynchronizer : ISynchronizeInvoke
        {
            public FakeSynchronizer(bool invokeRequired)
            {
                InvokeRequired = invokeRequired;
            }

            public bool InvokeRequired { get; private set; }
            public int InvokeCallCount { get; private set; }

            public IAsyncResult BeginInvoke(Delegate method, object[] args)
            {
                throw new NotSupportedException();
            }

            public object EndInvoke(IAsyncResult result)
            {
                throw new NotSupportedException();
            }

            public object Invoke(Delegate method, object[] args)
            {
                InvokeCallCount++;
                return method.DynamicInvoke(args);
            }
        }

        private sealed class BlockingAsyncSynchronizer : ISynchronizeInvoke
        {
            public ManualResetEventSlim InvocationQueued { get; } = new ManualResetEventSlim(false);
            public bool InvokeRequired
            {
                get { return true; }
            }

            public int BeginInvokeCallCount { get; private set; }
            public int InvokeCallCount { get; private set; }

            public IAsyncResult BeginInvoke(Delegate method, object[] args)
            {
                BeginInvokeCallCount++;
                InvocationQueued.Set();
                return new PendingAsyncResult();
            }

            public object EndInvoke(IAsyncResult result)
            {
                return ((PendingAsyncResult)result).Result;
            }

            public object Invoke(Delegate method, object[] args)
            {
                InvokeCallCount++;
                return method.DynamicInvoke(args);
            }
        }

        private sealed class PendingAsyncResult : IAsyncResult
        {
            private readonly ManualResetEvent waitHandle = new ManualResetEvent(false);

            public object AsyncState
            {
                get { return null; }
            }

            public WaitHandle AsyncWaitHandle
            {
                get { return waitHandle; }
            }

            public bool CompletedSynchronously
            {
                get { return false; }
            }

            public bool IsCompleted
            {
                get { return false; }
            }

            public object Result
            {
                get { return null; }
            }
        }
    }
}
