namespace readboard
{
    internal sealed class FoxCornerFlipSummary
    {
        private const int MinimumScore = 10;
        private const int MinimumMargin = 5;

        public static FoxCornerFlipSummary Empty { get; } = new FoxCornerFlipSummary();

        private int bestScore;
        private int secondBestScore;
        private BoardCoordinate bestCoordinate;

        public void Observe(
            BoardCellState state,
            int blackOppositePercent,
            int whiteOppositePercent,
            int x,
            int y)
        {
            int score;
            if (state == BoardCellState.Black)
                score = blackOppositePercent;
            else if (state == BoardCellState.White)
                score = whiteOppositePercent;
            else
                return;

            if (score > bestScore)
            {
                secondBestScore = bestScore;
                bestScore = score;
                bestCoordinate = new BoardCoordinate(x, y);
                return;
            }

            if (score > secondBestScore)
                secondBestScore = score;
        }

        public bool TryGetUniqueCandidate(out BoardCoordinate candidate)
        {
            if (bestCoordinate != null
                && bestScore >= MinimumScore
                && bestScore - secondBestScore >= MinimumMargin)
            {
                candidate = new BoardCoordinate(bestCoordinate.X, bestCoordinate.Y);
                return true;
            }

            candidate = null;
            return false;
        }
    }
}
