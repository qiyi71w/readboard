using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class SerialBackgroundWorkQueueTests
    {
        [Fact]
        public void TryEnqueue_RunsQueuedWorkSequentiallyInOrder()
        {
            object queue = CreateQueue();
            ManualResetEventSlim firstStarted = new ManualResetEventSlim(false);
            ManualResetEventSlim releaseFirst = new ManualResetEventSlim(false);
            ManualResetEventSlim secondStarted = new ManualResetEventSlim(false);
            List<int> executionOrder = new List<int>();
            object orderLock = new object();

            Assert.True(TryEnqueue(queue, delegate
            {
                firstStarted.Set();
                Assert.True(releaseFirst.Wait(TimeSpan.FromSeconds(1)));
                lock (orderLock)
                {
                    executionOrder.Add(1);
                }
            }));

            Assert.True(TryEnqueue(queue, delegate
            {
                secondStarted.Set();
                lock (orderLock)
                {
                    executionOrder.Add(2);
                }
            }));

            Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(1)));
            Assert.False(secondStarted.Wait(TimeSpan.FromMilliseconds(150)));

            releaseFirst.Set();
            Assert.True(secondStarted.Wait(TimeSpan.FromSeconds(1)));
            SpinWait.SpinUntil(
                delegate
                {
                    lock (orderLock)
                    {
                        return executionOrder.Count == 2;
                    }
                },
                TimeSpan.FromSeconds(1));

            lock (orderLock)
            {
                Assert.Equal(new[] { 1, 2 }, executionOrder);
            }

            Stop(queue);
        }

        [Fact]
        public void Stop_DropsQueuedWorkThatHasNotStarted()
        {
            object queue = CreateQueue();
            ManualResetEventSlim firstStarted = new ManualResetEventSlim(false);
            ManualResetEventSlim releaseFirst = new ManualResetEventSlim(false);
            ManualResetEventSlim firstCompleted = new ManualResetEventSlim(false);
            ManualResetEventSlim secondRan = new ManualResetEventSlim(false);

            Assert.True(TryEnqueue(queue, delegate
            {
                firstStarted.Set();
                Assert.True(releaseFirst.Wait(TimeSpan.FromSeconds(1)));
                firstCompleted.Set();
            }));

            Assert.True(TryEnqueue(queue, delegate
            {
                secondRan.Set();
            }));

            Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(1)));
            Stop(queue);
            releaseFirst.Set();

            Assert.True(firstCompleted.Wait(TimeSpan.FromSeconds(1)));
            Assert.False(secondRan.Wait(TimeSpan.FromMilliseconds(150)));
        }

        [Fact]
        public void Stop_AllowsRunningWorkItemToFinish()
        {
            object queue = CreateQueue();
            ManualResetEventSlim firstStarted = new ManualResetEventSlim(false);
            ManualResetEventSlim releaseFirst = new ManualResetEventSlim(false);
            ManualResetEventSlim firstCompleted = new ManualResetEventSlim(false);

            Assert.True(TryEnqueue(queue, delegate
            {
                firstStarted.Set();
                Assert.True(releaseFirst.Wait(TimeSpan.FromSeconds(1)));
                firstCompleted.Set();
            }));

            Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(1)));
            Stop(queue);
            releaseFirst.Set();

            Assert.True(firstCompleted.Wait(TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public void TryEnqueue_ContinuesProcessingAfterWorkItemThrows()
        {
            object queue = CreateQueue();
            ManualResetEventSlim secondRan = new ManualResetEventSlim(false);

            Assert.True(TryEnqueue(queue, delegate
            {
                throw new InvalidOperationException("boom");
            }));

            Assert.True(TryEnqueue(queue, delegate
            {
                secondRan.Set();
            }));

            Assert.True(secondRan.Wait(TimeSpan.FromSeconds(1)));
            Stop(queue);
        }

        private static object CreateQueue()
        {
            Type queueType = ResolveQueueType();
            ConstructorInfo constructor = queueType.GetConstructor(new[] { typeof(string) });
            Assert.True(constructor != null, "SerialBackgroundWorkQueue(string) constructor is required.");
            return constructor.Invoke(new object[] { "PlaceRequestQueueTests" });
        }

        private static Type ResolveQueueType()
        {
            Type queueType = typeof(SyncSessionCoordinator).Assembly.GetType("readboard.SerialBackgroundWorkQueue");
            Assert.True(queueType != null, "Missing queue type: readboard.SerialBackgroundWorkQueue");
            return queueType;
        }

        private static bool TryEnqueue(object queue, Action action)
        {
            MethodInfo method = queue.GetType().GetMethod("TryEnqueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.True(method != null, "Missing queue method: TryEnqueue");
            return (bool)method.Invoke(queue, new object[] { action });
        }

        private static void Stop(object queue)
        {
            MethodInfo method = queue.GetType().GetMethod("Stop", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.True(method != null, "Missing queue method: Stop");
            method.Invoke(queue, null);
        }
    }
}
