using System;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class PlaceRequestExecutionResultTests
    {
        [Fact]
        public void HandlePlaceRequest_NullRequestReturnsNoResponse()
        {
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(new RecordingTransport(), new LegacyProtocolAdapter());
            Type resultType = ResolveResultType();
            MethodInfo method = ResolveHandlePlaceRequestMethod(resultType);

            object result = method.Invoke(coordinator, new object[] { null });

            Assert.NotNull(result);
            Assert.False(ReadBool(result, "ShouldSendResponse"));
        }

        [Fact]
        public void HandlePlaceRequest_WithoutActiveSyncReturnsNoResponse()
        {
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(new RecordingTransport(), new LegacyProtocolAdapter());
            Type resultType = ResolveResultType();
            MethodInfo method = ResolveHandlePlaceRequestMethod(resultType);
            AttachRuntime(coordinator, CreateSnapshot(19, 19));

            object result = method.Invoke(coordinator, new object[] { new MoveRequest { X = 1, Y = 1, VerifyMove = false } });

            Assert.NotNull(result);
            Assert.False(ReadBool(result, "ShouldSendResponse"));
        }

        [Fact]
        public async Task HandlePlaceRequest_WhenPendingMoveSucceedsReturnsResponseAndSuccess()
        {
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(new RecordingTransport(), new LegacyProtocolAdapter());
            Type resultType = ResolveResultType();
            MethodInfo method = ResolveHandlePlaceRequestMethod(resultType);
            AttachRuntime(coordinator, CreateSnapshot(19, 19));
            coordinator.BeginKeepSync();
            coordinator.SetSyncBoth(true);
            SetRuntimeBoardPixelWidth(coordinator, 19);

            Task<object> resultTask = Task.Run(() =>
                method.Invoke(coordinator, new object[]
                {
                    new MoveRequest { X = 1, Y = 1, VerifyMove = false }
                }));

            Assert.True(WaitForPendingMove(coordinator, out MoveRequest move));
            Assert.NotNull(move);
            Assert.Equal(1, move.X);
            Assert.Equal(1, move.Y);

            coordinator.HandlePendingMovePlacementResult(true);

            object result = await resultTask.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.NotNull(result);
            Assert.True(ReadBool(result, "ShouldSendResponse"));
            Assert.True(ReadBool(result, "Success"));
        }

        private static void AttachRuntime(SyncSessionCoordinator coordinator, object snapshot)
        {
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = assembly.GetType("readboard.SyncSessionRuntimeDependencies");
            Type hostType = assembly.GetType("readboard.ISyncCoordinatorHost");
            Type captureServiceType = assembly.GetType("readboard.IBoardCaptureService");
            Type recognitionServiceType = assembly.GetType("readboard.IBoardRecognitionService");
            Type placementServiceType = assembly.GetType("readboard.IMovePlacementService");
            Type overlayServiceType = assembly.GetType("readboard.IOverlayService");
            Assert.True(runtimeType != null, "Missing runtime type: readboard.SyncSessionRuntimeDependencies");
            Assert.True(hostType != null, "Missing host type: readboard.ISyncCoordinatorHost");
            Assert.True(captureServiceType != null, "Missing runtime type: readboard.IBoardCaptureService");
            Assert.True(recognitionServiceType != null, "Missing runtime type: readboard.IBoardRecognitionService");
            Assert.True(placementServiceType != null, "Missing runtime type: readboard.IMovePlacementService");
            Assert.True(overlayServiceType != null, "Missing runtime type: readboard.IOverlayService");

            object runtime = Activator.CreateInstance(runtimeType);
            object host = CreateProxy(hostType, delegate(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod.Name == "CaptureSnapshot")
                    return snapshot;
                return GetDefault(targetMethod.ReturnType);
            });

            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", CreateProxy(captureServiceType, DefaultProxyHandler));
            SetProperty(runtime, "RecognitionService", CreateProxy(recognitionServiceType, DefaultProxyHandler));
            SetProperty(runtime, "PlacementService", CreateProxy(placementServiceType, DefaultProxyHandler));
            SetProperty(runtime, "OverlayService", CreateProxy(overlayServiceType, DefaultProxyHandler));
            typeof(SyncSessionCoordinator)
                .GetMethod("AttachRuntime", BindingFlags.Instance | BindingFlags.Public)
                .Invoke(coordinator, new[] { runtime });
        }

        private static object CreateSnapshot(int boardWidth, int boardHeight)
        {
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type snapshotType = assembly.GetType("readboard.SyncCoordinatorHostSnapshot");
            Assert.True(snapshotType != null, "Missing snapshot type: readboard.SyncCoordinatorHostSnapshot");
            object snapshot = Activator.CreateInstance(snapshotType);
            SetProperty(snapshot, "BoardWidth", boardWidth);
            SetProperty(snapshot, "BoardHeight", boardHeight);
            return snapshot;
        }

        private static Type ResolveResultType()
        {
            Type resultType = typeof(SyncSessionCoordinator).Assembly.GetType("readboard.PlaceRequestExecutionResult");
            Assert.True(resultType != null, "Missing result type: readboard.PlaceRequestExecutionResult");
            return resultType;
        }

        private static MethodInfo ResolveHandlePlaceRequestMethod(Type resultType)
        {
            MethodInfo method = typeof(SyncSessionCoordinator).GetMethod("HandlePlaceRequest", BindingFlags.Instance | BindingFlags.Public);
            Assert.True(method != null, "Missing coordinator method: HandlePlaceRequest");
            Assert.Equal(resultType, method.ReturnType);
            return method;
        }

        private static bool WaitForPendingMove(SyncSessionCoordinator coordinator, out MoveRequest request)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(1);
            while (DateTime.UtcNow < deadline)
            {
                if (coordinator.TryTakePendingMove(out request))
                    return true;
                Task.Delay(10).Wait();
            }

            request = null;
            return false;
        }

        private static void SetRuntimeBoardPixelWidth(SyncSessionCoordinator coordinator, int boardPixelWidth)
        {
            FieldInfo runtimeStateField = typeof(SyncSessionCoordinator).GetField("runtimeState", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(runtimeStateField != null, "Missing coordinator field: runtimeState");
            object runtimeState = runtimeStateField.GetValue(coordinator);
            Assert.NotNull(runtimeState);
            SetProperty(runtimeState, "CurrentBoardPixelWidth", boardPixelWidth);
        }

        private static bool ReadBool(object instance, string propertyName)
        {
            PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.True(property != null, "Missing property: " + propertyName);
            return (bool)property.GetValue(instance, null);
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

            Assert.True(createMethod != null, "DispatchProxy.Create<T,TProxy>() is required.");
            object proxy = createMethod.Invoke(null, null);
            ((ReflectionProxy)proxy).Handler = handler;
            return proxy;
        }

        private static void SetProperty(object target, string propertyName, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.True(property != null, "Missing property: " + propertyName);
            property.SetValue(target, value, null);
        }

        private static object GetDefault(Type returnType)
        {
            if (returnType == typeof(void))
                return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }

        private static object DefaultProxyHandler(MethodInfo targetMethod, object[] args)
        {
            return GetDefault(targetMethod.ReturnType);
        }

        private sealed class RecordingTransport : IReadBoardTransport
        {
            public event EventHandler<string> MessageReceived;

            public bool IsConnected { get; private set; }

            public void Dispose()
            {
            }

            public void Send(string line)
            {
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
        }

        private class ReflectionProxy : DispatchProxy
        {
            public Func<MethodInfo, object[], object> Handler { get; set; }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                return Handler == null ? GetDefault(targetMethod.ReturnType) : Handler(targetMethod, args);
            }
        }
    }
}
