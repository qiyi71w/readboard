using System;

namespace readboard
{
    internal sealed class MovePlacementRequest
    {
        public BoardFrame Frame { get; set; }
        public MoveRequest Move { get; set; }
        public bool BringTargetToFront { get; set; }
        public Func<bool> ShouldCancel { get; set; }
    }
}
