using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Readboard.VerificationTests.Support
{
    internal static class VerificationCompletion
    {
        internal static readonly TimeSpan WatchdogTimeout = TimeSpan.FromSeconds(30);

        internal static void Wait(ManualResetEventSlim signal, string message)
        {
            Assert.True(signal.Wait(WatchdogTimeout), message);
        }

        internal static void Join(Thread thread, string message)
        {
            Assert.True(thread.Join(WatchdogTimeout), message);
        }

        internal static async Task WaitAsync(Task completion, string message)
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Task watchdog = Task.Delay(WatchdogTimeout, cancellation.Token);
                Task finished = await Task.WhenAny(completion, watchdog);
                Assert.True(ReferenceEquals(completion, finished), message);
                cancellation.Cancel();
                await completion;
            }
        }

        internal static async Task<T> WaitAsync<T>(Task<T> completion, string message)
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Task watchdog = Task.Delay(WatchdogTimeout, cancellation.Token);
                Task finished = await Task.WhenAny(completion, watchdog);
                Assert.True(ReferenceEquals(completion, finished), message);
                cancellation.Cancel();
                return await completion;
            }
        }
    }
}
