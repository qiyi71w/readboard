using System;
using System.Threading;

namespace readboard
{
    // A runtime selection in physical pixels relative to the bound HWND's client origin.
    internal sealed class BackgroundSelectionBinding
    {
        private readonly PixelRect clientSelection;
        private readonly int clientWidth;
        private readonly int clientHeight;
        private readonly double dpiScale;
        private int invalidated;

        private BackgroundSelectionBinding(WindowDescriptor window, PixelRect selection, int requestId)
        {
            Handle = window.Handle;
            RequestId = requestId;
            clientWidth = window.ClientBounds.Width;
            clientHeight = window.ClientBounds.Height;
            dpiScale = window.DpiScale;
            clientSelection = selection;
        }

        public IntPtr Handle { get; private set; }
        public int RequestId { get; private set; }

        public static BackgroundSelectionBinding TryCreate(WindowDescriptor window, PixelRect screenSelection, int requestId)
        {
            if (window == null || window.Handle == IntPtr.Zero || !IsPositive(window.ClientBounds)
                || !IsPositive(screenSelection) || window.DpiScale <= 0d)
                return null;
            PixelRect local = new PixelRect(screenSelection.X - window.ClientBounds.X,
                screenSelection.Y - window.ClientBounds.Y, screenSelection.Width, screenSelection.Height);
            if (!Contains(window.ClientBounds, local))
                return null;
            return new BackgroundSelectionBinding(window, local, requestId);
        }

        public bool TryProject(WindowDescriptor window, out PixelRect screenSelection)
        {
            screenSelection = null;
            if (Volatile.Read(ref invalidated) != 0)
                return false;
            if (window == null || window.Handle != Handle || !IsPositive(window.ClientBounds)
                || window.ClientBounds.Width != clientWidth || window.ClientBounds.Height != clientHeight
                || window.DpiScale != dpiScale || !Contains(window.ClientBounds, clientSelection))
            {
                Invalidate();
                return false;
            }
            screenSelection = new PixelRect(window.ClientBounds.X + clientSelection.X,
                window.ClientBounds.Y + clientSelection.Y, clientSelection.Width, clientSelection.Height);
            return true;
        }

        public void Invalidate()
        {
            Interlocked.Exchange(ref invalidated, 1);
        }

        private static bool IsPositive(PixelRect bounds)
        {
            return bounds != null && bounds.Width > 0 && bounds.Height > 0;
        }

        private static bool Contains(PixelRect client, PixelRect selection)
        {
            return selection.X >= 0 && selection.Y >= 0
                && (long)selection.X + selection.Width <= client.Width
                && (long)selection.Y + selection.Height <= client.Height;
        }
    }
}
