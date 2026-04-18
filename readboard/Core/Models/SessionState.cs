namespace readboard
{
    internal sealed class SessionState
    {
        public SessionState()
        {
            PendingMove = new PendingMoveState();
        }

        public bool StartedSync { get; set; }
        public bool KeepSync { get; set; }
        public bool IsContinuousSyncing { get; set; }
        public bool SyncBoth { get; set; }
        public string LastBoardPayload { get; set; }
        public string LastOverlayProtocolLine { get; set; }
        public PendingMoveState PendingMove { get; private set; }
    }

    internal sealed class PendingMoveState
    {
        public PendingMoveState()
        {
            Reset();
        }

        public int X { get; set; }
        public int Y { get; set; }
        public int AttemptsRemaining { get; set; }
        public bool Active { get; set; }
        public bool Completed { get; set; }
        public bool Succeeded { get; set; }
        public bool VerifyMove { get; set; }
        public bool PlacementInProgress { get; set; }

        public void Reset()
        {
            X = -1;
            Y = -1;
            AttemptsRemaining = 0;
            Active = false;
            Completed = false;
            Succeeded = false;
            VerifyMove = false;
            PlacementInProgress = false;
        }
    }
}
