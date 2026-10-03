using System;
using System.Collections.Generic;
using readboard;

namespace Readboard.VerificationTests.Host
{
    internal class RuntimeTestEnvironment : IControlCenterEnvironment
    {
        public bool HasActiveSyncOperation { get; set; }
        public bool? TargetWindowValid { get; set; }
        public bool ShowInBoardHint { get; set; }
        public DateTime UtcNow { get; set; } = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        public IntPtr Handle { get; set; } = new IntPtr(42);
        public FoxWindowContext Context { get; set; } = new FoxWindowContext
        {
            Kind = FoxWindowKind.LiveRoom,
            LiveRoomState = FoxLiveRoomState.Playing,
            RoomToken = "room-1"
        };
        public bool BindingInvalidated { get; set; }
        public FoxMatchBarReading Players { get; set; } = FoxMatchBarReading.Empty;
        public int PlayerReads { get; private set; }
        public int WindowReads { get; private set; }
        public int HintCount { get; private set; }
        public Action OnProject { get; set; }
        public Func<IntPtr, FoxWindowContext, FoxMatchBarReading> OnReadPlayers { get; set; }
        public Action OnHint { get; set; }

        public ControlCenterWindowFacts ReadWindow()
        {
            WindowReads++;
            bool invalidated = BindingInvalidated;
            BindingInvalidated = false;
            return new ControlCenterWindowFacts
            {
                Handle = Handle,
                Context = Context,
                BindingInvalidated = invalidated
            };
        }

        public FoxMatchBarReading ReadPlayers(IntPtr handle, FoxWindowContext context)
        {
            PlayerReads++;
            return OnReadPlayers == null ? Players : OnReadPlayers(handle, context);
        }

        public FoxMatchBarReading DiscoverIdentityCandidates(IntPtr handle)
        {
            return Players;
        }

        public void ProjectState(ControlCenterPreferences preferences, ControlCenterSessionState sessionState, bool platformChanged)
        {
            OnProject?.Invoke();
        }

        public void ShowOnBoardHint()
        {
            HintCount++;
            OnHint?.Invoke();
        }
    }

    internal sealed class RuntimeIdentityPersistence : IFoxIdentityPersistence
    {
        public string Saved { get; set; } = string.Empty;
        public Exception SaveFailure { get; set; }
        public Exception ClearFailure { get; set; }
        public string LoadSavedIdentitySignature() { return Saved; }
        public void SaveIdentitySignature(string signature)
        {
            if (SaveFailure != null) throw SaveFailure;
            Saved = signature;
        }
        public void ClearSavedIdentity()
        {
            if (ClearFailure != null) throw ClearFailure;
            Saved = string.Empty;
        }
    }

    internal sealed class RuntimeRecordingTransport : IReadBoardTransport
    {
        public event EventHandler<string> MessageReceived { add { } remove { } }
        public List<string> Lines { get; } = new List<string>();
        public Action<string> OnSend { get; set; }
        public bool IsConnected { get; private set; }
        public void Send(string line) { Lines.Add(line); OnSend?.Invoke(line); }
        public void Start() { IsConnected = true; }
        public void Stop() { IsConnected = false; }
        public void Dispose() { }
        public void SendError(string message) { }
    }

    internal static class RuntimeTestFactory
    {
        public static ControlCenterRuntime Create(
            ControlCenterPreferences preferences,
            IControlCenterEnvironment environment,
            IControlCenterPreferencePersistence persistence,
            IControlCenterActionAdapter actions)
        {
            return Create(preferences, new ControlCenterSessionState(), environment, persistence, actions);
        }

        public static ControlCenterRuntime Create(
            ControlCenterPreferences preferences,
            ControlCenterSessionState session,
            IControlCenterEnvironment environment,
            IControlCenterPreferencePersistence persistence,
            IControlCenterActionAdapter actions)
        {
            var coordinator = new SyncSessionCoordinator(new RuntimeRecordingTransport(), new LegacyProtocolAdapter());
            var runtime = new ControlCenterRuntime(preferences, session, environment, persistence,
                actions, coordinator, new FoxIdentitySelection(new RuntimeIdentityPersistence()));
            runtime.ProjectCurrentState();
            runtime.CompleteInitialization();
            return runtime;
        }
    }
}
