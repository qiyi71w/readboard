using System;

namespace readboard
{
    internal interface IReadBoardProtocolAdapter
    {
        ProtocolMessage ParseInbound(string rawLine);
        string Serialize(ProtocolMessage message);
        ProtocolMessage CreateReadyMessage();
        ProtocolMessage CreateClearMessage();
        ProtocolMessage CreateBoardEndMessage();
        ProtocolMessage CreatePonderStatusMessage(bool playPonderEnabled);
        ProtocolMessage CreateVersionMessage(string version);
        ProtocolMessage CreateSyncMessage();
        ProtocolMessage CreateStopSyncMessage();
        ProtocolMessage CreateEndSyncMessage();
        ProtocolMessage CreateBothSyncMessage(bool enabled);
        ProtocolMessage CreateForegroundFoxInBoardMessage(bool enabled);
        ProtocolMessage CreateStartMessage(int boardWidth, int boardHeight, IntPtr windowHandle, bool includeWindowHandle);
        ProtocolMessage CreatePlayMessage(string color, string time, string playouts, string firstPolicy);
        ProtocolMessage CreateNoInBoardMessage();
        ProtocolMessage CreateNotInBoardMessage();
        ProtocolMessage CreatePlacementResultMessage(bool success);
        ProtocolMessage CreateTimeChangedMessage(string value);
        ProtocolMessage CreatePlayoutsChangedMessage(string value);
        ProtocolMessage CreateFirstPolicyChangedMessage(string value);
        ProtocolMessage CreateNoPonderMessage();
        ProtocolMessage CreateStopAutoPlayMessage();
        ProtocolMessage CreatePassMessage();
    }
}
