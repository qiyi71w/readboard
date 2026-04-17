using System;
using System.IO;

namespace readboard
{
    internal static class PlacementCleanupTddHarness
    {
        private sealed class RecordingNativeMethods : IPlacementNativeMethods
        {
            public IntPtr FoxDialogHandleToReturn { get; set; }
            public string LastWindowClassQuery { get; private set; }
            public IntPtr LastSwitchedHandle { get; private set; }
            public int ClickCount { get; private set; }
            public int LastClickX { get; private set; }
            public int LastClickY { get; private set; }
            public bool LastHoldButtonBeforeRelease { get; private set; }

            public IntPtr FindWindowByClass(string className)
            {
                LastWindowClassQuery = className;
                return FoxDialogHandleToReturn;
            }

            public void SwitchToWindow(IntPtr handle)
            {
                LastSwitchedHandle = handle;
            }

            public bool TryForegroundLeftClick(int x, int y, bool holdButtonBeforeRelease)
            {
                ClickCount++;
                LastClickX = x;
                LastClickY = y;
                LastHoldButtonBeforeRelease = holdButtonBeforeRelease;
                return true;
            }

            public bool TryPostMouseMessage(IntPtr handle, uint message, int wParam, int lParam)
            {
                throw new NotSupportedException();
            }

            public void SendMouseMessage(IntPtr handle, uint message, int wParam, int lParam)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class StubLightweightInteropFactory : IPlacementLightweightInteropFactory
        {
            public IPlacementLightweightInteropClient Create()
            {
                throw new NotSupportedException();
            }
        }

        public static int Main()
        {
            try
            {
                VerifySourceRemovesExplicitSleepAndDeadTail();
                VerifyFoxForegroundPlacementStillRequestsButtonHold();
                VerifyStandardForegroundPlacementDoesNotRequestFoxButtonHold();
                Console.WriteLine("GREEN");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("RED: " + ex.Message);
                return 1;
            }
        }

        private static void VerifySourceRemovesExplicitSleepAndDeadTail()
        {
            string placementSource = File.ReadAllText(ResolveSourcePath("readboard/Core/Placement/IMovePlacementService.cs"));
            string frameSource = File.ReadAllText(ResolveSourcePath("readboard/Core/Models/BoardFrame.cs"));
            string captureResultSource = File.ReadAllText(ResolveSourcePath("readboard/Core/Capture/BoardCaptureResult.cs"));
            string captureServiceSource = File.ReadAllText(ResolveSourcePath("readboard/Core/Capture/IBoardCaptureService.cs"));

            Assert(!placementSource.Contains("Thread.Sleep("), "Placement path should not rely on explicit Thread.Sleep coordination.");
            Assert(!placementSource.Contains("addFoxDelay"), "Legacy addFoxDelay helper tail should be removed from placement source.");
            Assert(!frameSource.Contains("UsedEnhancedCapture"), "Unused capture tail should be removed from BoardFrame.");
            Assert(!captureResultSource.Contains("UsedEnhancedCapture"), "Unused capture tail should be removed from BoardCapturePlan.");
            Assert(!captureServiceSource.Contains("usedEnhancedCapture"), "Capture service should stop threading unused enhanced-capture flags through the main path.");
        }

        private static void VerifyFoxForegroundPlacementStillRequestsButtonHold()
        {
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods
            {
                FoxDialogHandleToReturn = new IntPtr(777)
            };
            LegacyMovePlacementService service = new LegacyMovePlacementService(
                nativeMethods,
                new StubLightweightInteropFactory());

            MovePlacementResult result = service.Place(CreateForegroundRequest(SyncMode.Fox));

            Assert(result != null && result.Success, "Fox foreground placement should succeed.");
            Assert(result.PlacementPath == PlacementPathKind.Foreground, "Fox foreground placement should stay on the foreground path.");
            Assert(nativeMethods.ClickCount == 1, "Fox foreground placement should click exactly once.");
            Assert(nativeMethods.LastHoldButtonBeforeRelease, "Fox foreground placement should still request a held button before release.");
            Assert(nativeMethods.LastWindowClassQuery == "#32770", "Fox foreground placement should still look for the dialog activation window.");
            Assert(nativeMethods.LastSwitchedHandle == new IntPtr(777), "Fox foreground placement should still activate the Fox dialog when present.");
            Assert(nativeMethods.LastClickX == 170 && nativeMethods.LastClickY == 290, "Fox foreground placement should preserve coordinate translation.");
        }

        private static void VerifyStandardForegroundPlacementDoesNotRequestFoxButtonHold()
        {
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods();
            LegacyMovePlacementService service = new LegacyMovePlacementService(
                nativeMethods,
                new StubLightweightInteropFactory());

            MovePlacementResult result = service.Place(CreateForegroundRequest(SyncMode.Foreground));

            Assert(result != null && result.Success, "Standard foreground placement should succeed.");
            Assert(result.PlacementPath == PlacementPathKind.Foreground, "Standard foreground placement should stay on the foreground path.");
            Assert(nativeMethods.ClickCount == 1, "Standard foreground placement should click exactly once.");
            Assert(!nativeMethods.LastHoldButtonBeforeRelease, "Standard foreground placement should not request the Fox button hold.");
            Assert(nativeMethods.LastSwitchedHandle == IntPtr.Zero, "Standard foreground placement should not force activation by default.");
            Assert(nativeMethods.LastClickX == 170 && nativeMethods.LastClickY == 290, "Standard foreground placement should preserve coordinate translation.");
        }

        private static MovePlacementRequest CreateForegroundRequest(SyncMode syncMode)
        {
            return new MovePlacementRequest
            {
                Frame = new BoardFrame
                {
                    SyncMode = syncMode,
                    BoardSize = new BoardDimensions(19, 19),
                    Viewport = new BoardViewport
                    {
                        ScreenBounds = new PixelRect(100, 200, 380, 380),
                        CellWidth = 20d,
                        CellHeight = 20d
                    },
                    Window = new WindowDescriptor
                    {
                        Handle = new IntPtr(1234)
                    }
                },
                Move = new MoveRequest
                {
                    X = 3,
                    Y = 4
                }
            };
        }

        private static string ResolveSourcePath(string relativePath)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                string candidate = baseDirectory;
                for (int level = 0; level < i; level++)
                    candidate = Path.Combine(candidate, "..");

                candidate = Path.GetFullPath(Path.Combine(candidate, relativePath));
                if (File.Exists(candidate))
                    return candidate;
            }

            throw new InvalidOperationException("Unable to resolve source path: " + relativePath);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
