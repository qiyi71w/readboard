using System;
using System.Collections.Generic;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Placement
{
    public sealed class LegacyMovePlacementServiceTests
    {
        [Fact]
        public void Place_ForegroundFrame_ClicksExpectedScreenCoordinate()
        {
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods();
            LegacyMovePlacementService service = new LegacyMovePlacementService(
                nativeMethods,
                new RecordingLightweightFactory());

            MovePlacementResult result = service.Place(new MovePlacementRequest
            {
                Frame = CreateForegroundFrame(),
                Move = new MoveRequest { X = 2, Y = 3 }
            });

            Assert.True(result.Success);
            Assert.Equal(PlacementPathKind.Foreground, result.PlacementPath);
            Assert.Equal(new PlacementClick(125, 235, false), nativeMethods.ForegroundClick);
        }

        [Fact]
        public void Place_FoxForegroundFrame_ProjectsDetectedBoardBoundsWithinWindow()
        {
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods();
            LegacyMovePlacementService service = new LegacyMovePlacementService(
                nativeMethods,
                new RecordingLightweightFactory());

            MovePlacementResult result = service.Place(new MovePlacementRequest
            {
                Frame = new BoardFrame
                {
                    SyncMode = SyncMode.Fox,
                    BoardSize = new BoardDimensions(5, 5),
                    Viewport = new BoardViewport
                    {
                        SourceBounds = new PixelRect(20, 30, 50, 50),
                        ScreenBounds = new PixelRect(100, 200, 150, 150)
                    },
                    Window = new WindowDescriptor
                    {
                        Handle = new IntPtr(3003),
                        Bounds = new PixelRect(100, 200, 150, 150)
                    }
                },
                Move = new MoveRequest { X = 1, Y = 2 }
            });

            Assert.True(result.Success);
            Assert.Equal(PlacementPathKind.Foreground, result.PlacementPath);
            Assert.Equal(new PlacementClick(135, 255, true), nativeMethods.ForegroundClick);
        }

        [Fact]
        public void Place_BackgroundFrame_PostsClientCoordinateUsingWindowScale()
        {
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods();
            LegacyMovePlacementService service = new LegacyMovePlacementService(
                nativeMethods,
                new RecordingLightweightFactory());

            MovePlacementResult result = service.Place(new MovePlacementRequest
            {
                Frame = CreateBackgroundFrame(),
                Move = new MoveRequest { X = 1, Y = 1 }
            });

            Assert.True(result.Success);
            Assert.Equal(PlacementPathKind.BackgroundPost, result.PlacementPath);
            Assert.Equal(2, nativeMethods.PostedMessages.Count);
            int expectedLParam = BuildMouseLParam(34, 54);
            Assert.All(nativeMethods.PostedMessages, message => Assert.Equal(expectedLParam, message.LParam));
        }

        [Fact]
        public void Place_BackgroundRecognizedSelection_PrefersScreenBoundsOverNormalizedSourceBounds()
        {
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods();
            LegacyMovePlacementService service = new LegacyMovePlacementService(
                nativeMethods,
                new RecordingLightweightFactory());

            MovePlacementResult result = service.Place(new MovePlacementRequest
            {
                Frame = new BoardFrame
                {
                    SyncMode = SyncMode.Background,
                    BoardSize = new BoardDimensions(5, 5),
                    Viewport = new BoardViewport
                    {
                        SourceBounds = new PixelRect(0, 0, 50, 50),
                        ScreenBounds = new PixelRect(320, 240, 50, 50)
                    },
                    Window = new WindowDescriptor
                    {
                        Handle = new IntPtr(2002),
                        Bounds = new PixelRect(300, 200, 100, 100),
                        IsDpiAware = true,
                        DpiScale = 1d
                    }
                },
                Move = new MoveRequest { X = 1, Y = 1 }
            });

            Assert.True(result.Success);
            Assert.Equal(PlacementPathKind.BackgroundPost, result.PlacementPath);
            Assert.Equal(2, nativeMethods.PostedMessages.Count);
            int expectedLParam = BuildMouseLParam(35, 55);
            Assert.All(nativeMethods.PostedMessages, message => Assert.Equal(expectedLParam, message.LParam));
        }

        [Fact]
        public void Place_ForegroundCancellationRequestedAfterActivation_DoesNotClick()
        {
            bool cancelRequested = false;
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods();
            nativeMethods.OnSwitchToWindow = delegate { cancelRequested = true; };
            LegacyMovePlacementService service = new LegacyMovePlacementService(
                nativeMethods,
                new RecordingLightweightFactory());

            MovePlacementResult result = service.Place(new MovePlacementRequest
            {
                Frame = CreateForegroundFrame(),
                Move = new MoveRequest { X = 2, Y = 3 },
                BringTargetToFront = true,
                ShouldCancel = delegate { return cancelRequested; }
            });

            Assert.False(result.Success);
            Assert.Equal(MovePlacementFailureKind.PlacementFailed, result.FailureKind);
            Assert.Equal("Placement cancelled.", result.FailureReason);
            Assert.Equal(0, nativeMethods.ForegroundClickCount);
        }

        private static int BuildMouseLParam(int x, int y)
        {
            return (x & 0xFFFF) | ((y & 0xFFFF) << 16);
        }

        private static BoardFrame CreateForegroundFrame()
        {
            return new BoardFrame
            {
                SyncMode = SyncMode.Foreground,
                BoardSize = new BoardDimensions(5, 5),
                Viewport = new BoardViewport
                {
                    ScreenBounds = new PixelRect(100, 200, 50, 50)
                },
                Window = new WindowDescriptor
                {
                    Handle = new IntPtr(1001),
                    Bounds = new PixelRect(80, 180, 90, 90)
                }
            };
        }

        private static BoardFrame CreateBackgroundFrame()
        {
            return new BoardFrame
            {
                SyncMode = SyncMode.Background,
                BoardSize = new BoardDimensions(5, 5),
                Viewport = new BoardViewport
                {
                    ScreenBounds = new PixelRect(320, 240, 50, 50)
                },
                Window = new WindowDescriptor
                {
                    Handle = new IntPtr(2002),
                    Bounds = new PixelRect(300, 200, 100, 100),
                    IsDpiAware = false,
                    DpiScale = 1.5d
                }
            };
        }

        private sealed class RecordingNativeMethods : IPlacementNativeMethods
        {
            public PlacementClick ForegroundClick { get; private set; }
            public List<PostedMouseMessage> PostedMessages { get; } = new List<PostedMouseMessage>();
            public int ForegroundClickCount { get; private set; }
            public Action OnSwitchToWindow { get; set; }

            public IntPtr FindWindowByClass(string className)
            {
                return IntPtr.Zero;
            }

            public void SwitchToWindow(IntPtr handle)
            {
                if (OnSwitchToWindow != null)
                    OnSwitchToWindow();
            }

            public bool TryForegroundLeftClick(int x, int y, bool addFoxDelay)
            {
                ForegroundClickCount++;
                ForegroundClick = new PlacementClick(x, y, addFoxDelay);
                return true;
            }

            public bool TryPostMouseMessage(IntPtr handle, uint message, int wParam, int lParam)
            {
                PostedMessages.Add(new PostedMouseMessage(handle, message, wParam, lParam));
                return true;
            }

            public void SendMouseMessage(IntPtr handle, uint message, int wParam, int lParam)
            {
            }
        }

        private sealed class RecordingLightweightFactory : IPlacementLightweightInteropFactory
        {
            public IPlacementLightweightInteropClient Create()
            {
                return new RecordingLightweightClient();
            }
        }

        private sealed class RecordingLightweightClient : IPlacementLightweightInteropClient
        {
            public bool BindWindow(IntPtr handle)
            {
                return true;
            }

            public void MoveTo(int x, int y)
            {
            }

            public void LeftClick()
            {
            }

            public void Dispose()
            {
            }
        }

        private readonly record struct PlacementClick(int X, int Y, bool AddFoxDelay);

        private readonly record struct PostedMouseMessage(IntPtr Handle, uint Message, int WParam, int LParam);
    }
}
