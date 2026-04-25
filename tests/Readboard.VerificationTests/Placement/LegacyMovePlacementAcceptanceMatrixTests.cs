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
            yield return new object[] { (int)SyncMode.Yike, (int)PlacementPathKind.BackgroundSend };
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
            AssertPlacementMessages(expectedPath, nativeMethods);
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
                Move = new MoveRequest { X = 1, Y = 2 }
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

        private readonly record struct MouseMessage(IntPtr Handle, uint Message, int WParam, int LParam);
    }
}
