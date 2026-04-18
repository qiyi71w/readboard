using System.Collections.Generic;

namespace readboard
{
    internal sealed class BoardSnapshot
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public BoardCellState[] BoardState { get; set; }
        public bool IsValid { get; set; }
        public bool IsAllBlack { get; set; }
        public bool IsAllWhite { get; set; }
        public int BlackStoneCount { get; set; }
        public int WhiteStoneCount { get; set; }
        public BoardCoordinate LastMove { get; set; }
        public int? FoxMoveNumber { get; set; }
        public bool NeedsPrintWindowFallback { get; set; }
        public string Payload { get; set; }
        public IList<string> ProtocolLines { get; set; }
        public ulong StateSignature { get; set; }
        public bool IsUnchangedFromPrevious { get; set; }
        public bool ReusedPayload { get; set; }
    }
}
