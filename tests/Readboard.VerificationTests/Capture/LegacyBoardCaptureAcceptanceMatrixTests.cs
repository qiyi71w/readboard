using System;
using System.Drawing;
using System.Drawing.Imaging;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Capture
{
    public sealed class LegacyBoardCaptureAcceptanceMatrixTests
    {
        [Fact]
        public void Capture_EnhancedOffscreenWindowUsesPrintWindow()
        {
            RecordingCapturePlatform platform = new RecordingCapturePlatform(CreateBoardBitmap(), CreateOffscreenWindow());
            LegacyBoardCaptureService service = new LegacyBoardCaptureService(platform);

            BoardCaptureResult result = service.Capture(CreateWindowRequest(preferPrintWindow: true, useEnhancedCapture: true, isInitialProbe: false));

            Assert.True(result.Success);
            Assert.Equal(CapturePathKind.WindowBitmap, result.CapturePath);
            Assert.True(result.Frame.UsedPrintWindow);
            Assert.Equal(1, platform.PrintWindowCalls);
            Assert.Equal(0, platform.WindowCalls);
        }

        [Fact]
        public void Capture_InitialProbeUsesPrintWindowBeforeWindowCanBeFocused()
        {
            RecordingCapturePlatform platform = new RecordingCapturePlatform(CreateBoardBitmap(), CreateOffscreenWindow());
            LegacyBoardCaptureService service = new LegacyBoardCaptureService(platform);

            BoardCaptureResult result = service.Capture(CreateWindowRequest(preferPrintWindow: false, useEnhancedCapture: true, isInitialProbe: true));

            Assert.True(result.Success);
            Assert.True(result.Frame.PreferPrintWindow);
            Assert.True(result.Frame.UsedPrintWindow);
            Assert.Equal(1, platform.PrintWindowCalls);
        }

        [Fact]
        public void Capture_EnhancedRightEdgeOverflowUsesPrintWindow()
        {
            PixelRect partiallyOffscreenWindow = new PixelRect(1918, 100, 6, 6);
            RecordingCapturePlatform platform = new RecordingCapturePlatform(CreateBoardBitmap(), CreateWindow(partiallyOffscreenWindow));
            LegacyBoardCaptureService service = new LegacyBoardCaptureService(platform);

            BoardCaptureResult result = service.Capture(
                CreateWindowRequest(preferPrintWindow: true, useEnhancedCapture: true, isInitialProbe: false, windowBounds: partiallyOffscreenWindow));

            Assert.True(result.Success);
            Assert.True(result.Frame.UsedPrintWindow);
            Assert.Equal(1, platform.PrintWindowCalls);
            Assert.Equal(0, platform.WindowCalls);
        }

        [Fact]
        public void Recognize_UnenhancedOffscreenCaptureRequestsPrintWindowFallback()
        {
            RecordingCapturePlatform platform = new RecordingCapturePlatform(CreateAllBlackBoardBitmap(), CreateOffscreenWindow());
            LegacyBoardCaptureService captureService = new LegacyBoardCaptureService(platform);
            LegacyBoardRecognitionService recognitionService = new LegacyBoardRecognitionService();

            BoardCaptureResult capture = captureService.Capture(CreateWindowRequest(preferPrintWindow: true, useEnhancedCapture: false, isInitialProbe: false));
            BoardRecognitionResult recognition = recognitionService.Recognize(new BoardRecognitionRequest
            {
                Frame = capture.Frame,
                InferLastMove = false
            });

            Assert.True(capture.Success);
            Assert.False(capture.Frame.UsedPrintWindow);
            Assert.True(recognition.Success, recognition.FailureReason);
            Assert.True(recognition.Snapshot.NeedsPrintWindowFallback);
            Assert.Equal(1, platform.WindowCalls);
            Assert.Equal(0, platform.PrintWindowCalls);
        }

        [Fact]
        public void Capture_PrintWindowFailureReturnsCaptureFailed()
        {
            RecordingCapturePlatform platform = new RecordingCapturePlatform(CreateBoardBitmap(), CreateOffscreenWindow(), failPrintWindow: true);
            LegacyBoardCaptureService service = new LegacyBoardCaptureService(platform);

            BoardCaptureResult result = service.Capture(CreateWindowRequest(preferPrintWindow: true, useEnhancedCapture: true, isInitialProbe: false));

            Assert.False(result.Success);
            Assert.Null(result.Frame);
            Assert.Equal(BoardCaptureFailureKind.CaptureFailed, result.FailureKind);
            Assert.Equal(1, platform.PrintWindowCalls);
            Assert.Equal(0, platform.WindowCalls);
        }

        [Fact]
        public void CapturePrintWindowBitmap_ReturnsNullWhenNativePrintWindowFails()
        {
            Bitmap bitmap = Win32BoardCapturePlatform.CapturePrintWindowBitmap(
                new IntPtr(4004),
                width: 6,
                height: 6,
                printWindow: (handle, dc, flags) => false);

            Assert.Null(bitmap);
        }

        private static BoardCaptureRequest CreateWindowRequest(
            bool preferPrintWindow,
            bool useEnhancedCapture,
            bool isInitialProbe,
            PixelRect windowBounds = null)
        {
            PixelRect bounds = CloneRect(windowBounds) ?? CreateOffscreenBounds();
            return new BoardCaptureRequest
            {
                Window = new WindowDescriptor
                {
                    Handle = new IntPtr(4004),
                    Bounds = bounds,
                    IsDpiAware = true,
                    DpiScale = 1d
                },
                SyncMode = SyncMode.Background,
                BoardSize = new BoardDimensions(2, 2),
                PreferPrintWindow = preferPrintWindow,
                UseEnhancedCapture = useEnhancedCapture,
                IsInitialProbe = isInitialProbe
            };
        }

        private static WindowDescriptor CreateOffscreenWindow()
        {
            return CreateWindow(CreateOffscreenBounds());
        }

        private static WindowDescriptor CreateWindow(PixelRect bounds)
        {
            return new WindowDescriptor
            {
                Handle = new IntPtr(4004),
                Bounds = CloneRect(bounds),
                IsDpiAware = true,
                DpiScale = 1d
            };
        }

        private static PixelRect CreateOffscreenBounds()
        {
            return new PixelRect(2050, 100, 6, 6);
        }

        private static PixelRect CloneRect(PixelRect rect)
        {
            return rect == null ? null : new PixelRect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private static Bitmap CreateBoardBitmap()
        {
            Bitmap bitmap = new Bitmap(6, 6, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(120, 80, 40));
                graphics.FillRectangle(Brushes.Black, 0, 0, 3, 3);
                graphics.FillRectangle(Brushes.White, 3, 0, 3, 3);
                graphics.FillRectangle(Brushes.Black, 3, 3, 3, 3);
            }
            return bitmap;
        }

        private static Bitmap CreateAllBlackBoardBitmap()
        {
            Bitmap bitmap = new Bitmap(6, 6, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);
            }
            return bitmap;
        }

        private sealed class RecordingCapturePlatform : IBoardCapturePlatform
        {
            private readonly Bitmap sourceBitmap;
            private readonly WindowDescriptor descriptor;
            private readonly bool failPrintWindow;

            public RecordingCapturePlatform(Bitmap sourceBitmap, WindowDescriptor descriptor, bool failPrintWindow = false)
            {
                this.sourceBitmap = sourceBitmap;
                this.descriptor = descriptor;
                this.failPrintWindow = failPrintWindow;
            }

            public int WindowCalls { get; private set; }
            public int PrintWindowCalls { get; private set; }

            public double GetDesktopDpiScale()
            {
                return 1d;
            }

            public PixelRect GetPrimaryScreenBounds()
            {
                return new PixelRect(0, 0, 1920, 1080);
            }

            public bool TryDescribeWindow(IntPtr handle, WindowDescriptor seed, out WindowDescriptor resolved)
            {
                resolved = new WindowDescriptor
                {
                    Handle = descriptor.Handle,
                    Bounds = new PixelRect(descriptor.Bounds.X, descriptor.Bounds.Y, descriptor.Bounds.Width, descriptor.Bounds.Height),
                    IsDpiAware = descriptor.IsDpiAware,
                    DpiScale = descriptor.DpiScale,
                    IsJavaWindow = descriptor.IsJavaWindow
                };
                return true;
            }

            public Bitmap CaptureWindow(IntPtr handle)
            {
                WindowCalls++;
                return CloneBitmap();
            }

            public Bitmap CapturePrintWindow(IntPtr handle)
            {
                PrintWindowCalls++;
                if (failPrintWindow)
                    return null;
                return CloneBitmap();
            }

            public Bitmap CaptureScreen(PixelRect bounds)
            {
                throw new NotSupportedException();
            }

            private Bitmap CloneBitmap()
            {
                return (Bitmap)sourceBitmap.Clone();
            }
        }
    }
}
