using System;

namespace readboard
{
    internal static class LastMoveInferenceResolver
    {
        public static LastMoveInference Apply(
            BoardCellState[] boardState,
            int boardWidth,
            StoneSummary blackSummary,
            StoneSummary whiteSummary,
            MarkerSummary markerSummary,
            FoxCornerFlipSummary foxCornerFlipSummary)
        {
            LastMoveInference lastMove = TryApplyMarkerLastMove(boardState, boardWidth, blackSummary, whiteSummary, markerSummary);
            if (lastMove.Source != LastMoveSource.None)
                return lastMove;

            lastMove = TryApplyFoxCornerFlipLastMove(boardState, boardWidth, blackSummary, whiteSummary, foxCornerFlipSummary);
            if (lastMove.Source != LastMoveSource.None)
                return lastMove;

            lastMove = TryApplyDeviationLastMove(boardState, boardWidth, blackSummary, whiteSummary);
            if (lastMove.Source != LastMoveSource.None)
                return lastMove;

            return TryApplyStoneCountLastMove(boardState, boardWidth, blackSummary, whiteSummary);
        }

        private static LastMoveInference TryApplyMarkerLastMove(
            BoardCellState[] boardState,
            int boardWidth,
            StoneSummary blackSummary,
            StoneSummary whiteSummary,
            MarkerSummary markerSummary)
        {
            bool redOnly = markerSummary.RedCount == 1 && markerSummary.BlueCount != 1;
            bool blueOnly = markerSummary.RedCount != 1 && markerSummary.BlueCount == 1;
            if (!redOnly && !blueOnly)
                return LastMoveInference.None;

            return PromoteLastMove(
                boardState,
                boardWidth,
                markerSummary.Candidate,
                blackSummary,
                whiteSummary,
                LastMoveSource.RedBlueMarker);
        }

        private static LastMoveInference TryApplyFoxCornerFlipLastMove(
            BoardCellState[] boardState,
            int boardWidth,
            StoneSummary blackSummary,
            StoneSummary whiteSummary,
            FoxCornerFlipSummary foxCornerFlipSummary)
        {
            BoardCoordinate candidate;
            if (foxCornerFlipSummary == null || !foxCornerFlipSummary.TryGetUniqueCandidate(out candidate))
                return LastMoveInference.None;

            return PromoteLastMove(
                boardState,
                boardWidth,
                candidate,
                blackSummary,
                whiteSummary,
                LastMoveSource.FoxCornerFlip);
        }

        private static LastMoveInference TryApplyDeviationLastMove(
            BoardCellState[] boardState,
            int boardWidth,
            StoneSummary blackSummary,
            StoneSummary whiteSummary)
        {
            if (blackSummary.Count < 2 || whiteSummary.Count < 2)
                return LastMoveInference.None;

            double blackOffset = CalculateDeviation(blackSummary);
            double whiteOffset = CalculateDeviation(whiteSummary);
            BoardCoordinate candidate = blackOffset >= whiteOffset
                ? blackSummary.MinCoordinate
                : whiteSummary.MinCoordinate;

            return PromoteLastMove(
                boardState,
                boardWidth,
                candidate,
                blackSummary,
                whiteSummary,
                LastMoveSource.Deviation);
        }

        private static double CalculateDeviation(StoneSummary summary)
        {
            if (summary.Count <= 1)
                return 0d;

            double average = (summary.TotalPercent - summary.MinPercent) / (double)(summary.Count - 1);
            return Math.Abs(summary.MinPercent - average);
        }

        private static LastMoveInference TryApplyStoneCountLastMove(
            BoardCellState[] boardState,
            int boardWidth,
            StoneSummary blackSummary,
            StoneSummary whiteSummary)
        {
            if (blackSummary.Count <= 0 || whiteSummary.Count <= 0)
                return LastMoveInference.None;

            if (blackSummary.Count > whiteSummary.Count)
                return PromoteLastMove(
                    boardState,
                    boardWidth,
                    blackSummary.MinCoordinate,
                    blackSummary,
                    whiteSummary,
                    LastMoveSource.StoneCount);
            if (whiteSummary.Count > blackSummary.Count)
                return PromoteLastMove(
                    boardState,
                    boardWidth,
                    whiteSummary.MinCoordinate,
                    blackSummary,
                    whiteSummary,
                    LastMoveSource.StoneCount);
            return LastMoveInference.None;
        }

        private static LastMoveInference PromoteLastMove(
            BoardCellState[] boardState,
            int boardWidth,
            BoardCoordinate candidate,
            StoneSummary blackSummary,
            StoneSummary whiteSummary,
            LastMoveSource source)
        {
            if (candidate == null)
                return LastMoveInference.None;

            int index = (candidate.Y * boardWidth) + candidate.X;
            if (index < 0 || index >= boardState.Length)
                return LastMoveInference.None;

            if (boardState[index] == blackSummary.NormalState)
            {
                boardState[index] = blackSummary.LastMoveState;
                return new LastMoveInference(new BoardCoordinate(candidate.X, candidate.Y), source);
            }

            if (boardState[index] == whiteSummary.NormalState)
            {
                boardState[index] = whiteSummary.LastMoveState;
                return new LastMoveInference(new BoardCoordinate(candidate.X, candidate.Y), source);
            }

            return LastMoveInference.None;
        }
    }

    internal sealed class FoxCornerFlipSummary
    {
        public static FoxCornerFlipSummary Empty { get; } = new FoxCornerFlipSummary();

        public bool TryGetUniqueCandidate(out BoardCoordinate candidate)
        {
            candidate = null;
            return false;
        }
    }
}
