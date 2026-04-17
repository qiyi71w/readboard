using System;
using System.Collections.Generic;
using System.Reflection;

namespace readboard
{
    internal static class SyncSessionCoordinatorTddHarness
    {
        private sealed class RecordingTransport : IReadBoardTransport
        {
            public event EventHandler<string> MessageReceived;
            public readonly List<string> SentLines = new List<string>();

            public bool IsConnected
            {
                get { return true; }
            }

            public void Start()
            {
            }

            public void Stop()
            {
            }

            public void Send(string line)
            {
                SentLines.Add(line);
            }

            public void SendError(string message)
            {
                SentLines.Add("error " + message);
            }

            public void Dispose()
            {
            }

            public void RaiseMessage(string line)
            {
                EventHandler<string> handler = MessageReceived;
                if (handler != null)
                    handler(this, line);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void AssertSerialized(
            LegacyProtocolAdapter adapter,
            string methodName,
            string expected,
            params object[] args)
        {
            Type[] parameterTypes = GetParameterTypes(args);
            MethodInfo method = GetRequiredMethod(typeof(LegacyProtocolAdapter), methodName, parameterTypes);
            ProtocolMessage message = (ProtocolMessage)method.Invoke(adapter, args);
            Assert(adapter.Serialize(message) == expected, methodName + " should serialize to " + expected + ".");
        }

        private static void InvokeAndAssertSend(
            SyncSessionCoordinator coordinator,
            RecordingTransport transport,
            string methodName,
            string expected,
            params object[] args)
        {
            int beforeCount = transport.SentLines.Count;
            Type[] parameterTypes = GetParameterTypes(args);
            MethodInfo method = GetRequiredMethod(typeof(SyncSessionCoordinator), methodName, parameterTypes);
            method.Invoke(coordinator, args);
            Assert(transport.SentLines.Count == beforeCount + 1, methodName + " should emit exactly one line.");
            Assert(transport.SentLines[beforeCount] == expected, methodName + " should emit " + expected + ".");
        }

        private static MethodInfo GetRequiredMethod(Type type, string methodName, params Type[] parameterTypes)
        {
            MethodInfo method = type.GetMethod(methodName, parameterTypes);
            Assert(method != null, type.Name + " should expose " + methodName + ".");
            return method;
        }

        private static Type[] GetParameterTypes(object[] args)
        {
            Type[] parameterTypes = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                parameterTypes[i] = args[i].GetType();
            return parameterTypes;
        }

        public static int Main()
        {
            LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();
            AssertSerialized(adapter, "CreateSyncMessage", "sync");
            AssertSerialized(adapter, "CreateStopSyncMessage", "stopsync");
            AssertSerialized(adapter, "CreateEndSyncMessage", "endsync");
            AssertSerialized(adapter, "CreateBothSyncMessage", "bothSync", true);
            AssertSerialized(adapter, "CreateBothSyncMessage", "nobothSync", false);
            AssertSerialized(adapter, "CreatePlacementResultMessage", "placeComplete", true);
            AssertSerialized(adapter, "CreatePlacementResultMessage", "error place failed", false);
            AssertSerialized(adapter, "CreateNoInBoardMessage", "noinboard");
            AssertSerialized(adapter, "CreateNotInBoardMessage", "notinboard");
            AssertSerialized(adapter, "CreateForegroundFoxInBoardMessage", "foreFoxWithInBoard", true);
            AssertSerialized(adapter, "CreateForegroundFoxInBoardMessage", "notForeFoxWithInBoard", false);
            AssertSerialized(adapter, "CreateStartMessage", "start 19 19", 19, 19, IntPtr.Zero, false);
            AssertSerialized(adapter, "CreateStartMessage", "start 19 19 123", 19, 19, new IntPtr(123), true);
            AssertSerialized(adapter, "CreatePlayMessage", "play>black>10 20 30", "black", "10", "20", "30");
            AssertSerialized(adapter, "CreateTimeChangedMessage", "timechanged 10", "10");
            AssertSerialized(adapter, "CreatePlayoutsChangedMessage", "playoutschanged 20", "20");
            AssertSerialized(adapter, "CreateFirstPolicyChangedMessage", "firstchanged 30", "30");
            AssertSerialized(adapter, "CreateNoPonderMessage", "noponder");
            AssertSerialized(adapter, "CreateStopAutoPlayMessage", "stopAutoPlay");
            AssertSerialized(adapter, "CreatePassMessage", "pass");

            RecordingTransport outboundTransport = new RecordingTransport();
            SyncSessionCoordinator outboundCoordinator = new SyncSessionCoordinator(outboundTransport, adapter);
            outboundCoordinator.BindSessionState(new SessionState());
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendSync", "sync");
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendStopSync", "stopsync");
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendEndSync", "endsync");
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendBothSync", "bothSync", true);
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendBothSync", "nobothSync", false);
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendPlacementResult", "placeComplete", true);
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendPlacementResult", "error place failed", false);
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendNoInBoard", "noinboard");
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendNotInBoard", "notinboard");
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendForegroundFoxInBoard", "foreFoxWithInBoard", true);
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendForegroundFoxInBoard", "notForeFoxWithInBoard", false);
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendStart", "start 19 19", 19, 19, IntPtr.Zero, false);
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendStart", "start 19 19 123", 19, 19, new IntPtr(123), true);
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendPlay", "play>white>10 20 30", "white", "10", "20", "30");
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendTimeChanged", "timechanged 10", "10");
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendPlayoutsChanged", "playoutschanged 20", "20");
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendFirstPolicyChanged", "firstchanged 30", "30");
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendNoPonder", "noponder");
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendStopAutoPlay", "stopAutoPlay");
            InvokeAndAssertSend(outboundCoordinator, outboundTransport, "SendPass", "pass");

            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, adapter);
            SessionState state = new SessionState();
            coordinator.BindSessionState(state);

            coordinator.SetSyncBoth(true);
            Assert(coordinator.SyncBoth, "SyncBoth should be tracked by coordinator.");
            Assert(state.SyncBoth, "SessionState.SyncBoth should mirror coordinator state.");

            coordinator.BeginContinuousSync();
            coordinator.BeginKeepSync();
            Assert(coordinator.IsContinuousSyncing, "Continuous sync should be active.");
            Assert(coordinator.StartedSync && coordinator.KeepSync, "Keep-sync lifecycle should be owned by coordinator.");
            Assert(state.IsContinuousSyncing && state.StartedSync && state.KeepSync, "SessionState should reflect lifecycle state.");
            Assert(!coordinator.WaitForSyncIdle(0), "Idle wait should report busy while sync is active.");

            MoveRequest directMove = new MoveRequest { X = 3, Y = 4, VerifyMove = false };
            Assert(coordinator.TryQueuePendingMove(directMove, 19, 19), "Pending move should queue while keep-sync and both-sync are enabled.");

            MoveRequest dispatchedMove;
            Assert(coordinator.TryTakePendingMove(out dispatchedMove), "Queued pending move should be dispatchable.");
            Assert(dispatchedMove.X == 3 && dispatchedMove.Y == 4, "Dispatched move should preserve coordinates.");
            coordinator.HandlePendingMovePlacementResult(true);
            Assert(coordinator.WaitForPendingMoveResult(), "Non-verify pending move should complete after a successful placement.");

            MoveRequest verifyMove = new MoveRequest { X = 2, Y = 1, VerifyMove = true };
            Assert(coordinator.TryQueuePendingMove(verifyMove, 19, 19), "Verify move should also queue.");
            Assert(coordinator.TryTakePendingMove(out dispatchedMove), "Verify move should be dispatchable.");
            coordinator.HandlePendingMovePlacementResult(true);
            coordinator.ResolvePendingMove(new BoardSnapshot
            {
                Width = 19,
                Height = 19,
                IsValid = true,
                BoardState = CreateBoardState(19, 19, 2, 1),
                Payload = "payload-verify",
                ProtocolLines = new List<string> { "re=verify" }
            }, 19);
            Assert(coordinator.WaitForPendingMoveResult(), "Verified pending move should complete after recognition confirms the stone.");

            BoardSnapshot snapshot = new BoardSnapshot
            {
                Width = 19,
                Height = 19,
                IsValid = true,
                Payload = "payload-1",
                ProtocolLines = new List<string> { "re=1", "re=2" }
            };

            coordinator.SendOverlayLine("inboard 1 2 3 4 5");
            coordinator.SendOverlayLine("inboard 1 2 3 4 5");
            coordinator.SendBoardSnapshot(snapshot);
            coordinator.SendBoardSnapshot(snapshot);
            Assert(transport.SentLines.Count == 4, "Overlay and payload should each send only once before reset.");

            coordinator.SendClear();
            coordinator.SendOverlayLine("inboard 1 2 3 4 5");
            coordinator.SendBoardSnapshot(snapshot);
            Assert(transport.SentLines.Count == 9, "Clear should reset overlay/payload dedup state.");
            Assert(transport.SentLines[4] == "clear", "Clear should still emit legacy clear command.");

            coordinator.EndKeepSync();
            coordinator.EndContinuousSync();
            Assert(coordinator.WaitForSyncIdle(0), "Idle wait should report idle after sync stops.");
            Assert(!coordinator.StartedSync && !coordinator.KeepSync && !coordinator.IsContinuousSyncing, "Stop should clear lifecycle state.");
            Assert(state.PendingMove == null || !state.PendingMove.Active, "Stopping sync should cancel pending move state.");

            Console.WriteLine("GREEN");
            return 0;
        }

        private static BoardCellState[] CreateBoardState(int width, int height, int x, int y)
        {
            BoardCellState[] boardState = new BoardCellState[width * height];
            boardState[(y * width) + x] = BoardCellState.Black;
            return boardState;
        }
    }
}
