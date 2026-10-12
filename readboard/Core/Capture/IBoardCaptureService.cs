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

            Bitmap source = CaptureWindowBitmap(plan, request);
            if (source == null)
                return BoardCaptureResult.CreateFailure(BoardCaptureFailureKind.CaptureFailed, "Window capture returned no bitmap.");

            Bitmap output = null;
            try
            {
                if (request.SyncMode == SyncMode.Background
                    && !IsCropWithinWindow(plan.SourceBounds, new PixelRect(0, 0, source.Width, source.Height)))
                {
                    request.BackgroundSelection.Invalidate();
                    return BoardCaptureResult.CreateFailure(BoardCaptureFailureKind.InvalidSelection, "Selection is outside the captured image.");
                }
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
            {
                if (request.BackgroundSelection != null)
                    request.BackgroundSelection.Invalidate();
                return BoardCaptureResult.CreateFailure(BoardCaptureFailureKind.WindowUnavailable, "Target window handle is missing.");
            }

            WindowDescriptor liveWindow;
            if (!platform.TryDescribeWindow(handle, request.Window, out liveWindow))
            {
                if (request.BackgroundSelection != null)
                    request.BackgroundSelection.Invalidate();
                return BoardCaptureResult.CreateFailure(BoardCaptureFailureKind.WindowUnavailable, "Failed to resolve the target window.");
            }

            PixelRect screenSelection = null;
            if (request.SyncMode == SyncMode.Background)
            {
                BackgroundSelectionBinding binding = request.BackgroundSelection;
                if (binding == null)
                    return BoardCaptureResult.CreateFailure(BoardCaptureFailureKind.InvalidSelection, "Background capture requires a bound selection.");
                if (binding.Handle != handle || !binding.TryProject(liveWindow, out screenSelection))
                {
                    binding.Invalidate();
                    return BoardCaptureResult.CreateFailure(BoardCaptureFailureKind.InvalidSelection, "Background selection requires reselection.");
                }
            }

            bool usePrintWindow = ShouldUsePrintWindow(request, liveWindow, screenSelection);
            CropResolution crop = ResolveWindowCrop(liveWindow, screenSelection, usePrintWindow);
            if (crop == null)
                return BoardCaptureResult.CreateFailure(BoardCaptureFailureKind.InvalidSelection, "Selection is outside the target window.");
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

        private static CropResolution ResolveWindowCrop(WindowDescriptor window, PixelRect screenSelection, bool usePrintWindow)
        {
            if (screenSelection == null)
                return new CropResolution
                {
                    SourceBounds = new PixelRect(0, 0, window.Bounds.Width, window.Bounds.Height),
                    ScreenBounds = CloneRect(window.Bounds)
                };

            PixelRect sourceOrigin = usePrintWindow ? window.Bounds : window.ClientBounds;
            PixelRect sourceBounds = DisplayScaling.ScreenToClientBounds(
                screenSelection, sourceOrigin, window.IsDpiAware, GetEffectiveScale(window));
            if (!IsPositiveRect(sourceBounds) || sourceBounds.X < 0 || sourceBounds.Y < 0)
                return null;
            return new CropResolution
            {
                SourceBounds = sourceBounds,
                ScreenBounds = screenSelection
            };
        }

        private bool ShouldUsePrintWindow(BoardCaptureRequest request, WindowDescriptor window, PixelRect screenSelection)
        {
            if (request.SyncMode == SyncMode.Yike || IsBrowserBackgroundCapture(request, window))
                return true;
            if (window.IsJavaWindow)
                return true;

            bool allowEnhancedPath = request.PreferPrintWindow || request.IsInitialProbe;
            if (!allowEnhancedPath || !request.UseEnhancedCapture)
                return false;

            PixelRect screenBounds = screenSelection ?? window.Bounds;
            PixelRect screen = platform.GetVirtualScreenBounds();
            return screenBounds.X < screen.X
                || screenBounds.Y < screen.Y
                || (long)screenBounds.X + screenBounds.Width > (long)screen.X + screen.Width
                || (long)screenBounds.Y + screenBounds.Height > (long)screen.Y + screen.Height;
        }

        private static bool IsBrowserBackgroundCapture(BoardCaptureRequest request, WindowDescriptor window)
        {
            return request.SyncMode == SyncMode.Background
                && IsBrowserWindowClass(window.ClassName);
        }

        private static bool IsBrowserWindowClass(string className)
        {
            return string.Equals(className, "Chrome_RenderWidgetHostHWND", StringComparison.Ordinal)
                || string.Equals(className, "Chrome_WidgetWin_1", StringComparison.Ordinal)
                || string.Equals(className, "MozillaWindowClass", StringComparison.Ordinal);
        }

        private Bitmap CaptureWindowBitmap(BoardCapturePlan plan, BoardCaptureRequest request)
        {
            if (plan.UsePrintWindow)
            {
                return request.SyncMode == SyncMode.Yike || IsBrowserBackgroundCapture(request, plan.Window)
                    ? platform.CapturePrintWindowFullContent(plan.Window.Handle)
                    : platform.CapturePrintWindow(plan.Window.Handle);
            }
            return platform.CaptureWindow(plan.Window.Handle);
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
            window.DpiScale = platform.GetScaleForPoint(new Point(selection.X + selection.Width / 2, selection.Y + selection.Height / 2));
            window.IsJavaWindow = false;
            return window;
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
            return window == null ? DisplayScaling.DefaultScale : DisplayScaling.NormalizeScale(window.DpiScale);
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
                ClientBounds = CloneRect(window.ClientBounds),
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
        double GetScaleForPoint(Point point);
        PixelRect GetVirtualScreenBounds();
        bool TryDescribeWindow(IntPtr handle, WindowDescriptor seed, out WindowDescriptor descriptor);
        Bitmap CaptureWindow(IntPtr handle);
        Bitmap CapturePrintWindow(IntPtr handle);
        Bitmap CapturePrintWindowFullContent(IntPtr handle);
        Bitmap CaptureScreen(PixelRect bounds);
    }

    internal sealed class Win32BoardCapturePlatform : IBoardCapturePlatform
    {
        private const int SRCCOPY = 0x00CC0020;
        private const string JavaFrameClassName = "SunAwtFrame";
        private const int ProcessDpiAwarenessOk = 0;

        public double GetScaleForPoint(Point point)
        {
            return DisplayScaling.GetScaleForPoint(point);
        }

        public PixelRect GetVirtualScreenBounds()
        {
            return DisplayScaling.GetVirtualScreenBounds();
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
            PixelRect bounds = new PixelRect(rect.Left, rect.Top, width, height);
            bool isDpiAware = GetSupportDpiState(handle);
            descriptor = new WindowDescriptor
            {
                Handle = handle,
                ClassName = className,
                Title = GetSeedTitle(seed, handle),
                Bounds = bounds,
                ClientBounds = GetPhysicalClientBounds(handle),
                IsDpiAware = isDpiAware,
                DpiScale = DisplayScaling.ResolveWindowScale(
                    isDpiAware,
                    DisplayScaling.GetScaleForWindow(handle),
                    DisplayScaling.GetScaleForWindowBounds(bounds)),
                IsJavaWindow = string.Equals(className, JavaFrameClassName, StringComparison.Ordinal)
            };
            return true;
        }

        internal static PixelRect GetPhysicalClientBounds(IntPtr handle)
        {
            // Query both client corners in a per-monitor-aware caller context so an
            // unaware target cannot virtualize the physical screen geometry.
            IntPtr previousContext = SetThreadDpiAwarenessContext(new IntPtr(-4));
            if (previousContext == IntPtr.Zero)
                return null;
            try
            {
                RECT client;
                if (!GetClientRect(handle, out client))
                    return null;
                Point topLeft = new Point(client.Left, client.Top);
                Point bottomRight = new Point(client.Right, client.Bottom);
                if (!ClientToScreen(handle, ref topLeft) || !ClientToScreen(handle, ref bottomRight))
                    return null;
                int width = bottomRight.X - topLeft.X;
                int height = bottomRight.Y - topLeft.Y;
                return width > 0 && height > 0 ? new PixelRect(topLeft.X, topLeft.Y, width, height) : null;
            }
            finally
            {
                SetThreadDpiAwarenessContext(previousContext);
            }
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
            return CapturePrintWindowCore(handle, PrintWindow);
        }

        public Bitmap CapturePrintWindowFullContent(IntPtr handle)
        {
            return CapturePrintWindowCore(handle, PrintWindowFullContent);
        }

        private Bitmap CapturePrintWindowCore(IntPtr handle, PrintWindowInvoker invoker)
        {
            RECT rect;
            if (!GetWindowRect(handle, out rect))
                return null;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                return null;

            return CapturePrintWindowBitmap(handle, width, height, invoker);
        }

        private static bool PrintWindowFullContent(IntPtr hwnd, IntPtr hdcBlt, uint nFlags)
        {
            // PW_RENDERFULLCONTENT = 0x2 — required to capture CEF/Chromium GPU-composited surfaces.
            return PrintWindow(hwnd, hdcBlt, nFlags | 0x2u);
        }

        internal static Bitmap CapturePrintWindowBitmap(
            IntPtr handle,
            int width,
            int height,
            PrintWindowInvoker printWindow)
        {
            if (width <= 0 || height <= 0 || printWindow == null)
                return null;

            Bitmap bitmap = new Bitmap(width, height);
            try
            {
                bool captured;
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr dc = graphics.GetHdc();
                    try
                    {
                        captured = printWindow(handle, dc, 0u);
                    }
                    finally
                    {
                        graphics.ReleaseHdc(dc);
                    }
                }
                if (!captured)
                {
                    bitmap.Dispose();
                    return null;
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
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr handle, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr handle, ref Point point);

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

        internal delegate bool PrintWindowInvoker(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
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

        private static unsafe ulong CopyRow(BitmapData bitmapData, int row, int width, byte[] pixels, int packedStride, ulong hash)
        {
            byte* sourceRow = (byte*)bitmapData.Scan0 + (row * bitmapData.Stride);
            int targetOffset = row * packedStride;
            for (int column = 0; column < width; column++)
            {
                int sourceIndex = column * BytesPerPixel;
                int targetIndex = targetOffset + (column * BytesPerPixel);
                byte blue = sourceRow[sourceIndex];
                byte green = sourceRow[sourceIndex + 1];
                byte red = sourceRow[sourceIndex + 2];
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
