using System;
using System.Diagnostics;

namespace readboard
{
    internal sealed class OutboundProtocolDispatcher
    {
        private readonly IReadBoardTransport transport;
        private readonly IReadBoardProtocolAdapter protocolAdapter;
        private readonly object syncRoot = new object();
        private volatile bool closed;

        public OutboundProtocolDispatcher(IReadBoardTransport transport, IReadBoardProtocolAdapter protocolAdapter)
        {
            if (transport == null)
                throw new ArgumentNullException("transport");
            if (protocolAdapter == null)
                throw new ArgumentNullException("protocolAdapter");

            this.transport = transport;
            this.protocolAdapter = protocolAdapter;
        }

        public bool IsClosed
        {
            get { return closed; }
        }

        public void Open()
        {
            lock (syncRoot)
                closed = false;
        }

        public void Close()
        {
            lock (syncRoot)
                closed = true;
        }

        public void SendLegacyLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            Send(ProtocolMessage.CreateLegacyLine(line));
        }

        public void Send(ProtocolMessage message)
        {
            ExecuteBatch(delegate
            {
                SendMessageWhileSynchronized(message);
            });
        }

        public void SendError(string message)
        {
            ExecuteBatch(delegate
            {
                SendErrorWhileSynchronized(message);
            });
        }

        public void SendShutdown(
            ProtocolMessage stopSyncMessage,
            ProtocolMessage bothSyncDisabledMessage,
            ProtocolMessage endSyncMessage)
        {
            ExecuteBatch(delegate
            {
                SendMessageWhileSynchronized(stopSyncMessage);
                SendMessageWhileSynchronized(bothSyncDisabledMessage);
                SendMessageWhileSynchronized(endSyncMessage);
                closed = true;
            });
        }

        public void ExecuteBatch(Action batchAction)
        {
            if (batchAction == null)
                throw new ArgumentNullException("batchAction");

            lock (syncRoot)
            {
                if (closed)
                    return;

                batchAction();
            }
        }

        internal void SendLegacyLineWhileSynchronized(string line)
        {
            Debug.Assert(System.Threading.Monitor.IsEntered(syncRoot), "syncRoot must be held when sending a synchronized legacy line.");
            if (string.IsNullOrWhiteSpace(line))
                return;

            SendMessageWhileSynchronized(ProtocolMessage.CreateLegacyLine(line));
        }

        internal void SendMessageWhileSynchronized(ProtocolMessage message)
        {
            Debug.Assert(System.Threading.Monitor.IsEntered(syncRoot), "syncRoot must be held when sending a synchronized protocol message.");
            SendCore(message);
        }

        internal void SendErrorWhileSynchronized(string message)
        {
            Debug.Assert(System.Threading.Monitor.IsEntered(syncRoot), "syncRoot must be held when sending a synchronized protocol error.");
            transport.SendError(message);
        }

        private void SendCore(ProtocolMessage message)
        {
            string line = protocolAdapter.Serialize(message);
            if (string.IsNullOrWhiteSpace(line))
                return;

            transport.Send(line);
        }
    }
}
