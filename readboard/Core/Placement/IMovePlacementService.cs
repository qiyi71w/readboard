using System;
using System.Runtime.InteropServices;
using System.Threading;
using LwInterop = Interop.lw;

namespace readboard
{
    internal interface IMovePlacementService
    {
        MovePlacementResult Place(MovePlacementRequest request);
    }

    internal sealed class LegacyMovePlacementService : IMovePlacementService
    {
        private readonly IPlacementNativeMethods nativeMethods;
        private readonly IPlacementLightweightInteropFactory lightweightInteropFactory;

        internal LegacyMovePlacementService(
            IPlacementNativeMethods nativeMethods,
            IPlacementLightweightInteropFactory lightweightInteropFactory)
        {
            if (nativeMethods == null)
                throw new ArgumentNullException("nativeMethods");
            if (lightweightInteropFactory == null)
                throw new ArgumentNullException("lightweightInteropFactory");

            this.nativeMethods = nativeMethods;
            this.lightweightInteropFactory = lightweightInteropFactory;
        }

        internal static LegacyMovePlacementService CreateDefault()
        {
            return new LegacyMovePlacementService(
                new User32PlacementNativeMethods(),
                new PlacementLightweightInteropFactory());
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
            if (syncMode == SyncMode.Fox && request.UseLightweightInterop)
                return PlacementPathKind.LightweightInterop;
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

        private static bool TryResolvePlacementPoint(
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

        private static bool TryResolveBounds(BoardFrame frame, PlacementPathKind path, out PixelRect bounds)
        {
            if (path == PlacementPathKind.Foreground)
                return TryResolveScreenBounds(frame, out bounds);
            return TryResolveClientBounds(frame, out bounds);
        }

        private static bool TryResolveScreenBounds(BoardFrame frame, out PixelRect bounds)
        {
            bounds = frame.Viewport.ScreenBounds;
            if (IsUsable(bounds))
                return true;
            if (!IsUsable(frame.Viewport.SourceBounds) || !IsUsable(frame.Window == null ? null : frame.Window.Bounds))
                return false;

            PixelRect sourceBounds = frame.Viewport.SourceBounds;
            PixelRect windowBounds = frame.Window.Bounds;
            bounds = new PixelRect(
                windowBounds.X + sourceBounds.X,
                windowBounds.Y + sourceBounds.Y,
                sourceBounds.Width,
                sourceBounds.Height);
            return true;
        }

        private static bool TryResolveClientBounds(BoardFrame frame, out PixelRect bounds)
        {
            bounds = frame.Viewport.SourceBounds;
            if (IsUsable(bounds))
                return true;
            if (!IsUsable(frame.Viewport.ScreenBounds) || !IsUsable(frame.Window == null ? null : frame.Window.Bounds))
                return false;

            PixelRect screenBounds = frame.Viewport.ScreenBounds;
            PixelRect windowBounds = frame.Window.Bounds;
            bounds = new PixelRect(
                screenBounds.X - windowBounds.X,
                screenBounds.Y - windowBounds.Y,
                screenBounds.Width,
                screenBounds.Height);
            return true;
        }

        private static PlacementPoint BuildPlacementPoint(
            MovePlacementRequest request,
            PlacementPathKind path,
            PixelRect bounds)
        {
            double cellWidth = ResolveCellSize(bounds.Width, request.Frame.Viewport.CellWidth, request.Frame.BoardSize.Width);
            double cellHeight = ResolveCellSize(bounds.Height, request.Frame.Viewport.CellHeight, request.Frame.BoardSize.Height);
            int x = (int)Math.Round(bounds.X + cellWidth * (request.Move.X + 0.5d));
            int y = (int)Math.Round(bounds.Y + cellHeight * (request.Move.Y + 0.5d));

            if (path != PlacementPathKind.BackgroundPost)
                return new PlacementPoint(x, y);

            double dpiScale = ResolveBackgroundDpiScale(request.Frame.Window);
            return new PlacementPoint(
                (int)Math.Round(x * dpiScale),
                (int)Math.Round(y * dpiScale));
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
            if (path == PlacementPathKind.Foreground)
                return PlaceForeground(request, point);
            if (path == PlacementPathKind.BackgroundPost)
                return PlaceBackgroundPost(request, point);
            if (path == PlacementPathKind.BackgroundSend)
                return PlaceBackgroundSend(request, point);
            return PlaceLightweight(request, point);
        }

        private MovePlacementResult PlaceForeground(MovePlacementRequest request, PlacementPoint point)
        {
            ActivateTargetWindowIfNeeded(request);
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
            IntPtr handle = ResolveTargetHandle(request);
            int lParam = BuildMouseLParam(point.X, point.Y);
            nativeMethods.SendMouseMessage(handle, NativePlacementConstants.WmMouseMove, 0, lParam);
            nativeMethods.SendMouseMessage(handle, NativePlacementConstants.WmLButtonDown, NativePlacementConstants.MkLButton, lParam);
            nativeMethods.SendMouseMessage(handle, NativePlacementConstants.WmLButtonUp, 0, lParam);
            return Success(request, PlacementPathKind.BackgroundSend);
        }

        private MovePlacementResult PlaceLightweight(MovePlacementRequest request, PlacementPoint point)
        {
            IntPtr handle = ResolveTargetHandle(request);
            using (IPlacementLightweightInteropClient client = lightweightInteropFactory.Create())
            {
                if (!client.BindWindow(handle))
                    return Failure(request, PlacementPathKind.LightweightInterop, MovePlacementFailureKind.PlacementFailed, "lw BindWindow failed.");
                client.MoveTo(point.X, point.Y);
                client.LeftClick();
            }
            return Success(request, PlacementPathKind.LightweightInterop);
        }

        private static IntPtr ResolveTargetHandle(MovePlacementRequest request)
        {
            if (request.Frame.Window == null || request.Frame.Window.Handle == IntPtr.Zero)
                throw new InvalidOperationException("Placement target window handle is required.");
            return request.Frame.Window.Handle;
        }

        private static int BuildMouseLParam(int x, int y)
        {
            return (x & 0xFFFF) | ((y & 0xFFFF) << 16);
        }

        private static bool IsUsable(PixelRect bounds)
        {
            return bounds != null && !bounds.IsEmpty;
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
        void SwitchToWindow(IntPtr handle);
        bool TryForegroundLeftClick(int x, int y, bool holdButtonBeforeRelease);
        bool TryPostMouseMessage(IntPtr handle, uint message, int wParam, int lParam);
        void SendMouseMessage(IntPtr handle, uint message, int wParam, int lParam);
    }

    internal interface IPlacementDelay
    {
        void Wait(int millisecondsTimeout);
    }

    internal interface IPlacementLightweightInteropFactory
    {
        IPlacementLightweightInteropClient Create();
    }

    internal interface IPlacementLightweightInteropClient : IDisposable
    {
        bool BindWindow(IntPtr handle);
        void MoveTo(int x, int y);
        void LeftClick();
    }

    internal sealed class PlacementLightweightInteropFactory : IPlacementLightweightInteropFactory
    {
        public IPlacementLightweightInteropClient Create()
        {
            return new PlacementLightweightInteropClient();
        }
    }

    internal sealed class PlacementLightweightInteropClient : IPlacementLightweightInteropClient
    {
        private readonly dynamic lightweightInterop;
        private bool isBound;

        public PlacementLightweightInteropClient()
        {
            lightweightInterop = new LwInterop.lwsoft();
        }

        public bool BindWindow(IntPtr handle)
        {
            object result = lightweightInterop.BindWindow((int)handle, 0, 4, 0, 0, 0);
            isBound = ToBoolean(result);
            return isBound;
        }

        public void MoveTo(int x, int y)
        {
            lightweightInterop.MoveTo(x, y);
        }

        public void LeftClick()
        {
            lightweightInterop.LeftClick();
        }

        public void Dispose()
        {
            if (!isBound)
                return;

            lightweightInterop.UnBindWindow();
            isBound = false;
        }

        private static bool ToBoolean(object value)
        {
            if (value == null)
                return false;
            if (value is bool)
                return (bool)value;
            if (value is int)
                return (int)value != 0;
            return true;
        }
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

        public void SwitchToWindow(IntPtr handle)
        {
            SwitchToThisWindow(handle, true);
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
            return PostMessage(handle, message, wParam, lParam);
        }

        public void SendMouseMessage(IntPtr handle, uint message, int wParam, int lParam)
        {
            SendMessage(handle, message, wParam, lParam);
        }

        [DllImport("USER32.DLL")]
        private static extern void SwitchToThisWindow(IntPtr hwnd, bool fAltTab);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, int wParam, int lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, int wParam, int lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, IntPtr dwExtraInfo);
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
