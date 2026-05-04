using System;

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
            lock (syncRoot)
            {
                if (closed)
                    return;

                SendCore(message);
            }
        }

        public void SendError(string message)
        {
            lock (syncRoot)
            {
                if (closed)
                    return;

                transport.SendError(message);
            }
        }

        public void SendShutdown(
            ProtocolMessage stopSyncMessage,
            ProtocolMessage bothSyncDisabledMessage,
            ProtocolMessage endSyncMessage)
        {
            lock (syncRoot)
            {
                if (closed)
                    return;

                SendCore(stopSyncMessage);
                SendCore(bothSyncDisabledMessage);
                SendCore(endSyncMessage);
                closed = true;
            }
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
