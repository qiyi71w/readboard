using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Xunit;
using readboard;
using Readboard.VerificationTests.Support;

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
                releaseFirst.Wait();
                lock (orderLock)
                {
                    executionOrder.Add(1);
                }
            }));

            Assert.True(TryEnqueue(queue, delegate
            {
                lock (orderLock)
                {
                    executionOrder.Add(2);
                }
                secondStarted.Set();
            }));

            VerificationCompletion.Wait(firstStarted, "First work item did not start.");
            Assert.False(secondStarted.IsSet);

            releaseFirst.Set();
            VerificationCompletion.Wait(secondStarted, "Second work item did not start after release.");

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
                releaseFirst.Wait();
                firstCompleted.Set();
            }));

            Assert.True(TryEnqueue(queue, delegate
            {
                secondRan.Set();
            }));

            VerificationCompletion.Wait(firstStarted, "First work item did not start before stop.");
            Stop(queue);
            releaseFirst.Set();

            VerificationCompletion.Wait(firstCompleted, "Running work item did not finish after release.");
            Assert.False(secondRan.IsSet);
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
                releaseFirst.Wait();
                firstCompleted.Set();
            }));

            VerificationCompletion.Wait(firstStarted, "First work item did not start before stop.");
            Stop(queue);
            releaseFirst.Set();

            VerificationCompletion.Wait(firstCompleted, "Running work item did not finish after release.");
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

            VerificationCompletion.Wait(secondRan, "Queued work item did not run.");
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
