using System;
using System.Collections.Generic;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class SessionCoordinatorScopeTests
    {
        [Fact]
        public void Run_SetsAndClearsActiveCoordinatorWhileDisposingCoordinator()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            ISyncSessionCoordinator activeCoordinator = null;

            SessionCoordinatorScope.Run(
                coordinator,
                value => activeCoordinator = value,
                value =>
                {
                    Assert.Same(coordinator, value);
                    Assert.Same(coordinator, activeCoordinator);
                    value.Start();
                });

            Assert.Null(activeCoordinator);
            Assert.Equal(1, transport.StopCount);
        }

        [Fact]
        public void Run_ClearsActiveCoordinatorWhenCallbackThrows()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            ISyncSessionCoordinator activeCoordinator = null;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => SessionCoordinatorScope.Run(
                    coordinator,
                    value => activeCoordinator = value,
                    value =>
                    {
                        value.Start();
                        throw new InvalidOperationException("boom");
                    }));

            Assert.Equal("boom", exception.Message);
            Assert.Null(activeCoordinator);
            Assert.Equal(1, transport.StopCount);
        }

        private sealed class RecordingTransport : IReadBoardTransport
        {
            public event EventHandler<string> MessageReceived;

            public bool IsConnected { get; private set; }
            public List<string> SentLines { get; } = new List<string>();
            public int StopCount { get; private set; }

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
                StopCount++;
                IsConnected = false;
            }
        }
    }
}
