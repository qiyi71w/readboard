using System;

namespace readboard
{
    internal sealed class LegacyProtocolAdapter : IReadBoardProtocolAdapter
    {
        public ProtocolMessage ParseInbound(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return null;

            string trimmed = rawLine.Trim();
            if (trimmed.StartsWith("place", StringComparison.Ordinal))
                return CreatePlaceMessage(trimmed);
            if (trimmed.StartsWith("loss", StringComparison.Ordinal))
                return new ProtocolMessage { Kind = ProtocolMessageKind.LossFocus, RawText = trimmed };
            if (trimmed.StartsWith("notinboard", StringComparison.Ordinal))
                return new ProtocolMessage { Kind = ProtocolMessageKind.StopInBoard, RawText = trimmed };
            if (trimmed.StartsWith("version", StringComparison.Ordinal))
                return new ProtocolMessage { Kind = ProtocolMessageKind.VersionRequest, RawText = trimmed };
            if (trimmed.StartsWith("quit", StringComparison.Ordinal))
                return new ProtocolMessage { Kind = ProtocolMessageKind.Quit, RawText = trimmed };
            return ProtocolMessage.CreateLegacyLine(trimmed);
        }

        public string Serialize(ProtocolMessage message)
        {
            return message == null ? string.Empty : message.RawText;
        }

        public ProtocolMessage CreateReadyMessage()
        {
            return CreateLegacyMessage("ready");
        }

        public ProtocolMessage CreateClearMessage()
        {
            return CreateLegacyMessage("clear");
        }

        public ProtocolMessage CreateBoardEndMessage()
        {
            return CreateLegacyMessage("end");
        }

        public ProtocolMessage CreatePonderStatusMessage(bool playPonderEnabled)
        {
            return CreateLegacyMessage(playPonderEnabled ? "playponder on" : "playponder off");
        }

        public ProtocolMessage CreateVersionMessage(string version)
        {
            return CreateLegacyMessage("version: " + version);
        }

        public ProtocolMessage CreateSyncMessage()
        {
            return CreateLegacyMessage("sync");
        }

        public ProtocolMessage CreateStopSyncMessage()
        {
            return CreateLegacyMessage("stopsync");
        }

        public ProtocolMessage CreateEndSyncMessage()
        {
            return CreateLegacyMessage("endsync");
        }

        public ProtocolMessage CreateBothSyncMessage(bool enabled)
        {
            return CreateLegacyMessage(enabled ? "bothSync" : "nobothSync");
        }

        public ProtocolMessage CreateForegroundFoxInBoardMessage(bool enabled)
        {
            return CreateLegacyMessage(enabled ? "foreFoxWithInBoard" : "notForeFoxWithInBoard");
        }

        public ProtocolMessage CreateFoxMoveNumberMessage(int moveNumber)
        {
            return CreateLegacyMessage("foxMoveNumber " + moveNumber);
        }

        public ProtocolMessage CreateStartMessage(int boardWidth, int boardHeight, IntPtr windowHandle, bool includeWindowHandle)
        {
            string line = "start " + boardWidth + " " + boardHeight;
            if (includeWindowHandle)
                line += " " + windowHandle;
            return CreateLegacyMessage(line);
        }

        public ProtocolMessage CreatePlayMessage(string color, string time, string playouts, string firstPolicy)
        {
            return CreateLegacyMessage(
                "play>"
                + color
                + ">"
                + NormalizeNumericValue(time)
                + " "
                + NormalizeNumericValue(playouts)
                + " "
                + NormalizeNumericValue(firstPolicy));
        }

        public ProtocolMessage CreateNoInBoardMessage()
        {
            return CreateLegacyMessage("noinboard");
        }

        public ProtocolMessage CreateNotInBoardMessage()
        {
            return CreateLegacyMessage("notinboard");
        }

        public ProtocolMessage CreatePlacementResultMessage(bool success)
        {
            return CreateLegacyMessage(success ? "placeComplete" : "error place failed");
        }

        public ProtocolMessage CreateTimeChangedMessage(string value)
        {
            return CreateLegacyMessage("timechanged " + NormalizeNumericValue(value));
        }

        public ProtocolMessage CreatePlayoutsChangedMessage(string value)
        {
            return CreateLegacyMessage("playoutschanged " + NormalizeNumericValue(value));
        }

        public ProtocolMessage CreateFirstPolicyChangedMessage(string value)
        {
            return CreateLegacyMessage("firstchanged " + NormalizeNumericValue(value));
        }

        public ProtocolMessage CreateNoPonderMessage()
        {
            return CreateLegacyMessage("noponder");
        }

        public ProtocolMessage CreateStopAutoPlayMessage()
        {
            return CreateLegacyMessage("stopAutoPlay");
        }

        public ProtocolMessage CreatePassMessage()
        {
            return CreateLegacyMessage("pass");
        }

        private static ProtocolMessage CreatePlaceMessage(string rawLine)
        {
            string[] parts = rawLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                return ProtocolMessage.CreateLegacyLine(rawLine);

            int x;
            int y;
            if (!int.TryParse(parts[1], out x) || !int.TryParse(parts[2], out y))
                return ProtocolMessage.CreateLegacyLine(rawLine);

            return new ProtocolMessage
            {
                Kind = ProtocolMessageKind.PlaceMove,
                RawText = rawLine,
                MoveRequest = new MoveRequest { X = x, Y = y }
            };
        }

        private static ProtocolMessage CreateLegacyMessage(string rawLine)
        {
            return ProtocolMessage.CreateLegacyLine(rawLine);
        }

        private static string NormalizeNumericValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "0" : value;
        }
    }
}
