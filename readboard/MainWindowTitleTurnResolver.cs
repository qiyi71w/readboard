namespace readboard
{
    internal static class MainWindowTitleTurnResolver
    {
        public static MainWindowTitleTurn Resolve(BoardSnapshot snapshot)
        {
            if (snapshot == null || snapshot.BoardState == null)
                return MainWindowTitleTurn.Unknown;

            int blackLastMoveCount = 0;
            int whiteLastMoveCount = 0;
            for (int i = 0; i < snapshot.BoardState.Length; i++)
            {
                if (snapshot.BoardState[i] == BoardCellState.BlackLastMove)
                    blackLastMoveCount++;
                else if (snapshot.BoardState[i] == BoardCellState.WhiteLastMove)
                    whiteLastMoveCount++;
            }

            if (blackLastMoveCount == 1 && whiteLastMoveCount == 0)
                return MainWindowTitleTurn.White;
            if (whiteLastMoveCount == 1 && blackLastMoveCount == 0)
                return MainWindowTitleTurn.Black;
            return MainWindowTitleTurn.Unknown;
        }
    }
}
