using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace readboard
{
    internal sealed class BackgroundSelectionWindowBindingCoordinator
    {
        private static readonly TimeSpan OverlayDismissDelay = TimeSpan.FromMilliseconds(40);
        private readonly Func<TimeSpan, Task> delayAsync;
        private int latestRequestId;

        internal BackgroundSelectionWindowBindingCoordinator(Func<TimeSpan, Task> delayAsync = null)
        {
            this.delayAsync = delayAsync ?? Task.Delay;
        }

        internal void Start(
            Point selectionCenter,
            Func<Point, IntPtr> resolveWindowHandle,
            Action<IntPtr> applyHandle,
            Action restoreMainWindow,
            Action<Exception> reportError)
        {
            if (resolveWindowHandle == null)
                throw new ArgumentNullException("resolveWindowHandle");
            if (applyHandle == null)
                throw new ArgumentNullException("applyHandle");
            if (restoreMainWindow == null)
                throw new ArgumentNullException("restoreMainWindow");
            if (reportError == null)
                throw new ArgumentNullException("reportError");

            int requestId = Interlocked.Increment(ref latestRequestId);
            _ = RunAsync(requestId, selectionCenter, resolveWindowHandle, applyHandle, restoreMainWindow, reportError);
        }

        private async Task RunAsync(
            int requestId,
            Point selectionCenter,
            Func<Point, IntPtr> resolveWindowHandle,
            Action<IntPtr> applyHandle,
            Action restoreMainWindow,
            Action<Exception> reportError)
        {
            try
            {
                await delayAsync(OverlayDismissDelay);
                if (!IsLatest(requestId))
                    return;

                IntPtr handle = resolveWindowHandle(selectionCenter);
                if (!IsLatest(requestId))
                    return;

                applyHandle(handle);
                restoreMainWindow();
            }
            catch (Exception ex)
            {
                if (!IsLatest(requestId))
                    return;

                reportError(ex);
                restoreMainWindow();
            }
        }

        private bool IsLatest(int requestId)
        {
            return Volatile.Read(ref latestRequestId) == requestId;
        }
    }
}
