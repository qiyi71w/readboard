using System;
using System.Collections.Generic;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Placement
{
    public sealed class LegacyMovePlacementAcceptanceMatrixTests
    {
        public static IEnumerable<object[]> BackgroundPlacementCases()
        {
            yield return new object[] { (int)SyncMode.Tygem, (int)PlacementPathKind.BackgroundPost };
            yield return new object[] { (int)SyncMode.Sina, (int)PlacementPathKind.BackgroundPost };
            yield return new object[] { (int)SyncMode.FoxBackgroundPlace, (int)PlacementPathKind.BackgroundSend };
            yield return new object[] { (int)SyncMode.Yike, (int)PlacementPathKind.BackgroundPost };
        }

        public static IEnumerable<object[]> CancellationCases()
        {
            yield return new object[] { (int)SyncMode.Foreground };
            yield return new object[] { (int)SyncMode.Background };
            yield return new object[] { (int)SyncMode.FoxBackgroundPlace };
            yield return new object[] { (int)SyncMode.Yike };
            yield return new object[] { (int)SyncMode.Fox };
        }

        [Theory]
        [MemberData(nameof(BackgroundPlacementCases))]
        public void Place_BackgroundModesUseExpectedPlacementPath(
            int syncModeValue,
            int expectedPathValue)
        {
            SyncMode syncMode = (SyncMode)syncModeValue;
            PlacementPathKind expectedPath = (PlacementPathKind)expectedPathValue;
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods();
            LegacyMovePlacementService service = new LegacyMovePlacementService(nativeMethods);

            MovePlacementResult result = service.Place(CreateRequest(syncMode));

            Assert.True(result.Success);
            Assert.Equal(expectedPath, result.PlacementPath);
            Assert.Empty(nativeMethods.ForegroundClicks);
            AssertPlacementMessages(syncMode, expectedPath, nativeMethods);
        }

        [Theory]
        [MemberData(nameof(CancellationCases))]
        public void Place_CancelledRequest_SkipsPlacementSideEffects(int syncModeValue)
        {
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods();
            LegacyMovePlacementService service = new LegacyMovePlacementService(nativeMethods);
            MovePlacementRequest request = CreateRequest((SyncMode)syncModeValue);
            request.ShouldCancel = delegate { return true; };

            MovePlacementResult result = service.Place(request);

            Assert.False(result.Success);
            Assert.Equal(MovePlacementFailureKind.PlacementFailed, result.FailureKind);
            Assert.Equal("Placement cancelled.", result.FailureReason);
            Assert.Empty(nativeMethods.ForegroundClicks);
            Assert.Empty(nativeMethods.PostedMessages);
            Assert.Empty(nativeMethods.SentMessages);
        }

        [Theory]
        [InlineData(true, true, 1d, 102, 108)]
        [InlineData(false, true, 1d, 105, 105)]
        [InlineData(true, false, 1d, 102, 108)]
        [InlineData(false, false, 1d, 105, 105)]
        [InlineData(true, true, 1.5d, 102, 108)]
        [InlineData(false, true, 1.5d, 105, 105)]
        [InlineData(true, false, 1.5d, 68, 72)]
        [InlineData(false, false, 1.5d, 70, 70)]
        public void Place_BackgroundTranslatedFrame_PreservesClientIntersection(
            bool explicitIntersection, bool dpiAware, double scale, int expectedX, int expectedY)
        {
            foreach ((int dx, int dy) in new[] { (0, 0), (3, -4), (-27, 36) })
            {
                RecordingNativeMethods nativeMethods = new RecordingNativeMethods();
                LegacyMovePlacementService service = new LegacyMovePlacementService(nativeMethods);
                BoardFrame frame = CreateTranslatedBackgroundFrame(explicitIntersection, dpiAware, scale, dx, dy);

                MovePlacementResult result = service.Place(new MovePlacementRequest
                {
                    Frame = frame,
                    Move = new MoveRequest { X = 2, Y = 1 }
                });

                Assert.True(result.Success);
                Assert.Equal(PlacementPathKind.BackgroundPost, result.PlacementPath);
                Assert.NotEmpty(nativeMethods.PostedMessages);
                Assert.All(nativeMethods.PostedMessages, message =>
                {
                    Assert.Equal(frame.Window.Handle, message.Handle);
                    Assert.Equal(BuildMouseLParam(expectedX, expectedY), message.LParam);
                });
                Assert.Empty(nativeMethods.ForegroundClicks);
            }
        }

        [Theory]
        [InlineData(true, true, 102, 108)]
        [InlineData(false, true, 105, 105)]
        [InlineData(true, false, 102, 108)]
        [InlineData(false, false, 105, 105)]
        public void Place_JavaBackgroundTranslatedFrame_TranslatesScreenIntersection(
            bool explicitIntersection, bool dpiAware, int localX, int localY)
        {
            foreach ((int dx, int dy) in new[] { (0, 0), (3, -4), (-27, 36) })
            {
                RecordingNativeMethods nativeMethods = new RecordingNativeMethods();
                LegacyMovePlacementService service = new LegacyMovePlacementService(nativeMethods);
                BoardFrame frame = CreateTranslatedBackgroundFrame(explicitIntersection, dpiAware, 1.5d, dx, dy);
                frame.Window.IsJavaWindow = true;

                MovePlacementResult result = service.Place(new MovePlacementRequest
                {
                    Frame = frame,
                    Move = new MoveRequest { X = 2, Y = 1 }
                });

                Assert.True(result.Success);
                Assert.Equal(PlacementPathKind.Foreground, result.PlacementPath);
                Assert.Equal(new[] { (-300 + dx + localX, 180 + dy + localY, false) }, nativeMethods.ForegroundClicks);
                Assert.Empty(nativeMethods.PostedMessages);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Place_BackgroundMissingAuthoritativeGeometry_DoesNotUseSourceOrOuterBounds(
            bool javaWindow, bool missingClientBounds)
        {
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods();
            LegacyMovePlacementService service = new LegacyMovePlacementService(nativeMethods);
            BoardFrame frame = CreateTranslatedBackgroundFrame(true, true, 1d, 0, 0);
            frame.Window.IsJavaWindow = javaWindow;
            if (missingClientBounds)
                frame.Window.ClientBounds = null;
            else
                frame.Viewport.ScreenBounds = null;

            Assert.False(service.CanResolvePlacementRegion(frame));
            MovePlacementResult result = service.Place(new MovePlacementRequest
            {
                Frame = frame,
                Move = new MoveRequest { X = 2, Y = 1 }
            });

            Assert.False(result.Success);
            Assert.Equal(MovePlacementFailureKind.MissingFrame, result.FailureKind);
            Assert.Empty(nativeMethods.ForegroundClicks);
            Assert.Empty(nativeMethods.PostedMessages);
            Assert.Empty(nativeMethods.SentMessages);
        }

        private static BoardFrame CreateTranslatedBackgroundFrame(
            bool explicitIntersection, bool dpiAware, double scale, int dx, int dy)
        {
            double sourceScale = dpiAware ? 1d : scale;
            // PrintWindow source includes a 12px border and 36px titlebar;
            // the selected board starts 30px right and 60px down in the client.
            PixelRect source = new PixelRect(
                (int)(42 / sourceScale), (int)(96 / sourceScale),
                (int)(150 / sourceScale), (int)(120 / sourceScale));
            return new BoardFrame
            {
                SyncMode = SyncMode.Background,
                BoardSize = new BoardDimensions(5, 4),
                Window = new WindowDescriptor
                {
                    Handle = new IntPtr(3003),
                    Bounds = new PixelRect(-312 + dx, 144 + dy, 324, 348),
                    ClientBounds = new PixelRect(-300 + dx, 180 + dy, 300, 300),
                    IsDpiAware = dpiAware,
                    DpiScale = scale
                },
                Viewport = new BoardViewport
                {
                    SourceBounds = source,
                    ScreenBounds = new PixelRect(-270 + dx, 240 + dy, 150, 120),
                    CellWidth = 30 / sourceScale,
                    CellHeight = 30 / sourceScale,
                    FirstIntersectionX = explicitIntersection ? source.X + 12 / sourceScale : (double?)null,
                    FirstIntersectionY = explicitIntersection ? source.Y + 18 / sourceScale : (double?)null
                }
            };
        }

        private static void AssertPlacementMessages(
            SyncMode syncMode,
            PlacementPathKind expectedPath,
            RecordingNativeMethods nativeMethods)
        {
            int expectedLParam = BuildMouseLParam(35, 55);
            if (expectedPath == PlacementPathKind.BackgroundPost)
            {
                int expectedCount = syncMode == SyncMode.Yike ? 3 : 2;
                Assert.Equal(expectedCount, nativeMethods.PostedMessages.Count);
                Assert.Empty(nativeMethods.SentMessages);
                Assert.All(nativeMethods.PostedMessages, message => Assert.Equal(expectedLParam, message.LParam));
                if (syncMode == SyncMode.Yike)
                {
                    Assert.All(nativeMethods.PostedMessages, message => Assert.Equal(new IntPtr(5005), message.Handle));
                    Assert.Equal("Chrome_RenderWidgetHostHWND", nativeMethods.LastRequestedChildClassName);
                }
                return;
            }

            Assert.Empty(nativeMethods.PostedMessages);
            Assert.Equal(3, nativeMethods.SentMessages.Count);
            Assert.All(nativeMethods.SentMessages, message => Assert.Equal(expectedLParam, message.LParam));
            Assert.All(nativeMethods.SentMessages, message => Assert.Equal(new IntPtr(3003), message.Handle));
        }

        private static int BuildMouseLParam(int x, int y)
        {
            return (x & 0xFFFF) | ((y & 0xFFFF) << 16);
        }

        private static MovePlacementRequest CreateRequest(SyncMode syncMode)
        {
            return new MovePlacementRequest
            {
                Frame = new BoardFrame
                {
                    SyncMode = syncMode,
                    BoardSize = new BoardDimensions(5, 5),
                    Viewport = new BoardViewport
                    {
                        SourceBounds = new PixelRect(20, 30, 50, 50),
                        ScreenBounds = new PixelRect(120, 230, 50, 50)
                    },
                    Window = new WindowDescriptor
                    {
                        Handle = new IntPtr(3003),
                        Bounds = new PixelRect(100, 200, 150, 150),
                        ClientBounds = new PixelRect(100, 200, 150, 150),
                        IsDpiAware = true,
                        DpiScale = 1d
                    }
                },
                Move = new MoveRequest { X = 1, Y = 2 }
            };
        }

        private sealed class RecordingNativeMethods : IPlacementNativeMethods
        {
            public List<(int X, int Y, bool Hold)> ForegroundClicks { get; } = new List<(int X, int Y, bool Hold)>();
            public List<MouseMessage> PostedMessages { get; } = new List<MouseMessage>();
            public List<MouseMessage> SentMessages { get; } = new List<MouseMessage>();
            public string LastRequestedChildClassName { get; private set; }

            public IntPtr FindWindowByClass(string className)
            {
                return IntPtr.Zero;
            }

            public IntPtr FindChildWindowByClass(IntPtr parentHandle, string className)
            {
                LastRequestedChildClassName = className;
                return new IntPtr(5005);
            }

            public bool TryGetWindowBounds(IntPtr handle, out PixelRect bounds)
            {
                bounds = handle == new IntPtr(5005)
                    ? new PixelRect(100, 200, 800, 600)
                    : null;
                return bounds != null;
            }

            public void SwitchToWindow(IntPtr handle)
            {
            }

            public bool TryForegroundLeftClick(int x, int y, bool holdButtonBeforeRelease)
            {
                ForegroundClicks.Add((x, y, holdButtonBeforeRelease));
                return true;
            }

            public bool TryPostMouseMessage(IntPtr handle, uint message, int wParam, int lParam)
            {
                PostedMessages.Add(new MouseMessage(handle, message, wParam, lParam));
                return true;
            }

            public bool TrySendMouseMessage(IntPtr handle, uint message, int wParam, int lParam)
            {
                SentMessages.Add(new MouseMessage(handle, message, wParam, lParam));
                return true;
            }
        }

        private readonly record struct MouseMessage(IntPtr Handle, uint Message, int WParam, int LParam);
    }
}
