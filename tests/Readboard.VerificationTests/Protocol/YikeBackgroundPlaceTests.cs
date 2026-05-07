using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class YikeBackgroundPlaceTests
    {
        [Fact]
        public void place_request_in_yike_mode_uses_background_send_path()
        {
            IntPtr handle = new IntPtr(5151);
            IntPtr childHandle = new IntPtr(6161);
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods
            {
                YikeRenderWidgetHandle = childHandle,
                YikeRenderWidgetBounds = new PixelRect(100, 200, 800, 600)
            };
            RecordingHost host = new RecordingHost(CreateSnapshot(handle));
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            host.AttachCoordinator(coordinator);
            coordinator.AttachHost(host);
            coordinator.SetSyncBoth(true);
            coordinator.AttachRuntime(new SyncSessionRuntimeDependencies
            {
                Host = host,
                CaptureService = new RequestFrameCaptureService(),
                RecognitionService = new StaticRecognitionService(),
                PlacementService = new LegacyMovePlacementService(nativeMethods),
                OverlayService = new PassiveOverlayService(),
                WindowDescriptorFactory = new StaticWindowDescriptorFactory(handle)
            });

            try
            {
                coordinator.Start();
                Assert.True(coordinator.TryStartKeepSync());
                Assert.True(host.KeepStarted.Wait(TimeSpan.FromSeconds(1)));
                Assert.True(transport.WaitForLine("end", TimeSpan.FromSeconds(1)));

                transport.Emit("place 1 2");

                Assert.True(transport.WaitForLine("placeComplete", TimeSpan.FromSeconds(1)));
            }
            finally
            {
                coordinator.Stop();
            }

            Assert.Empty(nativeMethods.ForegroundClicks);
            Assert.Empty(nativeMethods.PostedMessages);
            Assert.Equal(3, nativeMethods.SentMessages.Count);
            Assert.All(nativeMethods.SentMessages, message => Assert.Equal(childHandle, message.Handle));
        }

        [Fact]
        public void yike_geometry_overrides_capture_bounds_and_targets_render_widget_child_window()
        {
            IntPtr handle = new IntPtr(5151);
            IntPtr childHandle = new IntPtr(6262);
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods
            {
                YikeRenderWidgetHandle = childHandle,
                YikeRenderWidgetBounds = new PixelRect(100, 200, 800, 600)
            };
            RecordingHost host = new RecordingHost(CreateSnapshot(handle));
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            host.AttachCoordinator(coordinator);
            coordinator.AttachHost(host);
            coordinator.SetSyncBoth(true);
            coordinator.AttachRuntime(new SyncSessionRuntimeDependencies
            {
                Host = host,
                CaptureService = new RequestFrameCaptureService(),
                RecognitionService = new StaticRecognitionService(),
                PlacementService = new LegacyMovePlacementService(nativeMethods),
                OverlayService = new PassiveOverlayService(),
                WindowDescriptorFactory = new StaticWindowDescriptorFactory(handle)
            });

            try
            {
                coordinator.Start();
                Assert.True(coordinator.TryStartKeepSync());
                Assert.True(host.KeepStarted.Wait(TimeSpan.FromSeconds(1)));
                Assert.True(transport.WaitForLine("end", TimeSpan.FromSeconds(1)));

                transport.Emit("yikeGeometry left=100 top=200 width=250 height=250 board=5");
                transport.Emit("place 1 2");

                Assert.True(transport.WaitForLine("placeComplete", TimeSpan.FromSeconds(1)));
            }
            finally
            {
                coordinator.Stop();
            }

            Assert.Equal("Chrome_RenderWidgetHostHWND", nativeMethods.LastRequestedChildClassName);
            Assert.Equal(3, nativeMethods.SentMessages.Count);
            Assert.All(nativeMethods.SentMessages, message => Assert.Equal(childHandle, message.Handle));
            Assert.All(nativeMethods.SentMessages, message => Assert.Equal(BuildMouseLParam(175, 325), message.LParam));
        }

        [Fact]
        public void yike_geometry_explicit_grid_controls_background_send_coordinate()
        {
            IntPtr handle = new IntPtr(5151);
            IntPtr childHandle = new IntPtr(7272);
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods
            {
                YikeRenderWidgetHandle = childHandle,
                YikeRenderWidgetBounds = new PixelRect(100, 200, 800, 600)
            };
            RecordingHost host = new RecordingHost(CreateSnapshot(handle));
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            host.AttachCoordinator(coordinator);
            coordinator.AttachHost(host);
            coordinator.SetSyncBoth(true);
            coordinator.AttachRuntime(new SyncSessionRuntimeDependencies
            {
                Host = host,
                CaptureService = new RequestFrameCaptureService(),
                RecognitionService = new StaticRecognitionService(),
                PlacementService = new LegacyMovePlacementService(nativeMethods),
                OverlayService = new PassiveOverlayService(),
                WindowDescriptorFactory = new StaticWindowDescriptorFactory(handle)
            });

            try
            {
                coordinator.Start();
                Assert.True(coordinator.TryStartKeepSync());
                Assert.True(host.KeepStarted.Wait(TimeSpan.FromSeconds(1)));
                Assert.True(transport.WaitForLine("end", TimeSpan.FromSeconds(1)));

                transport.Emit("yikeGeometry left=45 top=60 width=656 height=640 board=19 firstX=81 firstY=97 cellX=32 cellY=31");
                transport.Emit("place 1 2");

                Assert.True(transport.WaitForLine("placeComplete", TimeSpan.FromSeconds(1)));
            }
            finally
            {
                coordinator.Stop();
            }

            Assert.Equal(3, nativeMethods.SentMessages.Count);
            Assert.All(nativeMethods.SentMessages, message => Assert.Equal(childHandle, message.Handle));
            Assert.All(nativeMethods.SentMessages, message => Assert.Equal(BuildMouseLParam(113, 159), message.LParam));
        }

        private static int BuildMouseLParam(int x, int y)
        {
            return (x & 0xFFFF) | ((y & 0xFFFF) << 16);
        }

        private static SyncCoordinatorHostSnapshot CreateSnapshot(IntPtr handle)
        {
            return new SyncCoordinatorHostSnapshot
            {
                SyncMode = SyncMode.Yike,
                BoardWidth = 19,
                BoardHeight = 19,
                SelectionBounds = new PixelRect(10, 20, 190, 190),
                SelectedWindowHandle = handle,
                DpiScale = 1f,
                LegacyTypeToken = "6",
                ShowInBoard = false,
                SupportsForegroundFoxInBoardProtocol = false,
                AutoMinimize = false,
                SampleIntervalMs = 5
            };
        }

        private sealed class RecordingHost : ISyncCoordinatorHost, IProtocolCommandHost
        {
            private readonly SyncCoordinatorHostSnapshot snapshot;
            private SyncSessionCoordinator coordinator;

            public RecordingHost(SyncCoordinatorHostSnapshot snapshot)
            {
                this.snapshot = snapshot;
            }

            public ManualResetEventSlim KeepStarted { get; } = new ManualResetEventSlim(false);

            public void AttachCoordinator(SyncSessionCoordinator value)
            {
                coordinator = value;
            }

            public SyncCoordinatorHostSnapshot CaptureSnapshot()
            {
                return snapshot;
            }

            public void UpdateSelectedWindowHandle(IntPtr handle)
            {
                snapshot.SelectedWindowHandle = handle;
            }

            public void OnKeepSyncStarted()
            {
                KeepStarted.Set();
            }

            public void OnKeepSyncStopped(bool continuousSyncActive)
            {
            }

            public void OnContinuousSyncStarted()
            {
            }

            public void OnContinuousSyncStopped()
            {
            }

            public void OnSyncCachesReset()
            {
            }

            public void ShowMissingSyncSourceMessage()
            {
            }

            public void ShowRecognitionFailureMessage()
            {
            }

            public void MinimizeWindow()
            {
            }

            public bool TrySendPlaceProtocolError(string message)
            {
                return true;
            }


            public void DispatchProtocolCommand(Action command)
            {
                command();
            }

            public void HandlePlaceRequest(MoveRequest request)
            {
                PlaceRequestExecutionResult result = coordinator.HandlePlaceRequest(request);
                if (result.ShouldSendResponse)
                    coordinator.SendPlacementResult(result.Success);
            }

            public void HandleYikeContext(YikeWindowContext context)
            {
            }

            public void HandleYikeGeometry(YikeBoardGeometry geometry)
            {
                coordinator.SetYikeGeometry(geometry);
            }

            public void HandleLossFocus()
            {
            }

            public void HandleStopInBoardRequest()
            {
            }

            public void HandleVersionRequest()
            {
            }

            public void HandleQuitRequest()
            {
            }

            public void HandleReadboardUpdateSupported()
            {
            }

            public void HandleReadboardUpdateInstalling()
            {
            }

            public void HandleReadboardUpdateCancelled()
            {
            }

            public void HandleReadboardUpdateFailed(string message)
            {
            }
        }

        private sealed class RequestFrameCaptureService : IBoardCaptureService
        {
            public BoardCaptureResult Capture(BoardCaptureRequest request)
            {
                return BoardCaptureResult.CreateSuccess(
                    new BoardFrame
                    {
                        SyncMode = request.SyncMode,
                        BoardSize = request.BoardSize,
                        Window = request.Window,
                        Viewport = new BoardViewport
                        {
                            SourceBounds = new PixelRect(0, 0, 190, 190),
                            ScreenBounds = new PixelRect(100, 200, 190, 190),
                            CellWidth = 10d,
                            CellHeight = 10d
                        }
                    },
                    CapturePathKind.WindowBitmap);
            }
        }

        private sealed class StaticRecognitionService : IBoardRecognitionService
        {
            public BoardRecognitionResult Recognize(BoardRecognitionRequest request)
            {
                return new BoardRecognitionResult
                {
                    Success = true,
                    Viewport = request.Frame.Viewport,
                    Snapshot = new BoardSnapshot
                    {
                        Width = 19,
                        Height = 19,
                        IsValid = true,
                        Payload = "re=yike",
                        ProtocolLines = new[] { "re=yike" }
                    }
                };
            }
        }

        private sealed class StaticWindowDescriptorFactory : IWindowDescriptorFactory
        {
            private readonly IntPtr expectedHandle;

            public StaticWindowDescriptorFactory(IntPtr expectedHandle)
            {
                this.expectedHandle = expectedHandle;
            }

            public bool TryCreate(IntPtr handle, out WindowDescriptor descriptor)
            {
                descriptor = null;
                if (handle != expectedHandle)
                    return false;

                descriptor = new WindowDescriptor
                {
                    Handle = handle,
                    Bounds = new PixelRect(100, 200, 190, 190),
                    ClassName = "SunAwtFrame",
                    Title = "弈客大厅",
                    IsDpiAware = true,
                    DpiScale = 1d
                };
                return true;
            }
        }

        private sealed class PassiveOverlayService : IOverlayService
        {
            public OverlayUpdateResult BuildUpdate(OverlayUpdateRequest request)
            {
                return null;
            }

            public void Reset()
            {
            }
        }

        private sealed class RecordingTransport : IReadBoardTransport
        {
            private readonly ManualResetEventSlim lineEvent = new ManualResetEventSlim(false);

            public event EventHandler<string> MessageReceived;

            public bool IsConnected { get; private set; }

            public List<string> SentLines { get; } = new List<string>();

            public void Dispose()
            {
            }

            public void Emit(string line)
            {
                MessageReceived?.Invoke(this, line);
            }

            public void Send(string line)
            {
                lock (SentLines)
                {
                    SentLines.Add(line);
                    lineEvent.Set();
                }
            }

            public void SendError(string message)
            {
            }

            public void Start()
            {
                IsConnected = true;
            }

            public void Stop()
            {
                IsConnected = false;
            }

            public bool WaitForLine(string line, TimeSpan timeout)
            {
                DateTime deadline = DateTime.UtcNow.Add(timeout);
                while (DateTime.UtcNow < deadline)
                {
                    lock (SentLines)
                    {
                        if (SentLines.Contains(line))
                            return true;
                    }

                    lineEvent.Wait(TimeSpan.FromMilliseconds(25));
                    lineEvent.Reset();
                }

                lock (SentLines)
                    return SentLines.Contains(line);
            }
        }

        private sealed class RecordingNativeMethods : IPlacementNativeMethods
        {
            public List<(int X, int Y, bool Hold)> ForegroundClicks { get; } = new List<(int X, int Y, bool Hold)>();
            public List<MouseMessage> PostedMessages { get; } = new List<MouseMessage>();
            public List<MouseMessage> SentMessages { get; } = new List<MouseMessage>();
            public IntPtr YikeRenderWidgetHandle { get; set; }
            public PixelRect YikeRenderWidgetBounds { get; set; }
            public string LastRequestedChildClassName { get; private set; }

            public IntPtr FindWindowByClass(string className)
            {
                return IntPtr.Zero;
            }

            public IntPtr FindChildWindowByClass(IntPtr parentHandle, string className)
            {
                LastRequestedChildClassName = className;
                return YikeRenderWidgetHandle;
            }

            public bool TryGetWindowBounds(IntPtr handle, out PixelRect bounds)
            {
                bounds = null;
                if (handle == YikeRenderWidgetHandle && YikeRenderWidgetBounds != null)
                {
                    bounds = YikeRenderWidgetBounds;
                    return true;
                }
                return false;
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
