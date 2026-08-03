using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Xunit;
using readboard;
using Readboard.VerificationTests.Support;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class YikeBackgroundPlaceTests
    {
        [Fact]
        public void place_request_in_yike_mode_with_geometry_uses_background_post_path()
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
                VerificationCompletion.Wait(host.KeepStarted, "Keep sync did not start.");
                Assert.True(transport.WaitForLine("end"));

                transport.Emit("yikeGeometry left=100 top=200 width=250 height=250 board=5");
                transport.Emit("place 1 2");

                Assert.True(transport.WaitForLine("placeComplete"));
            }
            finally
            {
                coordinator.Stop();
            }

            Assert.Empty(nativeMethods.ForegroundClicks);
            Assert.Empty(nativeMethods.SentMessages);
            Assert.Equal(3, nativeMethods.PostedMessages.Count);
            Assert.All(nativeMethods.PostedMessages, message => Assert.Equal(childHandle, message.Handle));
        }

        [Fact]
        public void yike_place_without_geometry_fails_without_using_capture_frame()
        {
            IntPtr handle = new IntPtr(5151);
            IntPtr childHandle = new IntPtr(6161);
            string diagnosticsRoot = CreateDiagnosticsRoot();
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods
            {
                YikeRenderWidgetHandle = childHandle,
                YikeRenderWidgetBounds = new PixelRect(100, 200, 800, 600)
            };
            RecordingHost host = new RecordingHost(CreateSnapshot(handle));
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            BoardDebugDiagnosticsWriter debugDiagnostics = new BoardDebugDiagnosticsWriter(diagnosticsRoot, () => true);
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
                WindowDescriptorFactory = new StaticWindowDescriptorFactory(handle),
                DebugDiagnostics = debugDiagnostics
            });

            try
            {
                coordinator.Start();
                Assert.True(coordinator.TryStartKeepSync());
                VerificationCompletion.Wait(host.KeepStarted, "Keep sync did not start.");
                Assert.True(transport.WaitForLine("end"));

                transport.Emit("place 1 2");

                Assert.True(transport.WaitForLine("error place failed"));
            }
            finally
            {
                coordinator.Stop();
                debugDiagnostics.Dispose();
            }

            try
            {
                Assert.Empty(nativeMethods.ForegroundClicks);
                Assert.Empty(nativeMethods.SentMessages);
                Assert.Empty(nativeMethods.PostedMessages);
                Assert.Contains(host.PlaceErrors, error => error.Contains(SyncSessionCoordinator.YikeGeometryUnavailableFailureReason));
                AssertPlacementSkippedMetadata(diagnosticsRoot, 1, 2);
            }
            finally
            {
                DeleteDiagnosticsRoot(diagnosticsRoot);
            }
        }

        [Fact]
        public void yike_geometry_clear_prevents_reusing_previous_geometry()
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
                VerificationCompletion.Wait(host.KeepStarted, "Keep sync did not start.");
                Assert.True(transport.WaitForLine("end"));

                transport.Emit("yikeGeometry left=100 top=200 width=250 height=250 board=5");
                transport.Emit("yikeGeometry");
                transport.Emit("place 1 2");

                Assert.True(transport.WaitForLine("error place failed"));
            }
            finally
            {
                coordinator.Stop();
            }

            Assert.Empty(nativeMethods.ForegroundClicks);
            Assert.Empty(nativeMethods.SentMessages);
            Assert.Empty(nativeMethods.PostedMessages);
            Assert.Contains(host.PlaceErrors, error => error.Contains(SyncSessionCoordinator.YikeGeometryUnavailableFailureReason));
        }

        [Fact]
        public void yike_place_without_selected_window_handle_fails()
        {
            string diagnosticsRoot = CreateDiagnosticsRoot();
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods
            {
                YikeRenderWidgetHandle = new IntPtr(6161),
                YikeRenderWidgetBounds = new PixelRect(100, 200, 800, 600)
            };
            RecordingHost host = new RecordingHost(CreateSnapshot(IntPtr.Zero))
            {
                ThrowOnPlaceError = true
            };
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(new RecordingTransport(), new LegacyProtocolAdapter());
            BoardDebugDiagnosticsWriter debugDiagnostics = new BoardDebugDiagnosticsWriter(diagnosticsRoot, () => true);
            SyncSessionRuntimeDependencies runtime = new SyncSessionRuntimeDependencies
            {
                Host = host,
                CaptureService = new RequestFrameCaptureService(),
                RecognitionService = new StaticRecognitionService(),
                PlacementService = new LegacyMovePlacementService(nativeMethods),
                OverlayService = new PassiveOverlayService(),
                WindowDescriptorFactory = new StaticWindowDescriptorFactory(IntPtr.Zero),
                DebugDiagnostics = debugDiagnostics
            };
            coordinator.AttachRuntime(runtime);
            coordinator.SetYikeGeometry(new YikeBoardGeometry
            {
                Bounds = new PixelRect(100, 200, 250, 250),
                BoardSize = 5
            });

            try
            {
                bool success = true;
                Exception exception = Record.Exception(() =>
                    success = InvokePlacePendingMove(
                        coordinator,
                        runtime,
                        host.CaptureSnapshot(),
                        new MoveRequest { X = 1, Y = 2, VerifyMove = false },
                        () => true));

                Assert.Null(exception);
                Assert.False(success);
            }
            finally
            {
                debugDiagnostics.Dispose();
            }

            try
            {
                Assert.Equal(1, host.PlaceErrorCallCount);
                Assert.Contains(host.PlaceErrors, error => error.Contains(SyncSessionCoordinator.YikeGeometryUnavailableFailureReason));
                Assert.Null(nativeMethods.LastRequestedChildClassName);
                Assert.Empty(nativeMethods.ForegroundClicks);
                Assert.Empty(nativeMethods.SentMessages);
                Assert.Empty(nativeMethods.PostedMessages);
                AssertPlacementSkippedMetadata(diagnosticsRoot, 1, 2);
            }
            finally
            {
                DeleteDiagnosticsRoot(diagnosticsRoot);
            }
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
                VerificationCompletion.Wait(host.KeepStarted, "Keep sync did not start.");
                Assert.True(transport.WaitForLine("end"));

                transport.Emit("yikeGeometry left=100 top=200 width=250 height=250 board=5");
                transport.Emit("place 1 2");

                Assert.True(transport.WaitForLine("placeComplete"));
            }
            finally
            {
                coordinator.Stop();
            }

            Assert.Equal("Chrome_RenderWidgetHostHWND", nativeMethods.LastRequestedChildClassName);
            Assert.Equal(3, nativeMethods.PostedMessages.Count);
            Assert.All(nativeMethods.PostedMessages, message => Assert.Equal(childHandle, message.Handle));
            Assert.All(nativeMethods.PostedMessages, message => Assert.Equal(BuildMouseLParam(175, 325), message.LParam));
        }

        [Fact]
        public void yike_geometry_targets_current_selected_handle_when_capture_frame_has_old_handle()
        {
            IntPtr oldHandle = new IntPtr(5151);
            IntPtr oldChildHandle = new IntPtr(6161);
            IntPtr currentHandle = new IntPtr(7171);
            IntPtr currentChildHandle = new IntPtr(8181);
            RecordingNativeMethods nativeMethods = new RecordingNativeMethods
            {
                YikeRenderWidgetBounds = new PixelRect(100, 200, 800, 600)
            };
            nativeMethods.YikeRenderWidgetHandles[oldHandle] = oldChildHandle;
            nativeMethods.YikeRenderWidgetHandles[currentHandle] = currentChildHandle;
            nativeMethods.WindowBounds[oldChildHandle] = new PixelRect(100, 200, 800, 600);
            nativeMethods.WindowBounds[currentChildHandle] = new PixelRect(100, 200, 800, 600);
            RecordingHost host = new RecordingHost(CreateSnapshot(currentHandle));
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
                WindowDescriptorFactory = new StaticWindowDescriptorFactory(currentHandle)
            });

            try
            {
                coordinator.Start();
                Assert.True(coordinator.TryStartKeepSync());
                VerificationCompletion.Wait(host.KeepStarted, "Keep sync did not start.");
                Assert.True(transport.WaitForLine("end"));

                ReplaceRuntimeYikeFrame(coordinator, currentHandle, oldHandle);
                transport.Emit("yikeGeometry left=100 top=200 width=250 height=250 board=5");
                transport.Emit("place 1 2");

                Assert.True(transport.WaitForLine("placeComplete"));
            }
            finally
            {
                coordinator.Stop();
            }

            Assert.Equal(3, nativeMethods.PostedMessages.Count);
            Assert.All(nativeMethods.PostedMessages, message => Assert.Equal(currentChildHandle, message.Handle));
            Assert.DoesNotContain(nativeMethods.PostedMessages, message => message.Handle == oldChildHandle);
        }

        [Fact]
        public void yike_geometry_explicit_grid_controls_background_post_coordinate()
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
                VerificationCompletion.Wait(host.KeepStarted, "Keep sync did not start.");
                Assert.True(transport.WaitForLine("end"));

                transport.Emit("yikeGeometry left=45 top=60 width=656 height=640 board=19 firstX=81 firstY=97 cellX=32 cellY=31");
                transport.Emit("place 1 2");

                Assert.True(transport.WaitForLine("placeComplete"));
            }
            finally
            {
                coordinator.Stop();
            }

            Assert.Equal(3, nativeMethods.PostedMessages.Count);
            Assert.All(nativeMethods.PostedMessages, message => Assert.Equal(childHandle, message.Handle));
            Assert.All(nativeMethods.PostedMessages, message => Assert.Equal(BuildMouseLParam(113, 159), message.LParam));
        }

        [Fact]
        public void yike_place_can_succeed_with_geometry_even_when_capture_keeps_failing()
        {
            IntPtr handle = new IntPtr(5151);
            IntPtr childHandle = new IntPtr(8383);
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
                CaptureService = new AlwaysFailCaptureService(),
                RecognitionService = new StaticRecognitionService(),
                PlacementService = new LegacyMovePlacementService(nativeMethods),
                OverlayService = new PassiveOverlayService(),
                WindowDescriptorFactory = new StaticWindowDescriptorFactory(handle)
            });

            try
            {
                coordinator.Start();
                Assert.True(coordinator.TryStartKeepSync());
                VerificationCompletion.Wait(host.KeepStarted, "Keep sync did not start.");

                transport.Emit("yikeGeometry left=45 top=60 width=656 height=640 board=19 firstX=81 firstY=97 cellX=32 cellY=31");
                transport.Emit("place 1 2");

                Assert.True(
                    transport.WaitForLine("placeComplete"),
                    "Expected placeComplete, got lines: " + string.Join(", ", transport.SnapshotSentLines()));
            }
            finally
            {
                coordinator.Stop();
            }

            Assert.Equal(3, nativeMethods.PostedMessages.Count);
            Assert.All(nativeMethods.PostedMessages, message => Assert.Equal(childHandle, message.Handle));
            Assert.All(nativeMethods.PostedMessages, message => Assert.Equal(BuildMouseLParam(113, 159), message.LParam));
        }

        private static int BuildMouseLParam(int x, int y)
        {
            return (x & 0xFFFF) | ((y & 0xFFFF) << 16);
        }

        private static void ReplaceRuntimeYikeFrame(SyncSessionCoordinator coordinator, IntPtr selectedHandle, IntPtr frameHandle)
        {
            FieldInfo field = typeof(SyncSessionCoordinator).GetField("runtimeState", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            SyncSessionRuntimeState runtimeState = (SyncSessionRuntimeState)field.GetValue(coordinator);
            runtimeState.SelectedWindowHandle = selectedHandle;
            runtimeState.CurrentBoardFrame = new BoardFrame
            {
                SyncMode = SyncMode.Yike,
                BoardSize = new BoardDimensions(5, 5),
                Window = new WindowDescriptor
                {
                    Handle = frameHandle,
                    ClassName = "SunAwtFrame",
                    Bounds = new PixelRect(1, 2, 190, 190),
                    IsDpiAware = true,
                    DpiScale = 1d,
                    IsJavaWindow = true
                },
                Viewport = new BoardViewport
                {
                    SourceBounds = new PixelRect(0, 0, 190, 190),
                    CellWidth = 10d,
                    CellHeight = 10d
                }
            };
        }

        private static bool InvokePlacePendingMove(
            SyncSessionCoordinator coordinator,
            SyncSessionRuntimeDependencies runtime,
            SyncCoordinatorHostSnapshot snapshot,
            MoveRequest request,
            Func<bool> isOperationCurrent)
        {
            MethodInfo method = typeof(SyncSessionCoordinator).GetMethod(
                "PlacePendingMove",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (bool)method.Invoke(coordinator, new object[] { runtime, snapshot, request, isOperationCurrent });
        }

        private static string CreateDiagnosticsRoot()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), "readboard-yike-place-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return rootPath;
        }

        private static void DeleteDiagnosticsRoot(string rootPath)
        {
            if (Directory.Exists(rootPath))
                Directory.Delete(rootPath, true);
        }

        private static void AssertPlacementSkippedMetadata(string diagnosticsRoot, int expectedX, int expectedY)
        {
            string eventDirectory = Assert.Single(Directory.GetDirectories(diagnosticsRoot, "*placement-skipped"));
            using (JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(eventDirectory, "metadata.json"))))
            {
                JsonElement root = metadata.RootElement;
                Assert.Equal("placement-skipped", root.GetProperty("EventName").GetString());
                Assert.Equal(expectedX, root.GetProperty("PlacementX").GetInt32());
                Assert.Equal(expectedY, root.GetProperty("PlacementY").GetInt32());
                Assert.Equal(0L, root.GetProperty("PlacementTargetHandle").GetInt64());
                Assert.Equal(JsonValueKind.Null, root.GetProperty("PlacementClientX").ValueKind);
                Assert.Equal(JsonValueKind.Null, root.GetProperty("PlacementClientY").ValueKind);
                Assert.Equal(JsonValueKind.Null, root.GetProperty("PlacementMouseLParam").ValueKind);
            }
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

            public List<string> PlaceErrors { get; } = new List<string>();

            public bool ThrowOnPlaceError { get; set; }

            public int PlaceErrorCallCount { get; private set; }

            public void AttachCoordinator(SyncSessionCoordinator value)
            {
                coordinator = value;
            }

            public SyncCoordinatorHostSnapshot CaptureSnapshot()
            {
                return snapshot;
            }

            public long AllocateSessionObservationGeneration()
            {
                return 0;
            }

            public void UpdateSelectedWindowHandle(IntPtr handle, long observationGeneration)
            {
                snapshot.SelectedWindowHandle = handle;
            }

            public void OnKeepSyncStarted(long observationGeneration)
            {
                KeepStarted.Set();
            }

            public void OnKeepSyncStopped(bool continuousSyncActive, long observationGeneration)
            {
            }

            public void OnContinuousSyncStarted(long observationGeneration)
            {
            }

            public void OnContinuousSyncStopped(long observationGeneration)
            {
            }

            public void OnSyncCachesReset(long observationGeneration)
            {
            }

            public void OnBoardSnapshotRecognized(
                BoardSnapshot snapshot,
                TimeSpan duration,
                long observationGeneration)
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
                lock (PlaceErrors)
                {
                    PlaceErrorCallCount++;
                    PlaceErrors.Add(message);
                }
                if (ThrowOnPlaceError)
                    throw new InvalidOperationException("place error gate failed");
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

            public void HandleReadboardUpdatePackageV2Supported()
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

        private sealed class AlwaysFailCaptureService : IBoardCaptureService
        {
            public BoardCaptureResult Capture(BoardCaptureRequest request)
            {
                return new BoardCaptureResult
                {
                    Success = false,
                    FailureReason = "capture-failed-for-test"
                };
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

            public bool WaitForLine(string line)
            {
                while (true)
                {
                    lock (SentLines)
                    {
                        if (SentLines.Contains(line))
                            return true;
                    }

                    VerificationCompletion.Wait(lineEvent, "Expected Yike protocol line was not sent.");
                    lineEvent.Reset();
                }
            }

            public string[] SnapshotSentLines()
            {
                lock (SentLines)
                    return SentLines.ToArray();
            }
        }

        private sealed class RecordingNativeMethods : IPlacementNativeMethods
        {
            public List<(int X, int Y, bool Hold)> ForegroundClicks { get; } = new List<(int X, int Y, bool Hold)>();
            public List<MouseMessage> PostedMessages { get; } = new List<MouseMessage>();
            public List<MouseMessage> SentMessages { get; } = new List<MouseMessage>();
            public IntPtr YikeRenderWidgetHandle { get; set; }
            public PixelRect YikeRenderWidgetBounds { get; set; }
            public Dictionary<IntPtr, IntPtr> YikeRenderWidgetHandles { get; } = new Dictionary<IntPtr, IntPtr>();
            public Dictionary<IntPtr, PixelRect> WindowBounds { get; } = new Dictionary<IntPtr, PixelRect>();
            public string LastRequestedChildClassName { get; private set; }

            public IntPtr FindWindowByClass(string className)
            {
                return IntPtr.Zero;
            }

            public IntPtr FindChildWindowByClass(IntPtr parentHandle, string className)
            {
                LastRequestedChildClassName = className;
                if (YikeRenderWidgetHandles.TryGetValue(parentHandle, out IntPtr childHandle))
                    return childHandle;
                return YikeRenderWidgetHandle;
            }

            public bool TryGetWindowBounds(IntPtr handle, out PixelRect bounds)
            {
                bounds = null;
                if (WindowBounds.TryGetValue(handle, out PixelRect mappedBounds))
                {
                    bounds = mappedBounds;
                    return true;
                }
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

            public bool TrySendMouseMessage(IntPtr handle, uint message, int wParam, int lParam)
            {
                SentMessages.Add(new MouseMessage(handle, message, wParam, lParam));
                return true;
            }
        }

        private readonly record struct MouseMessage(IntPtr Handle, uint Message, int WParam, int LParam);
    }
}
