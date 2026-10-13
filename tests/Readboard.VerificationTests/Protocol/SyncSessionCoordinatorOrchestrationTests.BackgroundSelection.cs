using System;
using System.Drawing;
using System.Linq;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed partial class SyncSessionCoordinatorOrchestrationTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BackgroundSessionRetainsBindingAcrossStopAndMoveRejectsResizeAndPublishesReselectedTarget(bool printWindow)
        {
            ReplayFixture fixture = ReplayFixtureCatalog.LoadForeground5x5();
            MovingBoardPlatform platform = new MovingBoardPlatform(fixture);
            platform.Window.IsJavaWindow = printWindow;
            SyncCoordinatorHostSnapshot snapshot = new SyncCoordinatorHostSnapshot
            {
                SyncMode = SyncMode.Background,
                BoardWidth = fixture.BoardWidth,
                BoardHeight = fixture.BoardHeight,
                SelectedWindowHandle = platform.Window.Handle,
                BackgroundSelection = platform.Bind(1),
                SampleIntervalMs = 500
            };
            WebViewHostRecorder host = new WebViewHostRecorder { Snapshot = snapshot };
            RecordingTransport transport = new RecordingTransport();
            using (SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter()))
            {
                coordinator.AttachRuntime(new SyncSessionRuntimeDependencies
                {
                    Host = host,
                    WindowDescriptorFactory = platform,
                    CaptureService = new LegacyBoardCaptureService(platform),
                    RecognitionService = new LegacyBoardRecognitionService(),
                    PlacementService = new PassivePlacementService(),
                    OverlayService = new PassiveOverlayService()
                });
                Assert.True(coordinator.TryRunOneTimeSync());
                Assert.Equal(fixture.BaseProtocolLines, LastBoard(transport, fixture.BoardHeight));
                Assert.Equal(printWindow ? 11 : 3, host.LastViewport.SourceBounds.X);
                Assert.Equal(printWindow ? 35 : 5, host.LastViewport.SourceBounds.Y);
                coordinator.StopSyncSession();
                platform.Move(-37, 21);
                Assert.True(coordinator.TryRunOneTimeSync());
                Assert.Equal(fixture.BaseProtocolLines, LastBoard(transport, fixture.BoardHeight));

                Assert.Equal(printWindow ? 11 : 3, host.LastViewport.SourceBounds.X);
                Assert.Equal(printWindow ? 35 : 5, host.LastViewport.SourceBounds.Y);
                Assert.Equal(74, host.LastViewport.ScreenBounds.X);
                Assert.Equal(156, host.LastViewport.ScreenBounds.Y);
                int sentBeforeInvalid = transport.SentLines.Count(line => line.StartsWith("re="));
                platform.Window.ClientBounds.Width++;
                Assert.False(coordinator.TryRunOneTimeSync());
                platform.Window.ClientBounds.Width--;
                Assert.False(coordinator.TryRunOneTimeSync());
                Assert.Equal(sentBeforeInvalid, transport.SentLines.Count(line => line.StartsWith("re=")));

                // A different target cannot reuse A's binding; a new selection publishes B's content.
                platform.Window.Handle = new IntPtr(72);
                snapshot.SelectedWindowHandle = platform.Window.Handle;
                platform.Variant = ReplayVariant.Changed;
                Assert.False(coordinator.TryRunOneTimeSync());
                snapshot.BackgroundSelection = platform.Bind(2);
                Assert.True(coordinator.TryRunOneTimeSync());
                Assert.Equal(fixture.ChangedProtocolLines, LastBoard(transport, fixture.BoardHeight));
            }
        }

        private static string[] LastBoard(RecordingTransport transport, int height)
        {
            // This contract concerns black/white/empty, not the existing last-move inference.
            return transport.SentLines.Where(line => line.StartsWith("re=")).TakeLast(height)
                .Select(line => "re=" + string.Join(",", line.Substring(3).Split(',')
                    .Select(cell => cell == "3" ? "1" : cell == "4" ? "2" : cell))).ToArray();
        }

        private sealed class MovingBoardPlatform : IBoardCapturePlatform, IWindowDescriptorFactory
        {
            private readonly ReplayFixture fixture;
            public WindowDescriptor Window { get; }
            public ReplayVariant Variant { get; set; }

            public MovingBoardPlatform(ReplayFixture fixture)
            {
                this.fixture = fixture;
                PixelBuffer pixels = fixture.CreateCaptureResult(ReplayVariant.Base).Frame.PixelBuffer;
                Window = new WindowDescriptor
                {
                    Handle = new IntPtr(71),
                    Bounds = new PixelRect(100, 100, pixels.Width + 24, pixels.Height + 50),
                    ClientBounds = new PixelRect(108, 130, pixels.Width + 8, pixels.Height + 12),
                    DpiScale = 1d,
                    IsDpiAware = true
                };
            }

            public BackgroundSelectionBinding Bind(int requestId)
            {
                PixelBuffer pixels = fixture.CreateCaptureResult(Variant).Frame.PixelBuffer;
                return BackgroundSelectionBinding.TryCreate(Window,
                    new PixelRect(Window.ClientBounds.X + 3, Window.ClientBounds.Y + 5, pixels.Width, pixels.Height), requestId);
            }

            public void Move(int x, int y)
            {
                Window.Bounds.X += x;
                Window.Bounds.Y += y;
                Window.ClientBounds.X += x;
                Window.ClientBounds.Y += y;
            }

            public bool TryCreate(IntPtr handle, out WindowDescriptor descriptor)
            {
                descriptor = Window;
                return handle == Window.Handle;
            }

            public bool TryDescribeWindow(IntPtr handle, WindowDescriptor seed, out WindowDescriptor descriptor)
            {
                return TryCreate(handle, out descriptor);
            }

            public double GetScaleForPoint(Point point) { return 1d; }
            public PixelRect GetVirtualScreenBounds() { return new PixelRect(-2000, -1000, 6000, 3000); }
            public Bitmap CapturePrintWindow(IntPtr handle) { return Render(11, 35, Window.Bounds); }
            public Bitmap CapturePrintWindowFullContent(IntPtr handle) { throw new InvalidOperationException("Not a browser."); }
            public Bitmap CaptureScreen(PixelRect bounds) { throw new InvalidOperationException("No foreground fallback."); }

            public Bitmap CaptureWindow(IntPtr handle) { return Render(3, 5, Window.ClientBounds); }

            private Bitmap Render(int originX, int originY, PixelRect bounds)
            {
                PixelBuffer pixels = fixture.CreateCaptureResult(Variant).Frame.PixelBuffer;
                Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height);
                for (int y = 0; y < pixels.Height; y++)
                    for (int x = 0; x < pixels.Width; x++)
                    {
                        int offset = y * pixels.Stride + x * 3;
                        bitmap.SetPixel(x + originX, y + originY, Color.FromArgb(pixels.Pixels[offset], pixels.Pixels[offset + 1], pixels.Pixels[offset + 2]));
                    }
                return bitmap;
            }
        }
    }
}
