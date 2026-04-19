using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Host
{
    public sealed class BackgroundSelectionWindowBindingCoordinatorTests
    {
        [Fact]
        public async Task Start_ResolvesWindowBeforeRestoringMainForm()
        {
            DelayQueue delayQueue = new DelayQueue();
            BackgroundSelectionWindowBindingCoordinator coordinator =
                new BackgroundSelectionWindowBindingCoordinator(delayQueue.DelayAsync);

            bool restored = false;
            bool resolvedAfterRestore = false;
            IntPtr appliedHandle = IntPtr.Zero;
            TaskCompletionSource<bool> applied = CreateSignal();
            TaskCompletionSource<bool> restoredSignal = CreateSignal();

            coordinator.Start(
                new Point(320, 240),
                delegate(Point point)
                {
                    resolvedAfterRestore = restored;
                    return new IntPtr(4242);
                },
                delegate(IntPtr handle)
                {
                    appliedHandle = handle;
                    applied.TrySetResult(true);
                },
                delegate
                {
                    restored = true;
                    restoredSignal.TrySetResult(true);
                },
                delegate(Exception ex)
                {
                    throw ex;
                });

            delayQueue.ReleaseNext();

            await applied.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await restoredSignal.Task.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.False(resolvedAfterRestore);
            Assert.True(restored);
            Assert.Equal(new IntPtr(4242), appliedHandle);
        }

        [Fact]
        public async Task Start_OnlyAppliesLatestSelectionCenterWhenRequestsOverlap()
        {
            DelayQueue delayQueue = new DelayQueue();
            BackgroundSelectionWindowBindingCoordinator coordinator =
                new BackgroundSelectionWindowBindingCoordinator(delayQueue.DelayAsync);

            int appliedCount = 0;
            int restoredCount = 0;
            IntPtr appliedHandle = IntPtr.Zero;
            TaskCompletionSource<bool> secondApplied = CreateSignal();
            TaskCompletionSource<bool> secondRestored = CreateSignal();
            Point firstCenter = new Point(100, 100);
            Point secondCenter = new Point(200, 200);

            coordinator.Start(
                firstCenter,
                ResolveHandle,
                delegate(IntPtr handle)
                {
                    appliedHandle = handle;
                    appliedCount++;
                },
                delegate
                {
                    restoredCount++;
                },
                delegate(Exception ex)
                {
                    throw ex;
                });

            coordinator.Start(
                secondCenter,
                ResolveHandle,
                delegate(IntPtr handle)
                {
                    appliedHandle = handle;
                    appliedCount++;
                    secondApplied.TrySetResult(true);
                },
                delegate
                {
                    restoredCount++;
                    secondRestored.TrySetResult(true);
                },
                delegate(Exception ex)
                {
                    throw ex;
                });

            delayQueue.WaitUntilPendingCount(2);
            delayQueue.ReleaseNext();
            delayQueue.ReleaseNext();

            await secondApplied.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await secondRestored.Task.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(1, appliedCount);
            Assert.Equal(1, restoredCount);
            Assert.Equal(new IntPtr(2200), appliedHandle);

            static IntPtr ResolveHandle(Point point)
            {
                return point.X == 200 ? new IntPtr(2200) : new IntPtr(1100);
            }
        }

        private static TaskCompletionSource<bool> CreateSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class DelayQueue
        {
            private readonly Queue<TaskCompletionSource<bool>> pendingSignals = new Queue<TaskCompletionSource<bool>>();

            public Task DelayAsync(TimeSpan delay)
            {
                TaskCompletionSource<bool> signal = CreateSignal();
                lock (pendingSignals)
                {
                    pendingSignals.Enqueue(signal);
                }
                return signal.Task;
            }

            public void ReleaseNext()
            {
                TaskCompletionSource<bool> signal;
                lock (pendingSignals)
                {
                    signal = pendingSignals.Dequeue();
                }

                signal.TrySetResult(true);
            }

            public void WaitUntilPendingCount(int expectedCount)
            {
                DateTime deadlineUtc = DateTime.UtcNow.AddSeconds(1);
                SpinWait spinWait = new SpinWait();

                while (DateTime.UtcNow <= deadlineUtc)
                {
                    lock (pendingSignals)
                    {
                        if (pendingSignals.Count >= expectedCount)
                            return;
                    }

                    spinWait.SpinOnce();
                }

                throw new TimeoutException("Delay queue did not reach the expected pending count.");
            }
        }
    }
}
