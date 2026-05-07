using System;
using System.Globalization;

namespace readboard
{
    internal sealed class LegacyProtocolAdapter : IReadBoardProtocolAdapter
    {
        public ProtocolMessage ParseInbound(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return null;

            string trimmed = rawLine.Trim();
            if (string.Equals(trimmed, ProtocolKeywords.ReadboardUpdateSupported, StringComparison.Ordinal))
                return new ProtocolMessage { Kind = ProtocolMessageKind.ReadboardUpdateSupported, RawText = trimmed };
            if (string.Equals(trimmed, ProtocolKeywords.ReadboardUpdateInstalling, StringComparison.Ordinal))
                return new ProtocolMessage { Kind = ProtocolMessageKind.ReadboardUpdateInstalling, RawText = trimmed };
            if (string.Equals(trimmed, ProtocolKeywords.ReadboardUpdateCancelled, StringComparison.Ordinal))
                return new ProtocolMessage { Kind = ProtocolMessageKind.ReadboardUpdateCancelled, RawText = trimmed };
            if (trimmed.StartsWith(ProtocolKeywords.ReadboardUpdateFailedPrefix, StringComparison.Ordinal)
                && trimmed.Length > ProtocolKeywords.ReadboardUpdateFailedPrefix.Length)
            {
                return new ProtocolMessage { Kind = ProtocolMessageKind.ReadboardUpdateFailed, RawText = trimmed };
            }
            if (trimmed.StartsWith(ProtocolKeywords.Place, StringComparison.Ordinal))
                return CreatePlaceMessage(trimmed);
            if (trimmed.StartsWith(ProtocolKeywords.Loss, StringComparison.Ordinal))
                return new ProtocolMessage { Kind = ProtocolMessageKind.LossFocus, RawText = trimmed };
            if (trimmed.StartsWith(ProtocolKeywords.NotInBoard, StringComparison.Ordinal))
                return new ProtocolMessage { Kind = ProtocolMessageKind.StopInBoard, RawText = trimmed };
            if (trimmed.StartsWith(ProtocolKeywords.Version, StringComparison.Ordinal))
                return new ProtocolMessage { Kind = ProtocolMessageKind.VersionRequest, RawText = trimmed };
            if (trimmed.StartsWith(ProtocolKeywords.Quit, StringComparison.Ordinal))
                return new ProtocolMessage { Kind = ProtocolMessageKind.Quit, RawText = trimmed };
            if (string.Equals(trimmed, ProtocolKeywords.YikeBrowserSyncStop, StringComparison.Ordinal))
                return new ProtocolMessage { Kind = ProtocolMessageKind.YikeBrowserSyncStop, RawText = trimmed };
            if (trimmed == ProtocolKeywords.YikeGeometry || trimmed.StartsWith(ProtocolKeywords.YikeGeometry + " ", StringComparison.Ordinal))
                return ParseYikeGeometry(trimmed);
            if (trimmed == ProtocolKeywords.Yike || trimmed.StartsWith(ProtocolKeywords.Yike + " ", StringComparison.Ordinal))
                return ParseYikeContext(trimmed);
            return ProtocolMessage.CreateLegacyLine(trimmed);
        }

        public string Serialize(ProtocolMessage message)
        {
            return message == null ? string.Empty : message.RawText;
        }

        public ProtocolMessage CreateReadyMessage()
        {
            return CreateLegacyMessage(ProtocolKeywords.Ready);
        }

        public ProtocolMessage CreateClearMessage()
        {
            return CreateLegacyMessage(ProtocolKeywords.Clear);
        }

        public ProtocolMessage CreateBoardEndMessage()
        {
            return CreateLegacyMessage(ProtocolKeywords.BoardEnd);
        }

        public ProtocolMessage CreatePonderStatusMessage(bool playPonderEnabled)
        {
            return CreateLegacyMessage(
                playPonderEnabled ? ProtocolKeywords.PlayPonderOn : ProtocolKeywords.PlayPonderOff);
        }

        public ProtocolMessage CreateVersionMessage(string version)
        {
            return CreateLegacyMessage(ProtocolKeywords.VersionResponsePrefix + version);
        }

        public ProtocolMessage CreateSyncMessage()
        {
            return CreateLegacyMessage(ProtocolKeywords.Sync);
        }

        public ProtocolMessage CreateStopSyncMessage()
        {
            return CreateLegacyMessage(ProtocolKeywords.StopSync);
        }

        public ProtocolMessage CreateEndSyncMessage()
        {
            return CreateLegacyMessage(ProtocolKeywords.EndSync);
        }

        public ProtocolMessage CreateBothSyncMessage(bool enabled)
        {
            return CreateLegacyMessage(enabled ? ProtocolKeywords.BothSync : ProtocolKeywords.NoBothSync);
        }

        public ProtocolMessage CreateForegroundFoxInBoardMessage(bool enabled)
        {
            return CreateLegacyMessage(
                enabled
                    ? ProtocolKeywords.ForegroundFoxWithInBoard
                    : ProtocolKeywords.NotForegroundFoxWithInBoard);
        }

        public ProtocolMessage CreateSyncPlatformMessage(string platform)
        {
            return CreateLegacyMessage(
                ProtocolKeywords.SyncPlatformPrefix
                + NormalizeTextValue(platform, ProtocolKeywords.GenericSyncPlatform));
        }

        public ProtocolMessage CreateRoomTokenMessage(string roomToken)
        {
            return CreateLegacyMessage(ProtocolKeywords.RoomTokenPrefix + roomToken);
        }

        public ProtocolMessage CreateLiveTitleMoveMessage(int moveNumber)
        {
            return CreateLegacyMessage(ProtocolKeywords.LiveTitleMovePrefix + moveNumber);
        }

        public ProtocolMessage CreateRecordCurrentMoveMessage(int moveNumber)
        {
            return CreateLegacyMessage(ProtocolKeywords.RecordCurrentMovePrefix + moveNumber);
        }

        public ProtocolMessage CreateRecordTotalMoveMessage(int moveNumber)
        {
            return CreateLegacyMessage(ProtocolKeywords.RecordTotalMovePrefix + moveNumber);
        }

        public ProtocolMessage CreateRecordAtEndMessage(bool atEnd)
        {
            return CreateLegacyMessage(atEnd ? ProtocolKeywords.RecordAtEndTrue : ProtocolKeywords.RecordAtEndFalse);
        }

        public ProtocolMessage CreateRecordTitleFingerprintMessage(string fingerprint)
        {
            return CreateLegacyMessage(ProtocolKeywords.RecordTitleFingerprintPrefix + fingerprint);
        }

        public ProtocolMessage CreateForceRebuildMessage()
        {
            return ProtocolMessage.CreateForceRebuildLine(ProtocolKeywords.ForceRebuild);
        }

        public ProtocolMessage CreateFoxMoveNumberMessage(int moveNumber)
        {
            return CreateLegacyMessage(ProtocolKeywords.FoxMoveNumberPrefix + moveNumber);
        }

        public ProtocolMessage CreateYikeRoomTokenMessage(string roomToken)
        {
            return CreateLegacyMessage(ProtocolKeywords.YikeRoomTokenPrefix + (roomToken ?? string.Empty));
        }

        public ProtocolMessage CreateYikeMoveNumberMessage(int moveNumber)
        {
            return CreateLegacyMessage(ProtocolKeywords.YikeMoveNumberPrefix + moveNumber);
        }

        public ProtocolMessage CreateYikeSyncStartMessage()
        {
            return CreateLegacyMessage(ProtocolKeywords.YikeSyncStart);
        }

        public ProtocolMessage CreateYikeSyncStopMessage()
        {
            return CreateLegacyMessage(ProtocolKeywords.YikeSyncStop);
        }

        public ProtocolMessage CreateStartMessage(int boardWidth, int boardHeight, IntPtr windowHandle, bool includeWindowHandle)
        {
            string line = ProtocolKeywords.StartPrefix + boardWidth + " " + boardHeight;
            if (includeWindowHandle)
                line += " " + windowHandle;
            return CreateLegacyMessage(line);
        }

        public ProtocolMessage CreatePlayMessage(string color, string time, string playouts, string firstPolicy)
        {
            return CreateLegacyMessage(
                ProtocolKeywords.PlayPrefix
                + color
                + ProtocolKeywords.PlaySeparator
                + NormalizeNumericValue(time)
                + " "
                + NormalizeNumericValue(playouts)
                + " "
                + NormalizeNumericValue(firstPolicy));
        }

        public ProtocolMessage CreateNoInBoardMessage()
        {
            return CreateLegacyMessage(ProtocolKeywords.NoInBoard);
        }

        public ProtocolMessage CreateNotInBoardMessage()
        {
            return CreateLegacyMessage(ProtocolKeywords.NotInBoard);
        }

        public ProtocolMessage CreatePlacementResultMessage(bool success)
        {
            return CreateLegacyMessage(success ? ProtocolKeywords.PlaceComplete : ProtocolKeywords.PlacementFailed);
        }

        public ProtocolMessage CreateTimeChangedMessage(string value)
        {
            return CreateLegacyMessage(ProtocolKeywords.TimeChangedPrefix + NormalizeNumericValue(value));
        }

        public ProtocolMessage CreatePlayoutsChangedMessage(string value)
        {
            return CreateLegacyMessage(ProtocolKeywords.PlayoutsChangedPrefix + NormalizeNumericValue(value));
        }

        public ProtocolMessage CreateFirstPolicyChangedMessage(string value)
        {
            return CreateLegacyMessage(ProtocolKeywords.FirstPolicyChangedPrefix + NormalizeNumericValue(value));
        }

        public ProtocolMessage CreateNoPonderMessage()
        {
            return CreateLegacyMessage(ProtocolKeywords.NoPonder);
        }

        public ProtocolMessage CreateStopAutoPlayMessage()
        {
            return CreateLegacyMessage(ProtocolKeywords.StopAutoPlay);
        }

        public ProtocolMessage CreatePassMessage()
        {
            return CreateLegacyMessage(ProtocolKeywords.Pass);
        }

        public ProtocolMessage CreateReadboardUpdateReadyMessage(string tag, string absoluteZipPath)
        {
            if (string.IsNullOrWhiteSpace(tag))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(tag));
            if (string.IsNullOrWhiteSpace(absoluteZipPath))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(absoluteZipPath));
            if (tag.IndexOf('\t') >= 0)
                throw new ArgumentException("Value cannot contain tabs.", nameof(tag));
            if (absoluteZipPath.IndexOf('\t') >= 0)
                throw new ArgumentException("Value cannot contain tabs.", nameof(absoluteZipPath));

            return CreateLegacyMessage(ProtocolKeywords.ReadboardUpdateReadyPrefix + tag + "\t" + absoluteZipPath);
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

        private static ProtocolMessage ParseYikeContext(string trimmed)
        {
            string roomToken = null;
            int? moveNumber = null;
            string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = 1; index < parts.Length; index++)
            {
                string part = parts[index];
                if (part.StartsWith("room=", StringComparison.Ordinal))
                {
                    string value = part.Substring("room=".Length);
                    roomToken = string.IsNullOrWhiteSpace(value) ? null : value;
                    continue;
                }

                if (part.StartsWith("move=", StringComparison.Ordinal))
                {
                    string value = part.Substring("move=".Length);
                    if (int.TryParse(value, out int parsedMove) && parsedMove > 0)
                        moveNumber = parsedMove;
                }
            }

            return new ProtocolMessage
            {
                Kind = ProtocolMessageKind.YikeContext,
                RawText = trimmed,
                YikeRoomToken = roomToken,
                YikeMoveNumber = moveNumber
            };
        }

        private static ProtocolMessage ParseYikeGeometry(string trimmed)
        {
            int? left = null;
            int? top = null;
            int? width = null;
            int? height = null;
            int? boardSize = null;
            double? firstX = null;
            double? firstY = null;
            double? cellX = null;
            double? cellY = null;
            string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = 1; index < parts.Length; index++)
            {
                string part = parts[index];
                if (part.StartsWith("left=", StringComparison.Ordinal))
                {
                    left = ParsePositiveInt(part.Substring("left=".Length));
                    continue;
                }

                if (part.StartsWith("top=", StringComparison.Ordinal))
                {
                    top = ParsePositiveInt(part.Substring("top=".Length));
                    continue;
                }

                if (part.StartsWith("width=", StringComparison.Ordinal))
                {
                    width = ParsePositiveInt(part.Substring("width=".Length));
                    continue;
                }

                if (part.StartsWith("height=", StringComparison.Ordinal))
                {
                    height = ParsePositiveInt(part.Substring("height=".Length));
                    continue;
                }

                if (part.StartsWith("board=", StringComparison.Ordinal))
                {
                    boardSize = ParsePositiveInt(part.Substring("board=".Length));
                    continue;
                }

                if (part.StartsWith("firstX=", StringComparison.Ordinal))
                {
                    firstX = ParsePositiveDouble(part.Substring("firstX=".Length));
                    continue;
                }

                if (part.StartsWith("firstY=", StringComparison.Ordinal))
                {
                    firstY = ParsePositiveDouble(part.Substring("firstY=".Length));
                    continue;
                }

                if (part.StartsWith("cellX=", StringComparison.Ordinal))
                {
                    cellX = ParsePositiveDouble(part.Substring("cellX=".Length));
                    continue;
                }

                if (part.StartsWith("cellY=", StringComparison.Ordinal))
                    cellY = ParsePositiveDouble(part.Substring("cellY=".Length));
            }

            YikeBoardGeometry geometry = null;
            if (left.HasValue && top.HasValue && width.HasValue && height.HasValue && boardSize.HasValue)
            {
                geometry = new YikeBoardGeometry
                {
                    Bounds = new PixelRect(left.Value, top.Value, width.Value, height.Value),
                    BoardSize = boardSize.Value
                };

                if (firstX.HasValue && firstY.HasValue && cellX.HasValue && cellY.HasValue)
                {
                    geometry.FirstIntersectionX = firstX.Value;
                    geometry.FirstIntersectionY = firstY.Value;
                    geometry.CellWidth = cellX.Value;
                    geometry.CellHeight = cellY.Value;
                }
            }

            return new ProtocolMessage
            {
                Kind = ProtocolMessageKind.YikeGeometry,
                RawText = trimmed,
                YikeGeometry = geometry
            };
        }

        private static int? ParsePositiveInt(string value)
        {
            return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : (int?)null;
        }

        private static double? ParsePositiveDouble(string value)
        {
            return double.TryParse(
                    value,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out double parsed)
                && parsed > 0d
                && !double.IsNaN(parsed)
                && !double.IsInfinity(parsed)
                ? parsed
                : (double?)null;
        }

        private static string NormalizeNumericValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? ProtocolKeywords.DefaultNumericValue : value;
        }

        private static string NormalizeTextValue(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
