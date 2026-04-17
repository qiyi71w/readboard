namespace readboard
{
    internal sealed class MoveRequest
    {
        public int X { get; set; }
        public int Y { get; set; }
        public bool VerifyMove { get; set; }
    }
}
