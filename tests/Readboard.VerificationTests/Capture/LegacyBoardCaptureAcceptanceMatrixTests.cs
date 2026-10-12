using System;
using System.Drawing;
using System.Drawing.Imaging;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Capture
{
    public sealed class LegacyBoardCaptureAcceptanceMatrixTests
    {
        [Theory]
        [InlineData("chrome", "Chrome_RenderWidgetHostHWND", 1d, 100)]
        [InlineData("chrome", "Chrome_WidgetWin_1", 1.5d, -1200)]
        [InlineData("msedge", "Chrome_RenderWidgetHostHWND", 1.5d, -1200)]
        [InlineData("msedge", "Chrome_WidgetWin_1", 1d, 100)]
        public void Capture_BrowserSamplesRecognizeCurrentBoardWithoutEnhancedCapture(string processName, string className, double scale, int originX)
        {
            ReplayFixture fixture = ReplayFixtureCatalog.LoadForeground5x5();
            PixelBuffer basePixels = fixture.CreateCaptureResult(ReplayVariant.Base).Frame.PixelBuffer;
            using (Bitmap ordinary = new Bitmap(basePixels.Width + 14, basePixels.Height + 22))
            using (Bitmap fullContent = new Bitmap(ordinary.Width, ordinary.Height))
            {
                WindowDescriptor window = CreateWindow(new PixelRect(originX, 100, fullContent.Width, fullContent.Height), true, scale);
                window.ProcessName = processName;
                window.ClassName = className;
                RecordingCapturePlatform platform = new RecordingCapturePlatform(ordinary, window, fullContentBitmap: fullContent);
                LegacyBoardCaptureService service = new LegacyBoardCaptureService(platform);
                LegacyBoardRecognitionService recognitionService = new LegacyBoardRecognitionService();
                BoardCaptureRequest request = CreateWindowRequest(false, false, true, window.Bounds,
                    new PixelRect(originX + 7, 111, basePixels.Width, basePixels.Height), dpiScale: scale);
                request.BoardSize = new BoardDimensions(fixture.BoardWidth, fixture.BoardHeight);
                for (int sample = 0; sample < 3; sample++)
                {
                    request.IsInitialProbe = sample == 0;
                    // Keep the original selection while the same target translates.
                    window.Bounds.X = originX + sample * 3;
                    window.Bounds.Y = 100 - sample * 4;
                    window.ClientBounds.X = window.Bounds.X;
                    window.ClientBounds.Y = window.Bounds.Y;
                    PixelBuffer pixels = fixture.CreateCaptureResult(sample == 2 ? ReplayVariant.Changed : ReplayVariant.Base).Frame.PixelBuffer;
                    PaintReplay(fullContent, pixels, 7, 11);
                    BoardCaptureResult capture = service.Capture(request);
                    Assert.True(capture.Success, capture.FailureReason);
                    using (capture.Frame.Image)
                    {
                        BoardRecognitionResult recognition = recognitionService.Recognize(new BoardRecognitionRequest
                        {
                            Frame = capture.Frame,
                            InferLastMove = false
                        });
                        Assert.True(recognition.Success, recognition.FailureReason);
                        Assert.Equal(sample == 2 ? fixture.ChangedProtocolLines : fixture.BaseProtocolLines, recognition.Snapshot.ProtocolLines);
                        Assert.Equal(7, capture.Frame.Viewport.SourceBounds.X);
                        Assert.Equal(11, capture.Frame.Viewport.SourceBounds.Y);
                        Assert.Equal(7, recognition.Viewport.SourceBounds.X);
                        Assert.Equal(11, recognition.Viewport.SourceBounds.Y);
                        Assert.Equal(originX + sample * 3 + 7, capture.Frame.Viewport.ScreenBounds.X);
                        Assert.Equal(111 - sample * 4, capture.Frame.Viewport.ScreenBounds.Y);
                        Assert.True(capture.Frame.UsedPrintWindow);
                        Assert.Equal(sample == 1, capture.Frame.HasSameContentAsPreviousCapture);
                    }
                }
            }
        }

        [Theory]
        [InlineData("electron", "Chrome_WidgetWin_1", 3, false)]
        [InlineData("msedgewebview2", "Chrome_RenderWidgetHostHWND", 3, false)]
        [InlineData("cefclient", "Chrome_WidgetWin_1", 3, false)]
        [InlineData("firefox", "Chrome_WidgetWin_1", 3, false)]
        [InlineData(null, "Chrome_WidgetWin_1", 3, false)]
        [InlineData("chrome", null, 3, false)]
        [InlineData("chrome", "OtherWindow", 3, false)]
        [InlineData("chrome", "Chrome_WidgetWin_1", 0, false)]
        [InlineData("chrome", "Chrome_WidgetWin_1", 1, false)]
        [InlineData("chrome", "Chrome_WidgetWin_1", 2, false)]
        [InlineData("chrome", "Chrome_WidgetWin_1", 4, false)]
        [InlineData("chrome", "Chrome_WidgetWin_1", 5, false)]
        [InlineData("yike", "OtherWindow", 6, true)]
        [InlineData("CHROME", "Chrome_WidgetWin_1", 3, true)]
        public void Capture_OnlySupportedIdentityAndModeChangePixelSource(string processName, string className, int mode, bool fullContentExpected)
        {
            using (Bitmap ordinary = CreateAllBlackBoardBitmap())
            using (Bitmap fullContent = CreateBoardBitmap())
            {
                WindowDescriptor window = CreateWindow(new PixelRect(100, 100, 6, 6));
                window.ProcessName = processName;
                window.ClassName = className;
                RecordingCapturePlatform platform = new RecordingCapturePlatform(ordinary, window, fullContentBitmap: fullContent);
                BoardCaptureRequest request = CreateWindowRequest(false, false, false, window.Bounds, window.Bounds);
                request.SyncMode = (SyncMode)mode;
                if (request.SyncMode == SyncMode.Foreground)
                    request.SelectionBounds = CloneRect(window.Bounds);
                // A stale browser seed must not override the current platform description.
                request.Window.ProcessName = "chrome";
                request.Window.ClassName = "Chrome_WidgetWin_1";
                BoardCaptureResult capture = new LegacyBoardCaptureService(platform).Capture(request);
                Assert.True(capture.Success, capture.FailureReason);
                using (capture.Frame.Image)
                    Assert.Equal((fullContentExpected ? Color.White : Color.Black).ToArgb(), capture.Frame.Image.GetPixel(4, 1).ToArgb());
            }
        }

        [Fact]
        public void Capture_BrowserFailureDoesNotReturnPreviousFrameAndNextSampleRecovers()
        {
            using (Bitmap ordinary = CreateAllBlackBoardBitmap())
            using (Bitmap fullContent = CreateBoardBitmap())
            {
                WindowDescriptor window = CreateWindow(new PixelRect(100, 100, 6, 6));
                window.ProcessName = "msedge";
                window.ClassName = "Chrome_RenderWidgetHostHWND";
                RecordingCapturePlatform platform = new RecordingCapturePlatform(ordinary, window, fullContentBitmap: fullContent);
                LegacyBoardCaptureService service = new LegacyBoardCaptureService(platform);
                BoardCaptureRequest request = CreateWindowRequest(false, false, false, window.Bounds);
                BoardCaptureResult first = service.Capture(request);
                Assert.True(first.Success, first.FailureReason);
                first.Frame.Image.Dispose();
                platform.FailPrintWindow = true;
                BoardCaptureResult failed = service.Capture(request);
                Assert.False(failed.Success);
                Assert.Equal(BoardCaptureFailureKind.CaptureFailed, failed.FailureKind);
                Assert.Null(failed.Frame);
                platform.FailPrintWindow = false;
                fullContent.SetPixel(4, 1, Color.Red);
                BoardCaptureResult recovered = service.Capture(request);
                Assert.True(recovered.Success, recovered.FailureReason);
                using (recovered.Frame.Image)
                    Assert.Equal(Color.Red.ToArgb(), recovered.Frame.Image.GetPixel(4, 1).ToArgb());
            }
        }

        private static void PaintReplay(Bitmap bitmap, PixelBuffer pixels, int offsetX, int offsetY)
        {
            for (int y = 0; y < pixels.Height; y++)
            {
                for (int x = 0; x < pixels.Width; x++)
                {
                    int index = y * pixels.Stride + x * 3;
                    bitmap.SetPixel(x + offsetX, y + offsetY, Color.FromArgb(pixels.Pixels[index], pixels.Pixels[index + 1], pixels.Pixels[index + 2]));
                }
            }
        }

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
            PixelRect partiallyOffscreenWindow = new PixelRect(3838, 100, 6, 6);
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
        public void Capture_EnhancedNegativeYWindowInsideVirtualScreenDoesNotUsePrintWindow()
        {
            PixelRect visibleWindow = new PixelRect(120, -200, 6, 6);
            PixelRect virtualScreen = new PixelRect(0, -1080, 3840, 3240);
            RecordingCapturePlatform platform = new RecordingCapturePlatform(
                CreateBoardBitmap(),
                CreateWindow(visibleWindow),
                failPrintWindow: false,
                virtualScreenBounds: virtualScreen);
            LegacyBoardCaptureService service = new LegacyBoardCaptureService(platform);

            BoardCaptureResult result = service.Capture(
                CreateWindowRequest(preferPrintWindow: true, useEnhancedCapture: true, isInitialProbe: false, windowBounds: visibleWindow));

            Assert.True(result.Success);
            Assert.False(result.Frame.UsedPrintWindow);
            Assert.Equal(0, platform.PrintWindowCalls);
            Assert.Equal(1, platform.WindowCalls);
        }

        [Fact]
        public void Capture_DpiUnawareWindowUsesMonitorScaleForScreenRelativeCrop()
        {
            PixelRect windowBounds = new PixelRect(300, 200, 150, 150);
            RecordingCapturePlatform platform = new RecordingCapturePlatform(
                CreateBitmap(150, 150),
                CreateWindow(windowBounds, isDpiAware: false, dpiScale: 1.5d));
            LegacyBoardCaptureService service = new LegacyBoardCaptureService(platform);

            BoardCaptureResult result = service.Capture(
                CreateWindowRequest(
                    preferPrintWindow: false,
                    useEnhancedCapture: false,
                    isInitialProbe: false,
                    windowBounds: windowBounds,
                    selectionBounds: new PixelRect(320, 240, 50, 50),
                    isDpiAware: false,
                    dpiScale: 1.5d));

            Assert.True(result.Success);
            Assert.Equal(13, result.Frame.Viewport.SourceBounds.X);
            Assert.Equal(26, result.Frame.Viewport.SourceBounds.Y);
            Assert.Equal(33, result.Frame.Viewport.SourceBounds.Width);
            Assert.Equal(33, result.Frame.Viewport.SourceBounds.Height);
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

        [Theory]
        [InlineData(false, true, 1d, 120, 8, 24, 17, -13)]
        [InlineData(true, true, 1d, -1200, 8, 24, -19, 11)]
        [InlineData(false, false, 1.5d, -1200, 6, 18, -19, 11)]
        [InlineData(true, false, 1.5d, 120, 6, 18, 17, -13)]
        [InlineData(false, true, 1.5d, -950, 0, 0, 17, -13)]
        [InlineData(true, true, 1.5d, 650, 0, 0, -19, 11)]
        public void Capture_BorderedAndChildTargetsKeepCorrectPixelsAcrossMovement(
            bool printWindow, bool dpiAware, double scale, int originX, int borderX, int borderY, int moveX, int moveY)
        {
            double imageScale = dpiAware ? 1d : scale;
            WindowDescriptor window = CreateWindow(new PixelRect(originX, 100, 100, 100), dpiAware, scale);
            window.ClientBounds = new PixelRect(originX + borderX, 100 + borderY, 60, 60);
            window.IsJavaWindow = printWindow;
            int printOffsetX = DisplayScaling.ScaleCoordinateDown(borderX, imageScale);
            int printOffsetY = DisplayScaling.ScaleCoordinateDown(borderY, imageScale);
            using (Bitmap board = CreateBoardBitmap())
            using (Bitmap clientImage = new Bitmap(60, 60))
            using (Bitmap outerImage = new Bitmap(100, 100))
            {
                using (Graphics graphics = Graphics.FromImage(clientImage))
                {
                    graphics.Clear(Color.Magenta);
                    graphics.DrawImageUnscaled(board, 10, 12);
                }
                using (Graphics graphics = Graphics.FromImage(outerImage))
                {
                    graphics.Clear(Color.Blue);
                    graphics.DrawImageUnscaled(board, 10 + printOffsetX, 12 + printOffsetY);
                }
                PixelRect selection = new PixelRect(window.ClientBounds.X + (int)(10 * imageScale),
                    window.ClientBounds.Y + (int)(12 * imageScale), (int)(6 * imageScale), (int)(6 * imageScale));
                BoardCaptureRequest request = new BoardCaptureRequest
                {
                    Window = window,
                    SyncMode = SyncMode.Background,
                    BoardSize = new BoardDimensions(2, 2),
                    BackgroundSelection = BackgroundSelectionBinding.TryCreate(window, selection, 1)
                };
                RecordingCapturePlatform platform = new RecordingCapturePlatform(clientImage, window) { PrintWindowBitmap = outerImage };
                LegacyBoardCaptureService service = new LegacyBoardCaptureService(platform);
                for (int sample = 0; sample < 3; sample++)
                {
                    window.Bounds.X = originX + sample * moveX;
                    window.Bounds.Y = 100 + sample * moveY;
                    window.ClientBounds.X = window.Bounds.X + borderX;
                    window.ClientBounds.Y = window.Bounds.Y + borderY;
                    if (sample == 2)
                    {
                        board.SetPixel(4, 4, Color.White);
                        clientImage.SetPixel(14, 16, Color.White);
                        outerImage.SetPixel(14 + printOffsetX, 16 + printOffsetY, Color.White);
                    }
                    BoardCaptureResult result = service.Capture(request);
                    Assert.True(result.Success, result.FailureReason);
                    using (result.Frame.Image)
                    {
                        for (int y = 0; y < board.Height; y++)
                            for (int x = 0; x < board.Width; x++)
                                Assert.Equal(board.GetPixel(x, y).ToArgb(), result.Frame.Image.GetPixel(x, y).ToArgb());
                        Assert.Equal(10 + (printWindow ? printOffsetX : 0), result.Frame.Viewport.SourceBounds.X);
                        Assert.Equal(12 + (printWindow ? printOffsetY : 0), result.Frame.Viewport.SourceBounds.Y);
                        Assert.Equal(selection.X + sample * moveX, result.Frame.Viewport.ScreenBounds.X);
                        Assert.Equal(selection.Y + sample * moveY, result.Frame.Viewport.ScreenBounds.Y);
                        Assert.Equal(printWindow, result.Frame.UsedPrintWindow);
                    }
                }
            }
        }

        [Theory]
        [InlineData("resize")]
        [InlineData("dpi")]
        [InlineData("unavailable")]
        [InlineData("replacement")]
        public void Capture_InvalidBindingCannotResumeUntilReselected(string change)
        {
            using (Bitmap bitmap = CreateBoardBitmap())
            {
                WindowDescriptor window = CreateWindow(new PixelRect(100, 100, 6, 6));
                RecordingCapturePlatform platform = new RecordingCapturePlatform(bitmap, window);
                LegacyBoardCaptureService service = new LegacyBoardCaptureService(platform);
                BoardCaptureRequest request = CreateWindowRequest(false, false, false, window.Bounds);
                BoardCaptureResult first = service.Capture(request);
                Assert.True(first.Success, first.FailureReason);
                first.Frame.Image.Dispose();
                if (change == "resize") window.ClientBounds.Width++;
                if (change == "dpi") window.DpiScale = 1.5d;
                if (change == "unavailable") platform.WindowUnavailable = true;
                if (change == "replacement") request.Window.Handle = new IntPtr(5005);
                BoardCaptureResult invalid = service.Capture(request);
                Assert.False(invalid.Success);
                Assert.Null(invalid.Frame);
                window.ClientBounds.Width = 6;
                window.DpiScale = 1d;
                platform.WindowUnavailable = false;
                request.Window.Handle = window.Handle;
                BoardCaptureResult restored = service.Capture(request);
                Assert.False(restored.Success);
                Assert.Null(restored.Frame);
                request.BackgroundSelection = BackgroundSelectionBinding.TryCreate(window, window.ClientBounds, 2);
                bitmap.SetPixel(4, 1, Color.Red);
                BoardCaptureResult reselected = service.Capture(request);
                Assert.True(reselected.Success, reselected.FailureReason);
                using (reselected.Frame.Image)
                    Assert.Equal(Color.Red.ToArgb(), reselected.Frame.Image.GetPixel(4, 1).ToArgb());
            }
        }

        [Fact]
        public void Capture_BackgroundWithoutBindingRejectsScreenSelectionInsteadOfCapturingWholeWindow()
        {
            using (Bitmap bitmap = CreateBoardBitmap())
            {
                WindowDescriptor window = CreateWindow(new PixelRect(100, 100, 6, 6));
                BoardCaptureRequest request = CreateWindowRequest(false, false, false, window.Bounds);
                request.BackgroundSelection = null;
                request.SelectionBounds = CloneRect(window.Bounds);
                BoardCaptureResult result = new LegacyBoardCaptureService(new RecordingCapturePlatform(bitmap, window)).Capture(request);
                Assert.False(result.Success);
                Assert.Equal(BoardCaptureFailureKind.InvalidSelection, result.FailureKind);
                Assert.Null(result.Frame);
            }
        }

        [Fact]
        public void Capture_CropOutsideActualBitmapFailsWithoutPaddedFrame()
        {
            using (Bitmap bitmap = CreateBoardBitmap())
            {
                WindowDescriptor window = CreateWindow(new PixelRect(100, 100, 12, 12));
                BoardCaptureRequest request = CreateWindowRequest(false, false, false, window.Bounds);
                LegacyBoardCaptureService service = new LegacyBoardCaptureService(new RecordingCapturePlatform(bitmap, window));
                BoardCaptureResult result = service.Capture(request);
                Assert.False(result.Success);
                Assert.Equal(BoardCaptureFailureKind.InvalidSelection, result.FailureKind);
                Assert.Null(result.Frame);
                PixelRect projection;
                Assert.False(request.BackgroundSelection.TryProject(window, out projection));
            }
        }

        [Fact]
        public void Capture_EnhancedPolicyUsesMovedProjectionRatherThanOriginalScreenSelection()
        {
            using (Bitmap client = CreateAllBlackBoardBitmap())
            using (Bitmap printed = CreateBoardBitmap())
            {
                WindowDescriptor window = CreateWindow(new PixelRect(100, 100, 6, 6));
                RecordingCapturePlatform platform = new RecordingCapturePlatform(client, window) { PrintWindowBitmap = printed };
                LegacyBoardCaptureService service = new LegacyBoardCaptureService(platform);
                BoardCaptureRequest request = CreateWindowRequest(true, true, false, window.Bounds);
                window.Bounds.X = window.ClientBounds.X = 4000;
                BoardCaptureResult moved = service.Capture(request);
                Assert.True(moved.Success, moved.FailureReason);
                using (moved.Frame.Image)
                    Assert.Equal(Color.White.ToArgb(), moved.Frame.Image.GetPixel(4, 1).ToArgb());
                window.Bounds.X = window.ClientBounds.X = 100;
                BoardCaptureResult returned = service.Capture(request);
                Assert.True(returned.Success, returned.FailureReason);
                using (returned.Frame.Image)
                    Assert.Equal(Color.Black.ToArgb(), returned.Frame.Image.GetPixel(4, 1).ToArgb());
            }
        }

        private static BoardCaptureRequest CreateWindowRequest(
            bool preferPrintWindow,
            bool useEnhancedCapture,
            bool isInitialProbe,
            PixelRect windowBounds = null,
            PixelRect selectionBounds = null,
            bool isDpiAware = true,
            double dpiScale = 1d)
        {
            PixelRect bounds = CloneRect(windowBounds) ?? CreateOffscreenBounds();
            WindowDescriptor window = CreateWindow(bounds, isDpiAware, dpiScale);
            return new BoardCaptureRequest
            {
                Window = window,
                SyncMode = SyncMode.Background,
                BoardSize = new BoardDimensions(2, 2),
                BackgroundSelection = BackgroundSelectionBinding.TryCreate(window, selectionBounds ?? bounds, 1),
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
            return CreateWindow(bounds, isDpiAware: true, dpiScale: 1d);
        }

        private static WindowDescriptor CreateWindow(PixelRect bounds, bool isDpiAware, double dpiScale)
        {
            return new WindowDescriptor
            {
                Handle = new IntPtr(4004),
                Bounds = CloneRect(bounds),
                ClientBounds = CloneRect(bounds),
                IsDpiAware = isDpiAware,
                DpiScale = dpiScale
            };
        }

        private static PixelRect CreateOffscreenBounds()
        {
            return new PixelRect(4000, 100, 6, 6);
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
            Bitmap bitmap = CreateBitmap(6, 6);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);
            }
            return bitmap;
        }

        private static Bitmap CreateBitmap(int width, int height)
        {
            return new Bitmap(width, height, PixelFormat.Format24bppRgb);
        }

        private sealed class RecordingCapturePlatform : IBoardCapturePlatform
        {
            private readonly Bitmap sourceBitmap;
            private readonly WindowDescriptor descriptor;
            public bool FailPrintWindow { get; set; }
            public bool WindowUnavailable { get; set; }
            private readonly PixelRect virtualScreenBounds;
            private readonly Bitmap fullContentBitmap;
            public Bitmap PrintWindowBitmap { get; set; }

            public RecordingCapturePlatform(
                Bitmap sourceBitmap,
                WindowDescriptor descriptor,
                bool failPrintWindow = false,
                PixelRect virtualScreenBounds = null,
                Bitmap fullContentBitmap = null)
            {
                this.sourceBitmap = sourceBitmap;
                this.descriptor = descriptor;
                FailPrintWindow = failPrintWindow;
                this.virtualScreenBounds = CloneRect(virtualScreenBounds) ?? new PixelRect(0, 0, 3840, 2160);
                this.fullContentBitmap = fullContentBitmap;
            }

            public int WindowCalls { get; private set; }
            public int PrintWindowCalls { get; private set; }

            public double GetScaleForPoint(Point point)
            {
                return 1d;
            }

            public PixelRect GetVirtualScreenBounds()
            {
                return CloneRect(virtualScreenBounds);
            }

            public bool TryDescribeWindow(IntPtr handle, WindowDescriptor seed, out WindowDescriptor resolved)
            {
                if (WindowUnavailable)
                {
                    resolved = null;
                    return false;
                }
                resolved = new WindowDescriptor
                {
                    Handle = descriptor.Handle,
                    ProcessName = descriptor.ProcessName,
                    ClassName = descriptor.ClassName,
                    Bounds = new PixelRect(descriptor.Bounds.X, descriptor.Bounds.Y, descriptor.Bounds.Width, descriptor.Bounds.Height),
                    ClientBounds = CloneRect(descriptor.ClientBounds),
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
                if (FailPrintWindow)
                    return null;
                return (Bitmap)(PrintWindowBitmap ?? sourceBitmap).Clone();
            }

            public Bitmap CapturePrintWindowFullContent(IntPtr handle)
            {
                PrintWindowCalls++;
                if (FailPrintWindow)
                    return null;
                return (Bitmap)(fullContentBitmap ?? sourceBitmap).Clone();
            }

            public Bitmap CaptureScreen(PixelRect bounds)
            {
                return CloneBitmap();
            }

            private Bitmap CloneBitmap()
            {
                return (Bitmap)sourceBitmap.Clone();
            }
        }
    }
}
