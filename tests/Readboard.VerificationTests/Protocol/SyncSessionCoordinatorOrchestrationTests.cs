using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class SyncSessionCoordinatorOrchestrationTests
    {
        [Fact]
        public void TryStartKeepSync_OwnsInitialProbeAndLegacyStartFlow()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());

            Invoke(coordinator, "AttachRuntime", runtime);

            bool started = (bool)Invoke(coordinator, "TryStartKeepSync");
            Assert.True(started);
            Assert.True(hostRecorder.KeepStarted.Wait(TimeSpan.FromSeconds(1)));
            Assert.True(transport.WaitForLines(5, TimeSpan.FromSeconds(1)));

            Invoke(coordinator, "StopSyncSession");

            Assert.True(hostRecorder.KeepStopped.Wait(TimeSpan.FromSeconds(1)));
            Assert.Equal(
                new[]
                {
                    "notForeFoxWithInBoard",
                    "sync",
                    "start 19 19",
                    "re=foreground",
                    "end"
                },
                transport.SentLines.GetRange(0, 5));
            Assert.True(hostRecorder.SnapshotRequests >= 2);
        }

        [Fact]
        public void TryStartContinuousSync_UsesWindowLocatorAndCoreDescriptorFactory()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type locatorInterfaceType = RequireType(assembly, "readboard.ISyncWindowLocator");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Fox, IntPtr.Zero);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            WindowLocatorRecorder locatorRecorder = new WindowLocatorRecorder(new IntPtr(4242));
            object locator = CreateProxy(locatorInterfaceType, locatorRecorder.HandleCall);
            DescriptorFactoryRecorder descriptorRecorder = new DescriptorFactoryRecorder();
            object descriptorFactory = CreateProxy(descriptorInterfaceType, descriptorRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=fox")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowLocator", locator);
            SetProperty(runtime, "WindowDescriptorFactory", descriptorFactory);

            Invoke(coordinator, "AttachRuntime", runtime);

            bool started = (bool)Invoke(coordinator, "TryStartContinuousSync");
            Assert.True(started);
            Assert.True(hostRecorder.ContinuousStarted.Wait(TimeSpan.FromSeconds(1)));
            Assert.True(transport.WaitForLines(5, TimeSpan.FromSeconds(1)));

            Invoke(coordinator, "StopSyncSession");

            Assert.True(hostRecorder.ContinuousStopped.Wait(TimeSpan.FromSeconds(1)));
            Assert.True(locatorRecorder.Calls > 0);
            Assert.Equal(new IntPtr(4242), hostRecorder.LastSelectedWindow);
            Assert.Equal(
                new[]
                {
                    "notForeFoxWithInBoard",
                    "sync",
                    "start 19 19 4242",
                    "re=fox",
                    "end"
                },
                transport.SentLines.GetRange(0, 5));
        }

        [Fact]
        public void CreateCaptureRequest_UsesEnhancedCaptureFlagFromHostSnapshot()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Background, new IntPtr(5151));
            SetProperty(snapshot, "UseEnhancedCapture", true);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            DescriptorFactoryRecorder descriptorFactoryRecorder = new DescriptorFactoryRecorder();
            object descriptorFactory = CreateProxy(descriptorInterfaceType, descriptorFactoryRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=background")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowDescriptorFactory", descriptorFactory);

            Invoke(coordinator, "AttachRuntime", runtime);

            BoardCaptureRequest request = (BoardCaptureRequest)Invoke(coordinator, "CreateCaptureRequest", runtime, snapshot);

            Assert.NotNull(request);
            Assert.True(request.UseEnhancedCapture);
        }

        private static object CreateProxy(Type interfaceType, Func<MethodInfo, object[], object> handler)
        {
            MethodInfo createMethod = null;
            MethodInfo[] methods = typeof(DispatchProxy).GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (candidate.Name != "Create" || !candidate.IsGenericMethodDefinition)
                    continue;
                if (candidate.GetParameters().Length != 0)
                    continue;
                createMethod = candidate.MakeGenericMethod(interfaceType, typeof(ReflectionProxy));
                break;
            }
            Assert.True(createMethod != null, "DispatchProxy.Create<T,TProxy>() is required for orchestration tests.");
            object proxy = createMethod.Invoke(null, null);
            ((ReflectionProxy)proxy).Handler = handler;
            return proxy;
        }

        private static object CreateSnapshot(Type snapshotType, SyncMode syncMode, IntPtr handle)
        {
            object snapshot = Activator.CreateInstance(snapshotType);
            SetProperty(snapshot, "SyncMode", syncMode);
            SetProperty(snapshot, "BoardWidth", 19);
            SetProperty(snapshot, "BoardHeight", 19);
            SetProperty(snapshot, "SelectionBounds", new PixelRect(10, 20, 190, 190));
            SetProperty(snapshot, "SelectedWindowHandle", handle);
            SetProperty(snapshot, "DpiScale", 1f);
            SetProperty(snapshot, "LegacyTypeToken", "0");
            SetProperty(snapshot, "ShowInBoard", false);
            SetProperty(snapshot, "SupportsForegroundFoxInBoardProtocol", false);
            SetProperty(snapshot, "CanUseLightweightInterop", false);
            SetProperty(snapshot, "AutoMinimize", false);
            SetProperty(snapshot, "SampleIntervalMs", 5);
            return snapshot;
        }

        private static BoardFrame CreateFrame()
        {
            return new BoardFrame
            {
                SyncMode = SyncMode.Foreground,
                BoardSize = new BoardDimensions(19, 19),
                Viewport = new BoardViewport
                {
                    SourceBounds = new PixelRect(0, 0, 190, 190),
                    ScreenBounds = new PixelRect(0, 0, 190, 190),
                    CellWidth = 10d,
                    CellHeight = 10d
                }
            };
        }

        private static BoardRecognitionResult CreateResult(string protocolLine)
        {
            return new BoardRecognitionResult
            {
                Success = true,
                Viewport = new BoardViewport
                {
                    SourceBounds = new PixelRect(0, 0, 190, 190),
                    ScreenBounds = new PixelRect(0, 0, 190, 190),
                    CellWidth = 10d,
                    CellHeight = 10d
                },
                Snapshot = new BoardSnapshot
                {
                    Width = 19,
                    Height = 19,
                    IsValid = true,
                    Payload = protocolLine,
                    ProtocolLines = new[] { protocolLine }
                }
            };
        }

        private static Type RequireType(Assembly assembly, string typeName)
        {
            Type resolved = assembly.GetType(typeName);
            Assert.True(resolved != null, "Missing runtime contract type: " + typeName);
            return resolved;
        }

        private static object Invoke(object target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.True(method != null, "Missing coordinator method: " + methodName);
            return method.Invoke(target, args);
        }

        private static void SetProperty(object target, string propertyName, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.True(property != null, "Missing property: " + propertyName);
            property.SetValue(target, value, null);
        }

        private class ReflectionProxy : DispatchProxy
        {
            public Func<MethodInfo, object[], object> Handler { get; set; }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                return Handler(targetMethod, args);
            }
        }

        private sealed class HostRecorder
        {
            private readonly object snapshot;

            public HostRecorder(object snapshot)
            {
                this.snapshot = snapshot;
            }

            public ManualResetEventSlim KeepStarted { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim KeepStopped { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim ContinuousStarted { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim ContinuousStopped { get; } = new ManualResetEventSlim(false);
            public int SnapshotRequests { get; private set; }
            public IntPtr LastSelectedWindow { get; private set; }

            public object HandleCall(MethodInfo method, object[] args)
            {
                switch (method.Name)
                {
                    case "CaptureSnapshot":
                        SnapshotRequests++;
                        return snapshot;
                    case "UpdateSelectedWindowHandle":
                        LastSelectedWindow = (IntPtr)args[0];
                        SetProperty(snapshot, "SelectedWindowHandle", LastSelectedWindow);
                        return null;
                    case "OnKeepSyncStarted":
                        KeepStarted.Set();
                        return null;
                    case "OnKeepSyncStopped":
                        KeepStopped.Set();
                        return null;
                    case "OnContinuousSyncStarted":
                        ContinuousStarted.Set();
                        return null;
                    case "OnContinuousSyncStopped":
                        ContinuousStopped.Set();
                        return null;
                    case "ShowMissingSyncSourceMessage":
                    case "ShowRecognitionFailureMessage":
                    case "MinimizeWindow":
                    case "ReleasePlacementBinding":
                        return null;
                    default:
                        return GetDefault(method.ReturnType);
                }
            }
        }

        private sealed class WindowLocatorRecorder
        {
            private readonly IntPtr handle;

            public WindowLocatorRecorder(IntPtr handle)
            {
                this.handle = handle;
            }

            public int Calls { get; private set; }

            public object HandleCall(MethodInfo method, object[] args)
            {
                Calls++;
                return handle;
            }
        }

        private sealed class DescriptorFactoryRecorder
        {
            public object HandleCall(MethodInfo method, object[] args)
            {
                args[2] = new WindowDescriptor
                {
                    Handle = (IntPtr)args[0],
                    Bounds = new PixelRect(100, 200, 190, 190),
                    ClassName = "FoxBoard",
                    Title = "Fox",
                    IsDpiAware = true,
                    DpiScale = (float)args[1]
                };
                return true;
            }
        }

        private sealed class RecordingTransport : IReadBoardTransport
        {
            private readonly ManualResetEventSlim lineEvent = new ManualResetEventSlim(false);

            public event EventHandler<string> MessageReceived;

            public List<string> SentLines { get; } = new List<string>();

            public bool IsConnected { get; private set; }

            public void Dispose()
            {
            }

            public void Emit(string rawLine)
            {
                MessageReceived?.Invoke(this, rawLine);
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

            public bool WaitForLines(int expectedCount, TimeSpan timeout)
            {
                DateTime deadline = DateTime.UtcNow.Add(timeout);
                while (DateTime.UtcNow < deadline)
                {
                    lock (SentLines)
                    {
                        if (SentLines.Count >= expectedCount)
                            return true;
                    }

                    lineEvent.Wait(TimeSpan.FromMilliseconds(25));
                    lineEvent.Reset();
                }

                return false;
            }
        }

        private sealed class SequencedCaptureService : IBoardCaptureService
        {
            private readonly BoardFrame frame;

            public SequencedCaptureService(BoardFrame frame)
            {
                this.frame = frame;
            }

            public BoardCaptureResult Capture(BoardCaptureRequest request)
            {
                return new BoardCaptureResult
                {
                    Success = true,
                    Frame = frame
                };
            }
        }

        private sealed class SequencedRecognitionService : IBoardRecognitionService
        {
            private readonly BoardRecognitionResult result;

            public SequencedRecognitionService(BoardRecognitionResult result)
            {
                this.result = result;
            }

            public BoardRecognitionResult Recognize(BoardRecognitionRequest request)
            {
                return result;
            }
        }

        private sealed class PassivePlacementService : IMovePlacementService
        {
            public MovePlacementResult Place(MovePlacementRequest request)
            {
                return new MovePlacementResult { Success = true };
            }
        }

        private sealed class PassiveOverlayService : IOverlayService
        {
            public OverlayUpdateResult BuildUpdate(OverlayUpdateRequest request)
            {
                return new OverlayUpdateResult();
            }

            public void Reset()
            {
            }
        }

        private static object GetDefault(Type returnType)
        {
            if (returnType == typeof(void))
                return null;
            if (!returnType.IsValueType)
                return null;
            return Activator.CreateInstance(returnType);
        }
    }
}
