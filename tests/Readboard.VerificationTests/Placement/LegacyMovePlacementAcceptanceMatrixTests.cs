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
        }

        [Fact]
        public void Place_FoxLightweightInteropUsesLwWhenDllIsAvailable()
        {
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods();
            RecordingLightweightClient client = new RecordingLightweightClient();
            LegacyMovePlacementService service = new LegacyMovePlacementService(
                nativeMethods,
                new RecordingLightweightFactory(client));

            MovePlacementResult result = service.Place(CreateRequest(SyncMode.Fox, useLightweightInterop: true));

            Assert.True(result.Success);
            Assert.Equal(PlacementPathKind.LightweightInterop, result.PlacementPath);
            Assert.Equal(new IntPtr(3003), client.BoundHandle);
            Assert.Equal((35, 55), client.MoveToPoint);
            Assert.True(client.LeftClicked);
            Assert.True(client.DisposeCalled);
            Assert.Empty(nativeMethods.PostedMessages);
            Assert.Empty(nativeMethods.SentMessages);
        }

        [Fact]
        public void Place_FoxLightweightInteropFailsWhenDllIsUnavailable()
        {
            LegacyMovePlacementService service = new LegacyMovePlacementService(
                new RecordingNativeMethods(),
                new ThrowingLightweightFactory(new DllNotFoundException("lw.dll missing")));

            MovePlacementResult result = service.Place(CreateRequest(SyncMode.Fox, useLightweightInterop: true));

            Assert.False(result.Success);
            Assert.Equal(PlacementPathKind.LightweightInterop, result.PlacementPath);
            Assert.Equal(MovePlacementFailureKind.PlacementFailed, result.FailureKind);
            Assert.Contains("lw.dll", result.FailureReason);
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
            LegacyMovePlacementService service = new LegacyMovePlacementService(
                nativeMethods,
                new RecordingLightweightFactory(new RecordingLightweightClient()));

            MovePlacementResult result = service.Place(CreateRequest(syncMode, useLightweightInterop: false));

            Assert.True(result.Success);
            Assert.Equal(expectedPath, result.PlacementPath);
            Assert.Empty(nativeMethods.ForegroundClicks);
            AssertPlacementMessages(expectedPath, nativeMethods);
        }

        private static void AssertPlacementMessages(
            PlacementPathKind expectedPath,
            RecordingNativeMethods nativeMethods)
        {
            int expectedLParam = BuildMouseLParam(35, 55);
            if (expectedPath == PlacementPathKind.BackgroundPost)
            {
                Assert.Equal(2, nativeMethods.PostedMessages.Count);
                Assert.Empty(nativeMethods.SentMessages);
                Assert.All(nativeMethods.PostedMessages, message => Assert.Equal(expectedLParam, message.LParam));
                return;
            }

            Assert.Empty(nativeMethods.PostedMessages);
            Assert.Equal(3, nativeMethods.SentMessages.Count);
            Assert.All(nativeMethods.SentMessages, message => Assert.Equal(expectedLParam, message.LParam));
        }

        private static int BuildMouseLParam(int x, int y)
        {
            return (x & 0xFFFF) | ((y & 0xFFFF) << 16);
        }

        private static MovePlacementRequest CreateRequest(SyncMode syncMode, bool useLightweightInterop)
        {
            return new MovePlacementRequest
            {
                Frame = new BoardFrame
                {
                    SyncMode = syncMode,
                    BoardSize = new BoardDimensions(5, 5),
                    Viewport = new BoardViewport
                    {
                        SourceBounds = new PixelRect(20, 30, 50, 50)
                    },
                    Window = new WindowDescriptor
                    {
                        Handle = new IntPtr(3003),
                        Bounds = new PixelRect(100, 200, 150, 150),
                        IsDpiAware = true,
                        DpiScale = 1d
                    }
                },
                Move = new MoveRequest { X = 1, Y = 2 },
                UseLightweightInterop = useLightweightInterop
            };
        }

        private sealed class RecordingNativeMethods : IPlacementNativeMethods
        {
            public List<(int X, int Y, bool Hold)> ForegroundClicks { get; } = new List<(int X, int Y, bool Hold)>();
            public List<MouseMessage> PostedMessages { get; } = new List<MouseMessage>();
            public List<MouseMessage> SentMessages { get; } = new List<MouseMessage>();

            public IntPtr FindWindowByClass(string className)
            {
                return IntPtr.Zero;
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

            public void SendMouseMessage(IntPtr handle, uint message, int wParam, int lParam)
            {
                SentMessages.Add(new MouseMessage(handle, message, wParam, lParam));
            }
        }

        private sealed class RecordingLightweightFactory : IPlacementLightweightInteropFactory
        {
            private readonly RecordingLightweightClient client;

            public RecordingLightweightFactory(RecordingLightweightClient client)
            {
                this.client = client;
            }

            public IPlacementLightweightInteropClient Create()
            {
                return client;
            }
        }

        private sealed class ThrowingLightweightFactory : IPlacementLightweightInteropFactory
        {
            private readonly Exception exception;

            public ThrowingLightweightFactory(Exception exception)
            {
                this.exception = exception;
            }

            public IPlacementLightweightInteropClient Create()
            {
                throw exception;
            }
        }

        private sealed class RecordingLightweightClient : IPlacementLightweightInteropClient
        {
            public IntPtr BoundHandle { get; private set; }
            public (int X, int Y) MoveToPoint { get; private set; }
            public bool LeftClicked { get; private set; }
            public bool DisposeCalled { get; private set; }

            public bool BindWindow(IntPtr handle)
            {
                BoundHandle = handle;
                return true;
            }

            public void MoveTo(int x, int y)
            {
                MoveToPoint = (x, y);
            }

            public void LeftClick()
            {
                LeftClicked = true;
            }

            public void Dispose()
            {
                DisposeCalled = true;
            }
        }

        private readonly record struct MouseMessage(IntPtr Handle, uint Message, int WParam, int LParam);
    }
}
