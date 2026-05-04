using System;
using System.Collections.Generic;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class OutboundBoardSnapshotEmitterTests
    {
        [Fact]
        public void Emit_SendsWindowContextForceRebuildFoxMoveBoardLinesAndEndInOrder()
        {
            FakeTransport transport = new FakeTransport();
            LegacyProtocolAdapter protocolAdapter = new LegacyProtocolAdapter();
            OutboundProtocolDispatcher dispatcher = new OutboundProtocolDispatcher(transport, protocolAdapter);
            OutboundBoardSnapshotEmitter emitter = new OutboundBoardSnapshotEmitter(dispatcher, protocolAdapter);

            dispatcher.Open();
            emitter.Emit(new OutboundBoardSnapshotBatch(
                new[]
                {
                    protocolAdapter.CreateSyncPlatformMessage("fox"),
                    protocolAdapter.CreateRoomTokenMessage("43581号")
                },
                true,
                57,
                new[] { "re=000", "re=111" }));

            Assert.Equal(
                new[]
                {
                    "syncPlatform fox",
                    "roomToken 43581号",
                    "forceRebuild",
                    "foxMoveNumber 57",
                    "re=000",
                    "re=111",
                    "end"
                },
                transport.SentLines);
        }

        [Fact]
        public void Emit_SkipsOptionalSegmentsWhenBatchDoesNotNeedThem()
        {
            FakeTransport transport = new FakeTransport();
            LegacyProtocolAdapter protocolAdapter = new LegacyProtocolAdapter();
            OutboundProtocolDispatcher dispatcher = new OutboundProtocolDispatcher(transport, protocolAdapter);
            OutboundBoardSnapshotEmitter emitter = new OutboundBoardSnapshotEmitter(dispatcher, protocolAdapter);

            dispatcher.Open();
            emitter.Emit(new OutboundBoardSnapshotBatch(
                new[] { protocolAdapter.CreateSyncPlatformMessage("generic") },
                false,
                null,
                new[] { "re=000" }));

            Assert.Equal(
                new[]
                {
                    "syncPlatform generic",
                    "re=000",
                    "end"
                },
                transport.SentLines);
        }

        private sealed class FakeTransport : IReadBoardTransport
        {
            public event EventHandler<string> MessageReceived
            {
                add { }
                remove { }
            }

            public List<string> SentLines { get; } = new List<string>();

            public bool IsConnected { get; private set; }

            public void Dispose()
            {
            }

            public void Send(string line)
            {
                SentLines.Add(line);
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
    }
}
