using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace readboard
{
    internal interface IBoardCaptureService
    {
        BoardCaptureResult Capture(BoardCaptureRequest request);
    }

    internal sealed class LegacyBoardCaptureService : IBoardCaptureService
    {
        private const double DefaultDpiScale = 1.0d;
        private readonly IBoardCapturePlatform platform;
        private ulong previousContentSignature;
        private int previousPixelWidth;
        private int previousPixelHeight;
        private int previousPixelStride;
        private bool hasPreviousFrameContent;

        public LegacyBoardCaptureService(IBoardCapturePlatform platform)
        {
            if (platform == null)
                throw new ArgumentNullException("platform");

            this.platform = platform;
        }

        public BoardCaptureResult Capture(BoardCaptureRequest request)
        {
            if (request == null)
                throw new ArgumentNullException("request");

            if (request.SyncMode == SyncMode.Foreground)
                return CaptureScreenSelection(request);

            return CaptureWindow(request);
        }

        private BoardCaptureResult CaptureScreenSelection(BoardCaptureRequest request)
        {
            PixelRect selection = CloneRect(request.SelectionBounds);
            if (!IsPositiveRect(selection))
                return BoardCaptureResult.CreateFailure(BoardCaptureFailureKind.InvalidSelection, "Foreground capture requires a positive selection.");

            Bitmap output = platform.CaptureScreen(selection);
            if (output == null)
                return BoardCaptureResult.CreateFailure(BoardCaptureFailureKind.CaptureFailed, "CopyFromScreen returned no bitmap.");

            try
            {
                WindowDescriptor window = CreateScreenDescriptor(request.Window, selection);
                BoardFrame frame = BuildFrame(request, window, selection, selection, output, false);
                output = null;
                return BoardCaptureResult.CreateSuccess(frame, CapturePathKind.ScreenCopy);
            }
            finally
            {
                if (output != null)
                    output.Dispose();
            }
        }

        private BoardCaptureResult CaptureWindow(BoardCaptureRequest request)
        {
            BoardCapturePlan plan;
            BoardCaptureResult failure = TryBuildWindowPlan(request, out plan);
            if (failure != null)
                return failure;

            Bitmap source = CaptureWindowBitmap(plan);
            if (source == null)
                return BoardCaptureResult.CreateFailure(BoardCaptureFailureKind.CaptureFailed, "Window capture returned no bitmap.");

            Bitmap output = null;
            try
            {
                output = BitmapProjection.CreateProjectedBitmap(source, plan.SourceBounds);
                BoardFrame frame = BuildFrame(request, plan.Window, plan.SourceBounds, plan.ScreenBounds, output, plan.UsePrintWindow);
                output = null;
                return BoardCaptureResult.CreateSuccess(frame, plan.CapturePath);
            }
            finally
            {
                source.Dispose();
                if (output != null)
                    output.Dispose();
            }
        }

        private BoardCaptureResult TryBuildWindowPlan(BoardCaptureRequest request, out BoardCapturePlan plan)
        {
            plan = null;
            IntPtr handle = GetWindowHandle(request.Window);
            if (handle == IntPtr.Zero)
                return BoardCaptureResult.CreateFailure(BoardCaptureFailureKind.WindowUnavailable, "Target window handle is missing.");

            WindowDescriptor liveWindow;
            if (!platform.TryDescribeWindow(handle, request.Window, out liveWindow))
                return BoardCaptureResult.CreateFailure(BoardCaptureFailureKind.WindowUnavailable, "Failed to resolve the target window.");

            CropResolution crop = ResolveWindowCrop(request, liveWindow);
            if (crop == null)
                return BoardCaptureResult.CreateFailure(BoardCaptureFailureKind.InvalidSelection, "Selection is outside the target window.");

            bool usePrintWindow = ShouldUsePrintWindow(request, liveWindow);
            plan = new BoardCapturePlan
            {
                Window = liveWindow,
                SourceBounds = crop.SourceBounds,
                ScreenBounds = crop.ScreenBounds,
                CapturePath = CapturePathKind.WindowBitmap,
                UsePrintWindow = usePrintWindow
            };
            return null;
        }

        private CropResolution ResolveWindowCrop(BoardCaptureRequest request, WindowDescriptor window)
        {
            PixelRect selection = CloneRect(request.SelectionBounds);
            PixelRect fullWindow = new PixelRect(0, 0, window.Bounds.Width, window.Bounds.Height);
            if (!IsPositiveRect(selection))
                return new CropResolution
                {
                    SourceBounds = fullWindow,
                    ScreenBounds = CloneRect(window.Bounds)
                };

            CropResolution screenCrop = TryResolveScreenRelativeCrop(selection, window);
            if (screenCrop != null)
                return screenCrop;

            CropResolution localCrop = TryResolveLocalCrop(selection, window);
            if (localCrop != null)
                return localCrop;

            return null;
        }

        private CropResolution TryResolveScreenRelativeCrop(PixelRect selection, WindowDescriptor window)
        {
            double scale = GetEffectiveScale(window);
            int offsetX = selection.X - window.Bounds.X;
            int offsetY = selection.Y - window.Bounds.Y;
            PixelRect candidate = window.IsDpiAware
                ? new PixelRect(offsetX, offsetY, selection.Width, selection.Height)
                : ScaleRectDown(offsetX, offsetY, selection.Width, selection.Height, scale);

            if (!IsCropWithinWindow(candidate, window.Bounds))
                return null;

            return new CropResolution
            {
                SourceBounds = candidate,
                ScreenBounds = CloneRect(selection)
            };
        }

        private CropResolution TryResolveLocalCrop(PixelRect selection, WindowDescriptor window)
        {
            PixelRect candidate = new PixelRect(selection.X, selection.Y, selection.Width, selection.Height);
            if (!IsCropWithinWindow(candidate, window.Bounds))
                return null;

            return new CropResolution
            {
                SourceBounds = candidate,
                ScreenBounds = CreateScreenBounds(window, candidate)
            };
        }

        private bool ShouldUsePrintWindow(BoardCaptureRequest request, WindowDescriptor window)
        {
            if (window.IsJavaWindow)
                return true;

            bool allowEnhancedPath = request.PreferPrintWindow || request.IsInitialProbe;
            if (!allowEnhancedPath || !request.UseEnhancedCapture)
                return false;

            PixelRect screenBounds = CloneRect(request.SelectionBounds);
            if (!IsPositiveRect(screenBounds))
                screenBounds = CloneRect(window.Bounds);

            PixelRect screen = platform.GetPrimaryScreenBounds();
            if (screenBounds.Y < 0)
                return true;

            int bottom = screenBounds.Y + screenBounds.Height;
            if (IsPositiveRect(request.SelectionBounds))
                return screenBounds.X < 0 || screenBounds.X > screen.Width || bottom > screen.Height;

            return screenBounds.X < 0 || screenBounds.X > screen.Width || window.Bounds.Y < 0 || bottom > screen.Height;
        }

        private Bitmap CaptureWindowBitmap(BoardCapturePlan plan)
        {
            return plan.UsePrintWindow
                ? platform.CapturePrintWindow(plan.Window.Handle)
                : platform.CaptureWindow(plan.Window.Handle);
        }

        private BoardFrame BuildFrame(
            BoardCaptureRequest request,
            WindowDescriptor window,
            PixelRect sourceBounds,
            PixelRect screenBounds,
            Bitmap output,
            bool usedPrintWindow)
        {
            ulong contentSignature;
            PixelBuffer pixelBuffer = PixelBufferConverter.FromBitmap(output, out contentSignature);
            BoardFrame frame = new BoardFrame
            {
                Window = CloneWindow(window),
                SyncMode = request.SyncMode,
                BoardSize = CloneBoardSize(request.BoardSize),
                Viewport = CreateViewport(request.BoardSize, sourceBounds, screenBounds, output),
                Image = output,
                PixelBuffer = pixelBuffer,
                PreferPrintWindow = request.PreferPrintWindow || request.IsInitialProbe,
                UsedPrintWindow = usedPrintWindow,
                ContentSignature = contentSignature
            };
            TrackFrameContent(frame);
            return frame;
        }

        private void TrackFrameContent(BoardFrame frame)
        {
            if (frame == null || frame.PixelBuffer == null)
                return;

            frame.HasSameContentAsPreviousCapture = IsSameAsPreviousContent(frame.PixelBuffer, frame.ContentSignature);
            RememberFrameContent(frame.PixelBuffer, frame.ContentSignature);
        }

        private bool IsSameAsPreviousContent(PixelBuffer pixelBuffer, ulong contentSignature)
        {
            if (!hasPreviousFrameContent || pixelBuffer == null || contentSignature == 0UL)
                return false;

            return contentSignature == previousContentSignature
                && pixelBuffer.Width == previousPixelWidth
                && pixelBuffer.Height == previousPixelHeight
                && pixelBuffer.Stride == previousPixelStride;
        }

        private void RememberFrameContent(PixelBuffer pixelBuffer, ulong contentSignature)
        {
            if (pixelBuffer == null || contentSignature == 0UL)
            {
                hasPreviousFrameContent = false;
                return;
            }

            previousContentSignature = contentSignature;
            previousPixelWidth = pixelBuffer.Width;
            previousPixelHeight = pixelBuffer.Height;
            previousPixelStride = pixelBuffer.Stride;
            hasPreviousFrameContent = true;
        }

        private BoardViewport CreateViewport(BoardDimensions boardSize, PixelRect sourceBounds, PixelRect screenBounds, Bitmap output)
        {
            BoardViewport viewport = new BoardViewport
            {
                SourceBounds = CloneRect(sourceBounds),
                ScreenBounds = CloneRect(screenBounds)
            };
            if (boardSize == null || boardSize.Width <= 0 || boardSize.Height <= 0)
                return viewport;

            viewport.CellWidth = output.Width / (double)boardSize.Width;
            viewport.CellHeight = output.Height / (double)boardSize.Height;
            return viewport;
        }

        private WindowDescriptor CreateScreenDescriptor(WindowDescriptor requestWindow, PixelRect selection)
        {
            WindowDescriptor window = CloneWindow(requestWindow) ?? new WindowDescriptor();
            window.Handle = IntPtr.Zero;
            window.Bounds = CloneRect(selection);
            window.IsDpiAware = true;
            window.DpiScale = platform.GetDesktopDpiScale();
            window.IsJavaWindow = false;
            return window;
        }

        private PixelRect CreateScreenBounds(WindowDescriptor window, PixelRect localBounds)
        {
            double scale = GetEffectiveScale(window);
            if (window.IsDpiAware)
                return new PixelRect(window.Bounds.X + localBounds.X, window.Bounds.Y + localBounds.Y, localBounds.Width, localBounds.Height);

            return ScaleRectUp(window.Bounds.X, window.Bounds.Y, localBounds, scale);
        }

        private static PixelRect ScaleRectDown(int x, int y, int width, int height, double scale)
        {
            double safeScale = scale <= 0d ? DefaultDpiScale : scale;
            return new PixelRect((int)(x / safeScale), (int)(y / safeScale), (int)(width / safeScale), (int)(height / safeScale));
        }

        private static PixelRect ScaleRectUp(int originX, int originY, PixelRect localBounds, double scale)
        {
            double safeScale = scale <= 0d ? DefaultDpiScale : scale;
            int x = originX + (int)(localBounds.X * safeScale);
            int y = originY + (int)(localBounds.Y * safeScale);
            int width = (int)(localBounds.Width * safeScale);
            int height = (int)(localBounds.Height * safeScale);
            return new PixelRect(x, y, width, height);
        }

        private static bool IsCropWithinWindow(PixelRect crop, PixelRect windowBounds)
        {
            if (!IsPositiveRect(crop))
                return false;

            int right = crop.X + crop.Width;
            int bottom = crop.Y + crop.Height;
            return crop.X >= 0 && crop.Y >= 0 && right <= windowBounds.Width && bottom <= windowBounds.Height;
        }

        private static bool IsPositiveRect(PixelRect rect)
        {
            return rect != null && rect.Width > 0 && rect.Height > 0;
        }

        private static double GetEffectiveScale(WindowDescriptor window)
        {
            if (window == null || window.DpiScale <= 0d)
                return DefaultDpiScale;

            return window.DpiScale;
        }

        private static IntPtr GetWindowHandle(WindowDescriptor window)
        {
            return window == null ? IntPtr.Zero : window.Handle;
        }

        private static WindowDescriptor CloneWindow(WindowDescriptor window)
        {
            if (window == null)
                return null;

            return new WindowDescriptor
            {
                Handle = window.Handle,
                ClassName = window.ClassName,
                Title = window.Title,
                Bounds = CloneRect(window.Bounds),
                IsDpiAware = window.IsDpiAware,
                DpiScale = window.DpiScale,
                IsJavaWindow = window.IsJavaWindow
            };
        }

        private static PixelRect CloneRect(PixelRect rect)
        {
            return rect == null ? null : new PixelRect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private static BoardDimensions CloneBoardSize(BoardDimensions boardSize)
        {
            return boardSize == null ? null : new BoardDimensions(boardSize.Width, boardSize.Height);
        }
    }

    internal interface IBoardCapturePlatform
    {
        double GetDesktopDpiScale();
        PixelRect GetPrimaryScreenBounds();
        bool TryDescribeWindow(IntPtr handle, WindowDescriptor seed, out WindowDescriptor descriptor);
        Bitmap CaptureWindow(IntPtr handle);
        Bitmap CapturePrintWindow(IntPtr handle);
        Bitmap CaptureScreen(PixelRect bounds);
    }

    internal sealed class Win32BoardCapturePlatform : IBoardCapturePlatform
    {
        private const int SRCCOPY = 0x00CC0020;
        private const string JavaFrameClassName = "SunAwtFrame";
        private const double BaseDpi = 96d;
        private const int ProcessDpiAwarenessOk = 0;

        public double GetDesktopDpiScale()
        {
            using (Bitmap bitmap = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                double scale = graphics.DpiX / BaseDpi;
                return scale > 0d ? scale : 1.0d;
            }
        }

        public PixelRect GetPrimaryScreenBounds()
        {
            Screen screen = Screen.PrimaryScreen;
            if (screen == null)
                return new PixelRect(0, 0, 0, 0);

            Rectangle bounds = screen.Bounds;
            return new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        public bool TryDescribeWindow(IntPtr handle, WindowDescriptor seed, out WindowDescriptor descriptor)
        {
            descriptor = null;
            RECT rect;
            if (!GetWindowRect(handle, out rect))
                return false;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                return false;

            string className = GetWindowClassName(handle);
            string resolvedClassName = string.IsNullOrEmpty(className) ? GetSeedClassName(seed) : className;
            descriptor = new WindowDescriptor
            {
                Handle = handle,
                ClassName = resolvedClassName,
                Title = GetSeedTitle(seed, handle),
                Bounds = new PixelRect(rect.Left, rect.Top, width, height),
                IsDpiAware = GetSupportDpiState(handle),
                DpiScale = GetDesktopDpiScale(),
                IsJavaWindow = string.Equals(resolvedClassName, JavaFrameClassName, StringComparison.Ordinal)
            };
            return true;
        }

        public Bitmap CaptureWindow(IntPtr handle)
        {
            RECT rect;
            if (!GetWindowRect(handle, out rect))
                return null;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                return null;

            IntPtr windowDc = IntPtr.Zero;
            IntPtr memoryDc = IntPtr.Zero;
            IntPtr bitmapHandle = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                windowDc = GetDC(handle);
                memoryDc = CreateCompatibleDC(windowDc);
                bitmapHandle = CreateCompatibleBitmap(windowDc, width, height);
                oldBitmap = SelectObject(memoryDc, bitmapHandle);
                BitBlt(memoryDc, 0, 0, width, height, windowDc, 0, 0, SRCCOPY);
                return Bitmap.FromHbitmap(bitmapHandle);
            }
            catch
            {
                return null;
            }
            finally
            {
                RestoreBitmap(memoryDc, oldBitmap);
                DeleteNative(bitmapHandle, memoryDc, windowDc, handle);
            }
        }

        public Bitmap CapturePrintWindow(IntPtr handle)
        {
            RECT rect;
            if (!GetWindowRect(handle, out rect))
                return null;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                return null;

            Bitmap bitmap = new Bitmap(width, height);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr dc = graphics.GetHdc();
                    try
                    {
                        PrintWindow(handle, dc, 0u);
                    }
                    finally
                    {
                        graphics.ReleaseHdc(dc);
                    }
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                return null;
            }
        }

        public Bitmap CaptureScreen(PixelRect bounds)
        {
            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Size(bounds.Width, bounds.Height));
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                return null;
            }
        }

        private static string GetSeedClassName(WindowDescriptor seed)
        {
            return seed == null ? string.Empty : seed.ClassName;
        }

        private static string GetSeedTitle(WindowDescriptor seed, IntPtr handle)
        {
            string title = GetWindowTitle(handle);
            if (!string.IsNullOrEmpty(title))
                return title;

            return seed == null ? string.Empty : seed.Title;
        }

        private static string GetWindowClassName(IntPtr handle)
        {
            StringBuilder className = new StringBuilder(256);
            GetClassName(handle, className, className.Capacity);
            return className.ToString();
        }

        private static string GetWindowTitle(IntPtr handle)
        {
            StringBuilder title = new StringBuilder(256);
            GetWindowText(handle, title, title.Capacity);
            return title.ToString();
        }

        private static bool GetSupportDpiState(IntPtr handle)
        {
            int processId;
            GetWindowThreadProcessId(handle, out processId);
            if (processId <= 0)
                return false;

            Process process = null;
            try
            {
                process = Process.GetProcessById(processId);
                PROCESS_DPI_AWARENESS awareness;
                int result = GetProcessDpiAwareness(process.Handle, out awareness);
                return result == ProcessDpiAwarenessOk && awareness != PROCESS_DPI_AWARENESS.PROCESS_DPI_UNAWARE;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (process != null)
                    process.Dispose();
            }
        }

        private static void RestoreBitmap(IntPtr memoryDc, IntPtr oldBitmap)
        {
            if (memoryDc != IntPtr.Zero && oldBitmap != IntPtr.Zero)
                SelectObject(memoryDc, oldBitmap);
        }

        private static void DeleteNative(IntPtr bitmapHandle, IntPtr memoryDc, IntPtr windowDc, IntPtr handle)
        {
            if (bitmapHandle != IntPtr.Zero)
                DeleteObject(bitmapHandle);
            if (memoryDc != IntPtr.Zero)
                DeleteDC(memoryDc);
            if (windowDc != IntPtr.Zero)
                ReleaseDC(handle, windowDc);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("shcore.dll")]
        private static extern int GetProcessDpiAwareness(IntPtr hprocess, out PROCESS_DPI_AWARENESS value);

        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        private static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private enum PROCESS_DPI_AWARENESS
        {
            PROCESS_DPI_UNAWARE = 0,
            PROCESS_SYSTEM_DPI_AWARE = 1,
            PROCESS_PER_MONITOR_DPI_AWARE = 2
        }
    }

    internal static class BitmapProjection
    {
        public static Bitmap CreateProjectedBitmap(Bitmap source, PixelRect sourceBounds)
        {
            Bitmap bitmap = new Bitmap(sourceBounds.Width, sourceBounds.Height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(
                    source,
                    new Rectangle(0, 0, sourceBounds.Width, sourceBounds.Height),
                    new Rectangle(sourceBounds.X, sourceBounds.Y, sourceBounds.Width, sourceBounds.Height),
                    GraphicsUnit.Pixel);
            }
            return bitmap;
        }
    }

    internal static class PixelBufferConverter
    {
        private const int BytesPerPixel = 3;

        public static PixelBuffer FromBitmap(Bitmap bitmap)
        {
            ulong contentSignature;
            return FromBitmap(bitmap, out contentSignature);
        }

        public static PixelBuffer FromBitmap(Bitmap bitmap, out ulong contentSignature)
        {
            Rectangle bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData bitmapData = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                return CreatePixelBuffer(bitmapData, bitmap.Width, bitmap.Height, out contentSignature);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }

        private static PixelBuffer CreatePixelBuffer(BitmapData bitmapData, int width, int height, out ulong contentSignature)
        {
            int packedStride = width * BytesPerPixel;
            byte[] pixels = new byte[packedStride * height];
            ulong hash = BoardContentHash.Start();
            for (int row = 0; row < height; row++)
            {
                hash = CopyRow(bitmapData, row, width, pixels, packedStride, hash);
            }

            contentSignature = BoardContentHash.Finish(hash, width, height, packedStride);
            return new PixelBuffer
            {
                Format = PixelBufferFormat.Rgb24,
                Width = width,
                Height = height,
                Stride = packedStride,
                Pixels = pixels
            };
        }

        private static ulong CopyRow(BitmapData bitmapData, int row, int width, byte[] pixels, int packedStride, ulong hash)
        {
            int sourceOffset = row * bitmapData.Stride;
            int targetOffset = row * packedStride;
            for (int column = 0; column < width; column++)
            {
                int sourceIndex = sourceOffset + (column * BytesPerPixel);
                int targetIndex = targetOffset + (column * BytesPerPixel);
                byte red = Marshal.ReadByte(bitmapData.Scan0, sourceIndex + 2);
                byte green = Marshal.ReadByte(bitmapData.Scan0, sourceIndex + 1);
                byte blue = Marshal.ReadByte(bitmapData.Scan0, sourceIndex);
                pixels[targetIndex] = red;
                pixels[targetIndex + 1] = green;
                pixels[targetIndex + 2] = blue;
                hash = BoardContentHash.AppendByte(hash, red);
                hash = BoardContentHash.AppendByte(hash, green);
                hash = BoardContentHash.AppendByte(hash, blue);
            }

            return hash;
        }
    }
}
