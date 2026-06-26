namespace readboard
{
    internal sealed class FoxCornerFlipSummary
    {
        private const int MinimumScore = 10;
        private const int MinimumMargin = 5;
        private const int MinimumInnerStonePercent = 10;

        public static FoxCornerFlipSummary Empty
        {
            get { return new FoxCornerFlipSummary(); }
        }

        private int bestScore;
        private int secondBestScore;
        private BoardCoordinate bestCoordinate;

        public void Observe(
            BoardCellState state,
            int innerBlackPercent,
            int innerWhitePercent,
            int blackOppositePercent,
            int whiteOppositePercent,
            int x,
            int y)
        {
            int score;
            if (state == BoardCellState.Black)
            {
                if (innerBlackPercent < MinimumInnerStonePercent)
                    return;
                score = blackOppositePercent;
            }
            else if (state == BoardCellState.White)
            {
                if (innerWhitePercent < MinimumInnerStonePercent)
                    return;
                score = whiteOppositePercent;
            }
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
