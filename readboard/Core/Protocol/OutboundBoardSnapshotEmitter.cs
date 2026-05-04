using System;
using System.Collections.Generic;

namespace readboard
{
    internal sealed class OutboundBoardSnapshotBatch
    {
        public OutboundBoardSnapshotBatch(
            IList<ProtocolMessage> windowContextMessages,
            bool shouldForceRebuild,
            int? foxMoveNumber,
            IList<string> protocolLines)
        {
            WindowContextMessages = windowContextMessages;
            ShouldForceRebuild = shouldForceRebuild;
            FoxMoveNumber = foxMoveNumber;
            ProtocolLines = protocolLines;
        }

        public IList<ProtocolMessage> WindowContextMessages { get; private set; }
        public bool ShouldForceRebuild { get; private set; }
        public int? FoxMoveNumber { get; private set; }
        public IList<string> ProtocolLines { get; private set; }
    }

    internal sealed class OutboundBoardSnapshotEmitter
    {
        private readonly OutboundProtocolDispatcher outboundProtocolDispatcher;
        private readonly IReadBoardProtocolAdapter protocolAdapter;

        public OutboundBoardSnapshotEmitter(
            OutboundProtocolDispatcher outboundProtocolDispatcher,
            IReadBoardProtocolAdapter protocolAdapter)
        {
            if (outboundProtocolDispatcher == null)
                throw new ArgumentNullException("outboundProtocolDispatcher");
            if (protocolAdapter == null)
                throw new ArgumentNullException("protocolAdapter");

            this.outboundProtocolDispatcher = outboundProtocolDispatcher;
            this.protocolAdapter = protocolAdapter;
        }

        public void Emit(OutboundBoardSnapshotBatch batch)
        {
            if (batch == null)
                throw new ArgumentNullException("batch");

            outboundProtocolDispatcher.ExecuteBatch(delegate
            {
                EmitWhileSynchronized(batch);
            });
        }

        internal void EmitWhileSynchronized(OutboundBoardSnapshotBatch batch)
        {
            if (batch == null)
                throw new ArgumentNullException("batch");

            IList<ProtocolMessage> windowContextMessages = batch.WindowContextMessages;
            if (windowContextMessages != null)
            {
                for (int i = 0; i < windowContextMessages.Count; i++)
                    outboundProtocolDispatcher.SendMessageWhileSynchronized(windowContextMessages[i]);
            }

            if (batch.ShouldForceRebuild)
                outboundProtocolDispatcher.SendMessageWhileSynchronized(protocolAdapter.CreateForceRebuildMessage());

            if (batch.FoxMoveNumber.HasValue)
            {
                outboundProtocolDispatcher.SendMessageWhileSynchronized(
                    protocolAdapter.CreateFoxMoveNumberMessage(batch.FoxMoveNumber.Value));
            }

            IList<string> protocolLines = batch.ProtocolLines;
            if (protocolLines != null)
            {
                for (int i = 0; i < protocolLines.Count; i++)
                    outboundProtocolDispatcher.SendLegacyLineWhileSynchronized(protocolLines[i]);
            }

            outboundProtocolDispatcher.SendMessageWhileSynchronized(protocolAdapter.CreateBoardEndMessage());
        }
    }
}
