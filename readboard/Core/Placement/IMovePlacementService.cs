using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace readboard
{
    internal interface IMovePlacementService
    {
        MovePlacementResult Place(MovePlacementRequest request);
    }

    internal sealed class LegacyMovePlacementService : IMovePlacementService
    {
        private const string YikeRenderWidgetClassName = "Chrome_RenderWidgetHostHWND";
        private readonly IPlacementNativeMethods nativeMethods;

        internal LegacyMovePlacementService(IPlacementNativeMethods nativeMethods)
        {
            if (nativeMethods == null)
                throw new ArgumentNullException("nativeMethods");

            this.nativeMethods = nativeMethods;
        }

        internal static LegacyMovePlacementService CreateDefault()
        {
            return new LegacyMovePlacementService(
                new User32PlacementNativeMethods());
        }

        public MovePlacementResult Place(MovePlacementRequest request)
        {
            MovePlacementResult validationFailure = Validate(request);
            if (validationFailure != null)
                return validationFailure;

            PlacementPathKind path = ResolvePath(request);
            if (path == PlacementPathKind.Unknown)
                return Failure(request, path, MovePlacementFailureKind.UnsupportedPath, "Unsupported placement path.");

            PlacementPoint point;
            string failureReason;
            if (!TryResolvePlacementPoint(request, path, out point, out failureReason))
                return Failure(request, path, MovePlacementFailureKind.MissingFrame, failureReason);

            try
            {
                return ExecutePlacement(request, path, point);
            }
            catch (Exception ex)
            {
                return Failure(request, path, MovePlacementFailureKind.PlacementFailed, ex.Message);
            }
        }

        private static MovePlacementResult Validate(MovePlacementRequest request)
        {
            if (request == null)
                return Failure(null, PlacementPathKind.Unknown, MovePlacementFailureKind.MissingFrame, "Placement request is required.");
            if (request.Frame == null)
                return Failure(request, PlacementPathKind.Unknown, MovePlacementFailureKind.MissingFrame, "Board frame is required.");
            if (request.Move == null)
                return Failure(request, PlacementPathKind.Unknown, MovePlacementFailureKind.MissingMove, "Move request is required.");
            if (request.Frame.Viewport == null)
                return Failure(request, PlacementPathKind.Unknown, MovePlacementFailureKind.MissingFrame, "Board viewport is required.");
            if (request.Frame.BoardSize == null)
                return Failure(request, PlacementPathKind.Unknown, MovePlacementFailureKind.MissingFrame, "Board size is required.");
            if (request.Frame.BoardSize.Width <= 0 || request.Frame.BoardSize.Height <= 0)
                return Failure(request, PlacementPathKind.Unknown, MovePlacementFailureKind.MissingFrame, "Board size must be positive.");
            return null;
        }

        private static PlacementPathKind ResolvePath(MovePlacementRequest request)
        {
            SyncMode syncMode = request.Frame.SyncMode;
            if (syncMode == SyncMode.Yike)
                return PlacementPathKind.BackgroundSend;
            if (request.Frame.Window != null && request.Frame.Window.IsJavaWindow)
                return PlacementPathKind.Foreground;
            if (syncMode == SyncMode.Foreground || syncMode == SyncMode.Fox)
                return PlacementPathKind.Foreground;
            if (syncMode == SyncMode.FoxBackgroundPlace)
                return PlacementPathKind.BackgroundSend;
            if (syncMode == SyncMode.Tygem || syncMode == SyncMode.Sina || syncMode == SyncMode.Background)
                return PlacementPathKind.BackgroundPost;
            return PlacementPathKind.Unknown;
        }

        private bool TryResolvePlacementPoint(
            MovePlacementRequest request,
            PlacementPathKind path,
            out PlacementPoint point,
            out string failureReason)
        {
            PixelRect bounds;
            point = PlacementPoint.Empty;
            failureReason = null;

            if (!TryResolveBounds(request.Frame, path, out bounds))
            {
                failureReason = path == PlacementPathKind.Foreground
                    ? "Foreground placement requires screen bounds."
                    : "Background placement requires client bounds.";
                return false;
            }

            point = BuildPlacementPoint(request, path, bounds);
            return true;
        }

        private bool TryResolveBounds(BoardFrame frame, PlacementPathKind path, out PixelRect bounds)
        {
            if (path == PlacementPathKind.Foreground)
                return TryResolveScreenBounds(frame, out bounds);
            return TryResolveClientBounds(frame, out bounds);
        }

        private static bool TryResolveScreenBounds(BoardFrame frame, out PixelRect bounds)
        {
            if (TryProjectScreenBounds(frame, out bounds))
                return true;

            bounds = frame.Viewport.ScreenBounds;
            return IsUsable(bounds);
        }

        private static bool TryProjectScreenBounds(BoardFrame frame, out PixelRect bounds)
        {
            bounds = null;
            if (frame == null || frame.Viewport == null)
                return false;

            PixelRect sourceBounds = frame.Viewport.SourceBounds;
            WindowDescriptor window = frame.Window;
            PixelRect windowBounds = window == null ? null : window.Bounds;
            if (!IsUsable(sourceBounds) || !IsUsable(windowBounds))
                return false;

            bounds = DisplayScaling.ClientToScreenBounds(sourceBounds, windowBounds, window.IsDpiAware, window.DpiScale);
            return true;
        }

        private bool TryResolveClientBounds(BoardFrame frame, out PixelRect bounds)
        {
            if (frame != null && frame.SyncMode == SyncMode.Yike)
                return TryResolveYikeRenderWidgetClientBounds(frame, out bounds);

            if (ShouldPreferScreenBoundsForBackground(frame) && TryResolveClientBoundsFromScreen(frame, out bounds))
                return true;

            bounds = frame.Viewport.SourceBounds;
            if (IsUsable(bounds))
                return true;
            return TryResolveClientBoundsFromScreen(frame, out bounds);
        }

        private static bool ShouldPreferScreenBoundsForBackground(BoardFrame frame)
        {
            return frame != null && frame.SyncMode == SyncMode.Background;
        }

        private static bool TryResolveClientBoundsFromScreen(BoardFrame frame, out PixelRect bounds)
        {
            bounds = null;
            if (!IsUsable(frame == null || frame.Viewport == null ? null : frame.Viewport.ScreenBounds)
                || !IsUsable(frame == null || frame.Window == null ? null : frame.Window.Bounds))
                return false;

            PixelRect screenBounds = frame.Viewport.ScreenBounds;
            WindowDescriptor window = frame.Window;
            bounds = DisplayScaling.ScreenToClientBounds(screenBounds, window.Bounds, window.IsDpiAware, window.DpiScale);
            return IsUsable(bounds);
        }

        private bool TryResolveYikeRenderWidgetClientBounds(BoardFrame frame, out PixelRect bounds)
        {
            bounds = null;
            if (frame == null || frame.Viewport == null || frame.Window == null)
                return false;
            if (!IsUsable(frame.Viewport.SourceBounds) || !IsUsable(frame.Window.Bounds))
                return false;

            IntPtr renderWidgetHandle = ResolveYikeRenderWidgetHandle(frame.Window.Handle);
            if (renderWidgetHandle == IntPtr.Zero)
                return false;

            PixelRect renderWidgetScreenBounds;
            if (!nativeMethods.TryGetWindowBounds(renderWidgetHandle, out renderWidgetScreenBounds)
                || !IsUsable(renderWidgetScreenBounds))
            {
                return false;
            }

            bounds = new PixelRect(
                frame.Viewport.SourceBounds.X,
                frame.Viewport.SourceBounds.Y,
                frame.Viewport.SourceBounds.Width,
                frame.Viewport.SourceBounds.Height);
            return IsUsable(bounds);
        }

        private static PlacementPoint BuildPlacementPoint(
            MovePlacementRequest request,
            PlacementPathKind path,
            PixelRect bounds)
        {
            PlacementPoint explicitPoint;
            if (TryBuildExplicitPlacementPoint(request, bounds, out explicitPoint))
            {
                if (path != PlacementPathKind.BackgroundPost)
                    return explicitPoint;

                double explicitDpiScale = ResolveBackgroundDpiScale(request.Frame.Window);
                bool explicitIsDpiAware = request.Frame.Window != null && request.Frame.Window.IsDpiAware;
                return new PlacementPoint(
                    DisplayScaling.ScaleClientCoordinateForPostMessage(explicitPoint.X, explicitIsDpiAware, explicitDpiScale),
                    DisplayScaling.ScaleClientCoordinateForPostMessage(explicitPoint.Y, explicitIsDpiAware, explicitDpiScale));
            }

            double cellWidth = ResolveCellSize(bounds.Width, request.Frame.Viewport.CellWidth, request.Frame.BoardSize.Width);
            double cellHeight = ResolveCellSize(bounds.Height, request.Frame.Viewport.CellHeight, request.Frame.BoardSize.Height);
            int x = (int)Math.Round(bounds.X + cellWidth * (request.Move.X + 0.5d));
            int y = (int)Math.Round(bounds.Y + cellHeight * (request.Move.Y + 0.5d));

            if (path != PlacementPathKind.BackgroundPost)
                return new PlacementPoint(x, y);

            double dpiScale = ResolveBackgroundDpiScale(request.Frame.Window);
            bool isDpiAware = request.Frame.Window != null && request.Frame.Window.IsDpiAware;
            return new PlacementPoint(
                DisplayScaling.ScaleClientCoordinateForPostMessage(x, isDpiAware, dpiScale),
                DisplayScaling.ScaleClientCoordinateForPostMessage(y, isDpiAware, dpiScale));
        }

        private static bool TryBuildExplicitPlacementPoint(
            MovePlacementRequest request,
            PixelRect bounds,
            out PlacementPoint point)
        {
            point = PlacementPoint.Empty;
            if (request == null
                || request.Frame == null
                || request.Frame.Viewport == null
                || request.Move == null
                || !IsUsable(bounds))
            {
                return false;
            }

            BoardViewport viewport = request.Frame.Viewport;
            if (!viewport.FirstIntersectionX.HasValue
                || !viewport.FirstIntersectionY.HasValue
                || viewport.CellWidth <= 0d
                || viewport.CellHeight <= 0d
                || !IsUsable(viewport.SourceBounds))
            {
                return false;
            }

            double scaleX = bounds.Width / (double)viewport.SourceBounds.Width;
            double scaleY = bounds.Height / (double)viewport.SourceBounds.Height;
            double offsetX =
                (viewport.FirstIntersectionX.Value - viewport.SourceBounds.X)
                + (viewport.CellWidth * request.Move.X);
            double offsetY =
                (viewport.FirstIntersectionY.Value - viewport.SourceBounds.Y)
                + (viewport.CellHeight * request.Move.Y);
            point = new PlacementPoint(
                (int)Math.Round(bounds.X + (offsetX * scaleX)),
                (int)Math.Round(bounds.Y + (offsetY * scaleY)));
            return true;
        }

        private static double ResolveCellSize(int boundsSize, double cellSize, int boardSize)
        {
            if (cellSize > 0d)
                return cellSize;
            return boundsSize / (double)boardSize;
        }

        private static double ResolveBackgroundDpiScale(WindowDescriptor window)
        {
            if (window == null || window.IsDpiAware)
                return 1d;
            if (window.DpiScale <= 0d)
                return 1d;
            return window.DpiScale;
        }

        private MovePlacementResult ExecutePlacement(
            MovePlacementRequest request,
            PlacementPathKind path,
            PlacementPoint point)
        {
            MovePlacementResult cancellationFailure = FailIfCancellationRequested(request, path);
            if (cancellationFailure != null)
                return cancellationFailure;
            if (path == PlacementPathKind.Foreground)
                return PlaceForeground(request, point);
            if (path == PlacementPathKind.BackgroundPost)
                return PlaceBackgroundPost(request, point);
            if (path == PlacementPathKind.BackgroundSend)
                return PlaceBackgroundSend(request, point);
            return Failure(request, path, MovePlacementFailureKind.UnsupportedPath, "Unsupported placement path.");
        }

        private MovePlacementResult PlaceForeground(MovePlacementRequest request, PlacementPoint point)
        {
            MovePlacementResult cancellationFailure = FailIfCancellationRequested(request, PlacementPathKind.Foreground);
            if (cancellationFailure != null)
                return cancellationFailure;
            ActivateTargetWindowIfNeeded(request);
            cancellationFailure = FailIfCancellationRequested(request, PlacementPathKind.Foreground);
            if (cancellationFailure != null)
                return cancellationFailure;
            bool holdButtonBeforeRelease = request.Frame.SyncMode == SyncMode.Fox;
            if (!nativeMethods.TryForegroundLeftClick(point.X, point.Y, holdButtonBeforeRelease))
                return Failure(request, PlacementPathKind.Foreground, MovePlacementFailureKind.PlacementFailed, "Foreground click failed.");
            return Success(request, PlacementPathKind.Foreground);
        }

        private void ActivateTargetWindowIfNeeded(MovePlacementRequest request)
        {
            IntPtr activationHandle = ResolveActivationHandle(request);
            if (activationHandle != IntPtr.Zero)
                nativeMethods.SwitchToWindow(activationHandle);
        }

        private IntPtr ResolveActivationHandle(MovePlacementRequest request)
        {
            if (!ShouldBringTargetToFront(request))
                return IntPtr.Zero;
            if (request.Frame.SyncMode == SyncMode.Fox)
            {
                IntPtr foxDialogHandle = nativeMethods.FindWindowByClass("#32770");
                if (foxDialogHandle != IntPtr.Zero)
                    return foxDialogHandle;
            }
            return request.Frame.Window == null ? IntPtr.Zero : request.Frame.Window.Handle;
        }

        private static bool ShouldBringTargetToFront(MovePlacementRequest request)
        {
            if (request.BringTargetToFront)
                return true;
            if (request.Frame.SyncMode == SyncMode.Fox)
                return true;
            return request.Frame.Window != null
                && request.Frame.Window.IsJavaWindow
                && request.Frame.SyncMode != SyncMode.Foreground;
        }

        private MovePlacementResult PlaceBackgroundPost(MovePlacementRequest request, PlacementPoint point)
        {
            MovePlacementResult cancellationFailure = FailIfCancellationRequested(request, PlacementPathKind.BackgroundPost);
            if (cancellationFailure != null)
                return cancellationFailure;
            IntPtr handle = ResolveTargetHandle(request);
            int lParam = BuildMouseLParam(point.X, point.Y);
            bool downPosted = nativeMethods.TryPostMouseMessage(handle, NativePlacementConstants.WmLButtonDown, 0, lParam);
            bool upPosted = nativeMethods.TryPostMouseMessage(handle, NativePlacementConstants.WmLButtonUp, 0, lParam);
            if (!downPosted || !upPosted)
                return Failure(request, PlacementPathKind.BackgroundPost, MovePlacementFailureKind.PlacementFailed, "PostMessage placement failed.");
            return Success(request, PlacementPathKind.BackgroundPost);
        }

        private MovePlacementResult PlaceBackgroundSend(MovePlacementRequest request, PlacementPoint point)
        {
            MovePlacementResult cancellationFailure = FailIfCancellationRequested(request, PlacementPathKind.BackgroundSend);
            if (cancellationFailure != null)
                return cancellationFailure;
            IntPtr handle = ResolveTargetHandle(request);
            int lParam = BuildMouseLParam(point.X, point.Y);
            nativeMethods.SendMouseMessage(handle, NativePlacementConstants.WmMouseMove, 0, lParam);
            nativeMethods.SendMouseMessage(handle, NativePlacementConstants.WmLButtonDown, NativePlacementConstants.MkLButton, lParam);
            nativeMethods.SendMouseMessage(handle, NativePlacementConstants.WmLButtonUp, 0, lParam);
            return Success(request, PlacementPathKind.BackgroundSend);
        }

        private IntPtr ResolveTargetHandle(MovePlacementRequest request)
        {
            if (request.Frame.Window == null || request.Frame.Window.Handle == IntPtr.Zero)
                throw new InvalidOperationException("Placement target window handle is required.");
            if (request.Frame.SyncMode != SyncMode.Yike)
                return request.Frame.Window.Handle;

            IntPtr renderWidgetHandle = ResolveYikeRenderWidgetHandle(request);
            if (renderWidgetHandle == IntPtr.Zero)
                throw new InvalidOperationException("Yike render widget handle is required.");
            return renderWidgetHandle;
        }

        private IntPtr ResolveYikeRenderWidgetHandle(MovePlacementRequest request)
        {
            if (request == null || request.Frame == null || request.Frame.Window == null)
                return IntPtr.Zero;

            return ResolveYikeRenderWidgetHandle(request.Frame.Window.Handle);
        }

        private IntPtr ResolveYikeRenderWidgetHandle(IntPtr parentHandle)
        {
            return nativeMethods.FindChildWindowByClass(
                parentHandle,
                YikeRenderWidgetClassName);
        }

        private static int BuildMouseLParam(int x, int y)
        {
            return (x & 0xFFFF) | ((y & 0xFFFF) << 16);
        }

        private static bool IsUsable(PixelRect bounds)
        {
            return bounds != null && !bounds.IsEmpty;
        }

        private static MovePlacementResult FailIfCancellationRequested(
            MovePlacementRequest request,
            PlacementPathKind path)
        {
            if (request == null || request.ShouldCancel == null || !request.ShouldCancel())
                return null;
            return Failure(request, path, MovePlacementFailureKind.PlacementFailed, "Placement cancelled.");
        }

        private static MovePlacementResult Success(MovePlacementRequest request, PlacementPathKind path)
        {
            return new MovePlacementResult
            {
                Success = true,
                PlacementPath = path,
                Coordinate = new BoardCoordinate(request.Move.X, request.Move.Y),
                FailureKind = MovePlacementFailureKind.None,
                FailureReason = null
            };
        }

        private static MovePlacementResult Failure(
            MovePlacementRequest request,
            PlacementPathKind path,
            MovePlacementFailureKind failureKind,
            string failureReason)
        {
            BoardCoordinate coordinate = null;
            if (request != null && request.Move != null)
                coordinate = new BoardCoordinate(request.Move.X, request.Move.Y);

            return new MovePlacementResult
            {
                Success = false,
                PlacementPath = path,
                Coordinate = coordinate,
                FailureKind = failureKind,
                FailureReason = failureReason
            };
        }
    }

    internal interface IPlacementNativeMethods
    {
        IntPtr FindWindowByClass(string className);
        IntPtr FindChildWindowByClass(IntPtr parentHandle, string className);
        bool TryGetWindowBounds(IntPtr handle, out PixelRect bounds);
        void SwitchToWindow(IntPtr handle);
        bool TryForegroundLeftClick(int x, int y, bool holdButtonBeforeRelease);
        bool TryPostMouseMessage(IntPtr handle, uint message, int wParam, int lParam);
        void SendMouseMessage(IntPtr handle, uint message, int wParam, int lParam);
    }

    internal interface IPlacementDelay
    {
        void Wait(int millisecondsTimeout);
    }

    internal sealed class User32PlacementNativeMethods : IPlacementNativeMethods
    {
        private readonly IPlacementDelay placementDelay;

        public User32PlacementNativeMethods()
            : this(new BlockingPlacementDelay())
        {
        }

        internal User32PlacementNativeMethods(IPlacementDelay placementDelay)
        {
            if (placementDelay == null)
                throw new ArgumentNullException("placementDelay");

            this.placementDelay = placementDelay;
        }

        public IntPtr FindWindowByClass(string className)
        {
            return FindWindow(className, string.Empty);
        }

        public IntPtr FindChildWindowByClass(IntPtr parentHandle, string className)
        {
            if (parentHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(className))
                return IntPtr.Zero;

            ChildWindowSearchState state = new ChildWindowSearchState(className);
            GCHandle searchStateHandle = GCHandle.Alloc(state);
            try
            {
                EnumChildWindows(parentHandle, ChildWindowSearchCallback, GCHandle.ToIntPtr(searchStateHandle));
                return state.FoundHandle;
            }
            finally
            {
                searchStateHandle.Free();
            }
        }

        public void SwitchToWindow(IntPtr handle)
        {
            SwitchToThisWindow(handle, true);
        }

        public bool TryGetWindowBounds(IntPtr handle, out PixelRect bounds)
        {
            bounds = null;
            RECT rect;
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out rect))
                return false;
            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                return false;

            bounds = new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            return true;
        }

        public bool TryForegroundLeftClick(int x, int y, bool holdButtonBeforeRelease)
        {
            NativePoint cursorPosition;
            bool hasCursorPosition = GetCursorPos(out cursorPosition);
            if (!SetCursorPos(x, y))
                return false;

            mouse_event((int)NativeMouseEventFlags.LeftUp | (int)NativeMouseEventFlags.Absolute, 0, 0, 0, IntPtr.Zero);
            mouse_event((int)NativeMouseEventFlags.LeftDown | (int)NativeMouseEventFlags.Absolute, 0, 0, 0, IntPtr.Zero);
            WaitBeforeReleaseIfNeeded(holdButtonBeforeRelease);
            mouse_event((int)NativeMouseEventFlags.LeftUp | (int)NativeMouseEventFlags.Absolute, 0, 0, 0, IntPtr.Zero);

            if (hasCursorPosition)
                SetCursorPos(cursorPosition.X, cursorPosition.Y);
            return true;
        }

        private void WaitBeforeReleaseIfNeeded(bool holdButtonBeforeRelease)
        {
            if (!holdButtonBeforeRelease)
                return;

            placementDelay.Wait(NativePlacementConstants.FoxForegroundButtonHoldMs);
        }

        public bool TryPostMouseMessage(IntPtr handle, uint message, int wParam, int lParam)
        {
            return PostMessage(handle, message, (IntPtr)wParam, (IntPtr)lParam);
        }

        public void SendMouseMessage(IntPtr handle, uint message, int wParam, int lParam)
        {
            SendMessage(handle, message, (IntPtr)wParam, (IntPtr)lParam);
        }

        [DllImport("USER32.DLL")]
        private static extern void SwitchToThisWindow(IntPtr hwnd, bool fAltTab);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, IntPtr dwExtraInfo);

        private static bool ChildWindowSearchCallback(IntPtr handle, IntPtr lParam)
        {
            GCHandle stateHandle = GCHandle.FromIntPtr(lParam);
            ChildWindowSearchState state = stateHandle.Target as ChildWindowSearchState;
            if (state == null)
                return false;

            string className = GetWindowClassName(handle);
            if (!string.Equals(className, state.ClassName, StringComparison.Ordinal))
                return true;

            state.FoundHandle = handle;
            return false;
        }

        private static string GetWindowClassName(IntPtr handle)
        {
            StringBuilder className = new StringBuilder(256);
            return GetClassName(handle, className, className.Capacity) <= 0
                ? string.Empty
                : className.ToString();
        }

        private sealed class ChildWindowSearchState
        {
            public ChildWindowSearchState(string className)
            {
                ClassName = className;
            }

            public string ClassName { get; private set; }
            public IntPtr FoundHandle { get; set; }
        }

        private delegate bool EnumChildProc(IntPtr handle, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }

    internal sealed class BlockingPlacementDelay : IPlacementDelay
    {
        private static readonly ManualResetEvent DelayGate = new ManualResetEvent(false);

        public void Wait(int millisecondsTimeout)
        {
            if (millisecondsTimeout <= 0)
                return;

            // Fox only registers the foreground click reliably if the button stays down briefly before release.
            DelayGate.WaitOne(millisecondsTimeout);
        }
    }

    internal static class NativePlacementConstants
    {
        internal const int FoxForegroundButtonHoldMs = 50;
        internal const int MkLButton = 0x0001;
        internal const uint WmMouseMove = 0x0200;
        internal const uint WmLButtonDown = 0x0201;
        internal const uint WmLButtonUp = 0x0202;
    }

    internal enum NativeMouseEventFlags
    {
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        Absolute = 0x8000
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    internal struct PlacementPoint
    {
        public static readonly PlacementPoint Empty = new PlacementPoint(0, 0);

        public PlacementPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; private set; }
        public int Y { get; private set; }
    }
}
