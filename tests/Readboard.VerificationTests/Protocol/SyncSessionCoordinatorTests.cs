using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;
using readboard;

namespace Readboard.VerificationTests
{
    public sealed class SyncSessionCoordinatorTests
    {
        [Fact]
        public void NotifyReady_SendsReadyAndPlayPonderLinesInOrder()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.NotifyReady(true);

            Assert.Equal(new[] { "ready", "playponder on" }, transport.SentLines);
        }

        [Fact]
        public void StartAndStop_ManageDispatchLifecycleForInboundPlaceMessages()
        {
            FakeTransport transport = new FakeTransport();
            CapturingHost host = new CapturingHost();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.AttachHost(host);

            coordinator.Start();
            transport.Emit("place 3 4");
            coordinator.Stop();
            transport.Emit("place 9 9");

            Assert.Equal(1, transport.StartCount);
            Assert.Equal(1, transport.StopCount);
            Assert.Equal(1, host.DispatchCount);
            Assert.NotNull(host.LastMoveRequest);
            Assert.Equal(3, host.LastMoveRequest.X);
            Assert.Equal(4, host.LastMoveRequest.Y);
        }

        [Fact]
        public void SendError_ForwardsToTransport()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.SendError("boom");

            Assert.Equal(new[] { "boom" }, transport.ErrorMessages);
        }

        [Fact]
        public void runtime_state_carries_yike_context()
        {
            SyncSessionRuntimeState state = new SyncSessionRuntimeState();

            state.LastCapturedYikeContext = new YikeWindowContext { RoomToken = "x", MoveNumber = 1 };

            Assert.Equal("x", state.LastCapturedYikeContext.RoomToken);
            Assert.Null(state.LastSentYikeContextSignature);
        }

        [Fact]
        public void SendBoardSnapshot_DeduplicatesWhenPayloadAndFoxMoveNumberStayTheSame()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            BoardSnapshot firstSnapshot = new BoardSnapshot
            {
                Payload = "payload-1",
                FoxMoveNumber = 57,
                ProtocolLines = new[] { "re=000", "re=111" }
            };
            BoardSnapshot secondSnapshot = new BoardSnapshot
            {
                Payload = "payload-1",
                FoxMoveNumber = 57,
                ProtocolLines = new[] { "re=000", "re=111" }
            };

            coordinator.SendBoardSnapshot(firstSnapshot);
            coordinator.SendBoardSnapshot(secondSnapshot);

            Assert.Equal(new[] { "syncPlatform generic", "foxMoveNumber 57", "re=000", "re=111", "end" }, transport.SentLines);
        }

        [Fact]
        public void SendBoardSnapshot_EmitsFoxLiveContextMetadataBeforeBoardPayload()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.SetSyncPlatform("fox");
            coordinator.SetFoxWindowContext(new FoxWindowContext
            {
                Kind = FoxWindowKind.LiveRoom,
                RoomToken = "43581号",
                LiveTitleMove = 89
            });
            coordinator.SendBoardSnapshot(CreateSnapshot("payload-1", 57));

            Assert.Equal(
                new[]
                {
                    "syncPlatform fox",
                    "roomToken 43581号",
                    "liveTitleMove 89",
                    "foxMoveNumber 57",
                    "re=000",
                    "re=111",
                    "end"
                },
                transport.SentLines);
        }

        [Fact]
        public void SendBoardSnapshot_EmitsFoxRecordContextMetadataBeforeBoardPayload()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.SetSyncPlatform("fox");
            coordinator.SetFoxWindowContext(new FoxWindowContext
            {
                Kind = FoxWindowKind.RecordView,
                RecordCurrentMove = 333,
                RecordTotalMove = 333,
                RecordAtEnd = true,
                TitleFingerprint = "record-fingerprint"
            });
            coordinator.SendBoardSnapshot(CreateSnapshot("payload-1", 333));

            Assert.Equal(
                new[]
                {
                    "syncPlatform fox",
                    "recordCurrentMove 333",
                    "recordTotalMove 333",
                    "recordAtEnd 1",
                    "recordTitleFingerprint record-fingerprint",
                    "foxMoveNumber 333",
                    "re=000",
                    "re=111",
                    "end"
                },
                transport.SentLines);
        }

        [Fact]
        public void SendBoardSnapshot_ResendsFullFrameWhenFoxMoveNumberChangesWithoutPayloadChange()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            BoardSnapshot firstSnapshot = CreateSnapshot("payload-1", 57);
            BoardSnapshot secondSnapshot = CreateSnapshot("payload-1", 58);

            coordinator.SendBoardSnapshot(firstSnapshot);
            coordinator.SendBoardSnapshot(secondSnapshot);

            Assert.Equal(
                new[]
                {
                    "syncPlatform generic", "foxMoveNumber 57", "re=000", "re=111", "end",
                    "syncPlatform generic", "foxMoveNumber 58", "re=000", "re=111", "end"
                },
                transport.SentLines);
        }

        [Fact]
        public void SendBoardSnapshot_ResendsFullFrameWhenContextChangesWithoutPayloadChange()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.SetSyncPlatform("fox");
            coordinator.SetFoxWindowContext(new FoxWindowContext
            {
                Kind = FoxWindowKind.LiveRoom,
                RoomToken = "43581号",
                LiveTitleMove = 89
            });
            coordinator.SendBoardSnapshot(CreateSnapshot("payload-1", 57));

            coordinator.SetFoxWindowContext(new FoxWindowContext
            {
                Kind = FoxWindowKind.LiveRoom,
                RoomToken = "43582号",
                LiveTitleMove = 89
            });
            coordinator.SendBoardSnapshot(CreateSnapshot("payload-1", 57));

            Assert.Equal(
                new[]
                {
                    "syncPlatform fox",
                    "roomToken 43581号",
                    "liveTitleMove 89",
                    "foxMoveNumber 57",
                    "re=000",
                    "re=111",
                    "end",
                    "syncPlatform fox",
                    "roomToken 43582号",
                    "liveTitleMove 89",
                    "foxMoveNumber 57",
                    "re=000",
                    "re=111",
                    "end"
                },
                transport.SentLines);
        }

        [Fact]
        public void SendBoardSnapshot_UsesCapturedFoxMoveNumberWhenSnapshotDoesNotProvideOne()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            BoardSnapshot snapshot = CreateSnapshot("payload-1", null);

            coordinator.SetCapturedFoxMoveNumber(57);
            coordinator.SendBoardSnapshot(snapshot);

            Assert.Equal(new[] { "syncPlatform generic", "foxMoveNumber 57", "re=000", "re=111", "end" }, transport.SentLines);
        }

        [Fact]
        public void SendBoardSnapshot_DeduplicatesWhenPayloadAndCapturedFoxMoveNumberFallbackStayTheSame()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            BoardSnapshot firstSnapshot = CreateSnapshot("payload-1", null);
            BoardSnapshot secondSnapshot = CreateSnapshot("payload-1", null);

            coordinator.SetCapturedFoxMoveNumber(57);
            coordinator.SendBoardSnapshot(firstSnapshot);
            coordinator.SetCapturedFoxMoveNumber(57);
            coordinator.SendBoardSnapshot(secondSnapshot);

            Assert.Equal(new[] { "syncPlatform generic", "foxMoveNumber 57", "re=000", "re=111", "end" }, transport.SentLines);
        }

        [Fact]
        public void SendBoardSnapshot_ResendsFullFrameWhenCapturedFoxMoveNumberFallbackChangesWithoutPayloadChange()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            BoardSnapshot firstSnapshot = CreateSnapshot("payload-1", null);
            BoardSnapshot secondSnapshot = CreateSnapshot("payload-1", null);

            coordinator.SetCapturedFoxMoveNumber(57);
            coordinator.SendBoardSnapshot(firstSnapshot);
            coordinator.SetCapturedFoxMoveNumber(58);
            coordinator.SendBoardSnapshot(secondSnapshot);

            Assert.Equal(
                new[]
                {
                    "syncPlatform generic", "foxMoveNumber 57", "re=000", "re=111", "end",
                    "syncPlatform generic", "foxMoveNumber 58", "re=000", "re=111", "end"
                },
                transport.SentLines);
        }

        [Fact]
        public void SendBoardSnapshot_ResendsFullFrameWhenCapturedFoxMoveNumberFallbackChangesForTheSameSnapshotInstance()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            BoardSnapshot snapshot = CreateSnapshot("payload-1", null);

            coordinator.SetCapturedFoxMoveNumber(57);
            coordinator.SendBoardSnapshot(snapshot);
            coordinator.SetCapturedFoxMoveNumber(58);
            coordinator.SendBoardSnapshot(snapshot);

            Assert.Null(snapshot.FoxMoveNumber);
            Assert.Equal(
                new[]
                {
                    "syncPlatform generic", "foxMoveNumber 57", "re=000", "re=111", "end",
                    "syncPlatform generic", "foxMoveNumber 58", "re=000", "re=111", "end"
                },
                transport.SentLines);
        }

        [Fact]
        public void SendBoardSnapshot_ConsumesForceRebuildFlagAfterOneFrame()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.ArmForceRebuild();
            coordinator.SendBoardSnapshot(CreateSnapshot("payload-1", 57));
            coordinator.SendBoardSnapshot(CreateSnapshot("payload-1", 57));

            Assert.Single(transport.SentLines.FindAll(line => line == "forceRebuild"));
            Assert.Equal(
                new[]
                {
                    "syncPlatform generic",
                    "forceRebuild",
                    "foxMoveNumber 57",
                    "re=000",
                    "re=111",
                    "end"
                },
                transport.SentLines);
        }

        [Fact]
        public void SendBoardSnapshot_ResendsFullFrameWhenYikeRoomChangesWithoutPayloadChange()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.SetSyncPlatform("yike");
            coordinator.SetYikeContext(new YikeWindowContext { RoomToken = "65191829", MoveNumber = 16 });
            coordinator.SendBoardSnapshot(CreateSnapshot("payload-1", null));
            coordinator.SetYikeContext(new YikeWindowContext { RoomToken = "65191830", MoveNumber = 16 });
            coordinator.SendBoardSnapshot(CreateSnapshot("payload-1", null));

            Assert.Equal(
                new[]
                {
                    "syncPlatform yike",
                    "yikeRoomToken 65191829",
                    "yikeMoveNumber 16",
                    "re=000",
                    "re=111",
                    "end",
                    "syncPlatform yike",
                    "yikeRoomToken 65191830",
                    "yikeMoveNumber 16",
                    "re=000",
                    "re=111",
                    "end"
                },
                transport.SentLines);
        }

        [Fact]
        public void SendBoardSnapshot_ResendsFullFrameWhenYikeMoveChangesWithoutPayloadChange()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.SetSyncPlatform("yike");
            coordinator.SetYikeContext(new YikeWindowContext { RoomToken = "65191829", MoveNumber = 16 });
            coordinator.SendBoardSnapshot(CreateSnapshot("payload-1", null));
            coordinator.SetYikeContext(new YikeWindowContext { RoomToken = "65191829", MoveNumber = 17 });
            coordinator.SendBoardSnapshot(CreateSnapshot("payload-1", null));

            Assert.Equal(
                new[]
                {
                    "syncPlatform yike",
                    "yikeRoomToken 65191829",
                    "yikeMoveNumber 16",
                    "re=000",
                    "re=111",
                    "end",
                    "syncPlatform yike",
                    "yikeRoomToken 65191829",
                    "yikeMoveNumber 17",
                    "re=000",
                    "re=111",
                    "end"
                },
                transport.SentLines);
        }

        [Fact]
        public void SendBoardSnapshot_EmitsYikeRoomTokenAndMoveNumberLinesWhenPresent()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.SetSyncPlatform("yike");
            coordinator.SetYikeContext(new YikeWindowContext { RoomToken = "65191829", MoveNumber = 16 });
            coordinator.SendBoardSnapshot(CreateSnapshot("payload-1", null));

            Assert.Equal(
                new[]
                {
                    "syncPlatform yike",
                    "yikeRoomToken 65191829",
                    "yikeMoveNumber 16",
                    "re=000",
                    "re=111",
                    "end"
                },
                transport.SentLines);
        }

        [Fact]
        public void SendBoardSnapshot_OmitsYikeLinesInNonYikeMode()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.SetSyncPlatform("fox");
            coordinator.SetYikeContext(new YikeWindowContext { RoomToken = "65191829", MoveNumber = 16 });
            coordinator.SendBoardSnapshot(CreateSnapshot("payload-1", null));

            Assert.DoesNotContain("yikeRoomToken 65191829", transport.SentLines);
            Assert.DoesNotContain("yikeMoveNumber 16", transport.SentLines);
        }

        [Fact]
        public void ResetSyncCaches_ClearsYikeContextState()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.SetSyncPlatform("yike");
            coordinator.SetYikeContext(new YikeWindowContext { RoomToken = "65191829", MoveNumber = 16 });
            coordinator.SendBoardSnapshot(CreateSnapshot("payload-1", null));

            coordinator.ResetSyncCaches();

            SyncSessionRuntimeState runtimeState = GetRuntimeState(coordinator);
            Assert.Null(runtimeState.LastCapturedYikeContext);
            Assert.Null(runtimeState.LastSentYikeContextSignature);
        }

        private static BoardSnapshot CreateSnapshot(string payload, int? foxMoveNumber)
        {
            return new BoardSnapshot
            {
                Payload = payload,
                FoxMoveNumber = foxMoveNumber,
                ProtocolLines = new[] { "re=000", "re=111" }
            };
        }

        private static SyncSessionRuntimeState GetRuntimeState(SyncSessionCoordinator coordinator)
        {
            FieldInfo field = typeof(SyncSessionCoordinator).GetField("runtimeState", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            SyncSessionRuntimeState runtimeState = field.GetValue(coordinator) as SyncSessionRuntimeState;
            Assert.NotNull(runtimeState);
            return runtimeState;
        }

        private sealed class FakeTransport : IReadBoardTransport
        {
            public event EventHandler<string> MessageReceived;

            public List<string> SentLines { get; } = new List<string>();
            public List<string> ErrorMessages { get; } = new List<string>();
            public int StartCount { get; private set; }
            public int StopCount { get; private set; }

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
                SentLines.Add(line);
            }

            public void SendError(string message)
            {
                ErrorMessages.Add(message);
            }

            public void Start()
            {
                StartCount++;
                IsConnected = true;
            }

            public void Stop()
            {
                StopCount++;
                IsConnected = false;
            }
        }

        private sealed class CapturingHost : IProtocolCommandHost
        {
            public int DispatchCount { get; private set; }
            public MoveRequest LastMoveRequest { get; private set; }

            public void DispatchProtocolCommand(Action command)
            {
                DispatchCount++;
                command();
            }

            public void HandleLossFocus()
            {
            }

            public void HandlePlaceRequest(MoveRequest request)
            {
                LastMoveRequest = request;
            }

            public void HandleYikeContext(YikeWindowContext context)
            {
            }

            public void HandleQuitRequest()
            {
            }

            public void HandleStopInBoardRequest()
            {
            }

            public void HandleVersionRequest()
            {
            }
        }
    }
}
