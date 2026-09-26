using System;

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

    // The coordinator serializes every transition under its state lock.
    internal sealed class PendingMoveState
    {
        private static readonly TimeSpan VerificationWindow = TimeSpan.FromMilliseconds(500);
        // Finish before the host's three-second ACK timeout, even without usable captures.
        private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromSeconds(2);

        private enum Phase { Idle, Ready, Placing, Confirming, Completed, Cancelled }

        private Phase phase;
        private TimeProvider clock;
        private int x;
        private int y;
        private int attemptsRemaining;
        private bool verifyMove;
        private bool succeeded;
        private long queuedTimestamp;
        private long verificationStartedTimestamp;

        public bool IsPlacementAvailable => phase == Phase.Ready;
        public bool HasCompletedResult => phase == Phase.Completed || phase == Phase.Cancelled;

        public bool TryQueue(MoveRequest request, TimeProvider timeProvider)
        {
            if (request == null || phase != Phase.Idle)
                return false;

            clock = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            x = request.X;
            y = request.Y;
            verifyMove = request.VerifyMove;
            attemptsRemaining = verifyMove
                ? AppConfig.ResolveMoveVerifyTotalPlacementAttempts(request.MoveVerifyMaxAttempts)
                : 1;
            queuedTimestamp = clock.GetTimestamp();
            phase = Phase.Ready;
            return true;
        }

        public bool TryBeginPlacement(out MoveRequest request)
        {
            request = null;
            if (!IsPlacementAvailable)
                return false;
            if (attemptsRemaining <= 0 || HasTimedOut())
            {
                Complete(false);
                return false;
            }

            attemptsRemaining--;
            phase = Phase.Placing;
            request = new MoveRequest { X = x, Y = y, VerifyMove = verifyMove };
            return true;
        }

        public void CompletePlacement(bool success, bool keepSync)
        {
            if (phase != Phase.Placing)
                return;
            if (verifyMove && success && keepSync)
            {
                if (!HasTimedOut())
                {
                    verificationStartedTimestamp = clock.GetTimestamp();
                    phase = Phase.Confirming;
                    return;
                }
                success = false;
            }
            Complete(success);
        }

        public void Observe(BoardSnapshot snapshot, int boardWidth)
        {
            if (phase != Phase.Confirming)
                return;
            if (HasTimedOut())
                Complete(false);
            else if (IsMoveVisible(snapshot, boardWidth))
                Complete(true);
            else if (snapshot != null && snapshot.IsValid
                && clock.GetElapsedTime(verificationStartedTimestamp) >= VerificationWindow)
            {
                // Elapsed time alone never authorizes another physical attempt.
                if (attemptsRemaining <= 0)
                    Complete(false);
                else
                    phase = Phase.Ready;
            }
        }

        public void Cancel()
        {
            // An in-flight physical operation must report its own outcome.
            if (phase == Phase.Ready || phase == Phase.Confirming)
                phase = Phase.Cancelled;
        }

        public bool TryConsumeResult(bool keepSync, out PlaceRequestExecutionResult result)
        {
            if (!keepSync)
                Cancel();
            if ((phase == Phase.Ready || phase == Phase.Confirming) && HasTimedOut())
                Complete(false);
            if (HasCompletedResult)
            {
                result = phase == Phase.Cancelled
                    ? PlaceRequestExecutionResult.NoResponse
                    : PlaceRequestExecutionResult.CreateResponse(succeeded);
                phase = Phase.Idle;
                return true;
            }
            result = PlaceRequestExecutionResult.NoResponse;
            if (!keepSync && phase != Phase.Placing)
            {
                phase = Phase.Idle;
                return true;
            }
            return false;
        }

        private bool HasTimedOut()
        {
            return verifyMove && clock.GetElapsedTime(queuedTimestamp) >= ConfirmationTimeout;
        }

        private bool IsMoveVisible(BoardSnapshot snapshot, int boardWidth)
        {
            if (snapshot == null || !snapshot.IsValid || snapshot.BoardState == null)
                return false;
            int width = boardWidth > 0 ? boardWidth : snapshot.Width;
            if (x < 0 || y < 0 || width <= 0)
                return false;
            int index = (y * width) + x;
            return index >= 0 && index < snapshot.BoardState.Length
                && snapshot.BoardState[index] != BoardCellState.Empty;
        }

        private void Complete(bool success)
        {
            succeeded = success;
            phase = Phase.Completed;
        }
    }
}
