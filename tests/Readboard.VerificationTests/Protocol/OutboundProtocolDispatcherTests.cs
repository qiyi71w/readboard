using System;
using System.Collections.Generic;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class OutboundProtocolDispatcherTests
    {
        [Fact]
        public void SendShutdown_SendsStopBothEndAndClosesDispatcher()
        {
            FakeTransport transport = new FakeTransport();
            LegacyProtocolAdapter protocolAdapter = new LegacyProtocolAdapter();
            OutboundProtocolDispatcher dispatcher = new OutboundProtocolDispatcher(transport, protocolAdapter);

            dispatcher.Open();
            dispatcher.SendShutdown(
                protocolAdapter.CreateStopSyncMessage(),
                protocolAdapter.CreateBothSyncMessage(false),
                protocolAdapter.CreateEndSyncMessage());
            dispatcher.Send(protocolAdapter.CreateReadyMessage());

            Assert.True(dispatcher.IsClosed);
            Assert.Equal(new[] { "stopsync", "nobothSync", "endsync" }, transport.SentLines);
        }

        [Fact]
        public void Close_PreventsMessagesUntilOpenIsCalledAgain()
        {
            FakeTransport transport = new FakeTransport();
            LegacyProtocolAdapter protocolAdapter = new LegacyProtocolAdapter();
            OutboundProtocolDispatcher dispatcher = new OutboundProtocolDispatcher(transport, protocolAdapter);

            dispatcher.Open();
            dispatcher.Close();
            dispatcher.Send(protocolAdapter.CreateReadyMessage());
            dispatcher.SendError("ignored");
            dispatcher.Open();
            dispatcher.Send(protocolAdapter.CreateReadyMessage());
            dispatcher.SendError("boom");

            Assert.Equal(new[] { "ready" }, transport.SentLines);
            Assert.Equal(new[] { "boom" }, transport.ErrorMessages);
        }

        [Fact]
        public void SendLegacyLine_IgnoresBlankInput()
        {
            FakeTransport transport = new FakeTransport();
            OutboundProtocolDispatcher dispatcher = new OutboundProtocolDispatcher(transport, new LegacyProtocolAdapter());

            dispatcher.Open();
            dispatcher.SendLegacyLine(" ");
            dispatcher.SendLegacyLine("re=123");

            Assert.Equal(new[] { "re=123" }, transport.SentLines);
        }

        private sealed class FakeTransport : IReadBoardTransport
        {
            public event EventHandler<string> MessageReceived
            {
                add { }
                remove { }
            }

            public List<string> SentLines { get; } = new List<string>();
            public List<string> ErrorMessages { get; } = new List<string>();

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
                ErrorMessages.Add(message);
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
