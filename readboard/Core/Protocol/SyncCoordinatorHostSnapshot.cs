using System;
using System.ComponentModel;

namespace readboard
{
    internal sealed class UiThreadInvoker
    {
        private static readonly object[] EmptyArguments = new object[0];
        private const int AsyncWaitPollIntervalMs = 25;
        private readonly ISynchronizeInvoke synchronizer;

        public UiThreadInvoker(ISynchronizeInvoke synchronizer)
        {
            if (synchronizer == null)
                throw new ArgumentNullException("synchronizer");

            this.synchronizer = synchronizer;
        }

        public T Execute<T>(Func<T> callback)
        {
            if (callback == null)
                throw new ArgumentNullException("callback");
            if (synchronizer.InvokeRequired)
                return (T)synchronizer.Invoke(callback, EmptyArguments);
            return callback();
        }

        public T ExecuteOrCancel<T>(Func<T> callback, Func<bool> shouldCancel)
        {
            if (callback == null)
                throw new ArgumentNullException("callback");
            if (shouldCancel == null)
                throw new ArgumentNullException("shouldCancel");
            if (!synchronizer.InvokeRequired)
                return callback();
            if (shouldCancel())
                throw new SnapshotCaptureCancelledException();

            IAsyncResult asyncResult = synchronizer.BeginInvoke(callback, EmptyArguments);
            return WaitForResultOrCancel<T>(asyncResult, shouldCancel);
        }

        private T WaitForResultOrCancel<T>(IAsyncResult asyncResult, Func<bool> shouldCancel)
        {
            while (!asyncResult.AsyncWaitHandle.WaitOne(AsyncWaitPollIntervalMs))
            {
                if (shouldCancel())
                    throw new SnapshotCaptureCancelledException();
            }

            return (T)synchronizer.EndInvoke(asyncResult);
        }
    }

    internal sealed class SnapshotCaptureCancelledException : OperationCanceledException
    {
    }

    internal sealed class SyncCoordinatorHostSnapshot
    {
        public SyncMode SyncMode { get; set; }
        public int BoardWidth { get; set; }
        public int BoardHeight { get; set; }
        public PixelRect SelectionBounds { get; set; }
        public IntPtr SelectedWindowHandle { get; set; }
        public float DpiScale { get; set; }
        public string LegacyTypeToken { get; set; }
        public bool ShowInBoard { get; set; }
        public bool SupportsForegroundFoxInBoardProtocol { get; set; }
        public bool CanUseLightweightInterop { get; set; }
        public bool AutoMinimize { get; set; }
        public int SampleIntervalMs { get; set; }
        public bool UseEnhancedCapture { get; set; }
        public string PlayColor { get; set; }
        public string AiTimeValue { get; set; }
        public string PlayoutsValue { get; set; }
        public string FirstPolicyValue { get; set; }
    }
}
