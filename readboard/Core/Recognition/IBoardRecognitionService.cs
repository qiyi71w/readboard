using System;
using System.Collections.Generic;
using System.Text;

namespace readboard
{
    internal interface IBoardRecognitionService
    {
        BoardRecognitionResult Recognize(BoardRecognitionRequest request);
    }

    internal sealed class LegacyBoardRecognitionService : IBoardRecognitionService
    {
        private const int EmptyStoneValue = 0;
        private const int BlackStoneValue = 1;
        private const int WhiteStoneValue = 2;
        private const int BlackLastMoveValue = 3;
        private const int WhiteLastMoveValue = 4;
        private const int PureWhiteOffset = 30;
        private const int AlmostWhiteOffset = 65;
        private const int WhiteHeuristicGap = 10;
        private const int StrongColorMaxValue = 50;
        private const int StrongColorMinValue = 150;
        private const int OneQuarterDivisor = 4;
        private const int SampleWidthNumerator = 2;
        private const int SampleWidthDenominator = 6;
        private RecognitionCacheEntry previousRecognition;

        public BoardRecognitionResult Recognize(BoardRecognitionRequest request)
        {
            if (request == null || request.Frame == null)
                return BoardRecognitionResult.CreateFailure(BoardRecognitionFailureKind.MissingFrame, "Board frame is required.");

            if (!HasValidBoardSize(request.Frame.BoardSize))
                return BoardRecognitionResult.CreateFailure(BoardRecognitionFailureKind.InvalidBoardArea, "Board size is required.");

            RecognitionThresholds thresholds = RecognitionThresholds.GetEffective(request.Thresholds, request.Frame.SyncMode);
            BoardRecognitionResult cachedResult = TryReuseCachedRecognition(request, thresholds);
            if (cachedResult != null)
            {
                CacheRecognition(request, thresholds, cachedResult.Snapshot, cachedResult.Viewport);
                return cachedResult;
            }

            LegacyPixelMap pixels;
            BoardRecognitionFailureKind failureKind;
            string failureReason;
            if (!LegacyPixelMap.TryCreate(request.Frame, out pixels, out failureKind, out failureReason))
                return BoardRecognitionResult.CreateFailure(failureKind, failureReason);

            BoardViewport viewport;
            if (!LegacyBoardLocator.TryResolveViewport(request, pixels, out viewport, out failureReason))
                return BoardRecognitionResult.CreateFailure(BoardRecognitionFailureKind.InvalidBoardArea, failureReason);

            BoardSnapshot snapshot = AnalyzeBoard(request, pixels, viewport, thresholds);
            ReuseSnapshotBackingIfUnchanged(snapshot);
            snapshot.NeedsPrintWindowFallback = ShouldRequestPrintWindowFallback(request.Frame, snapshot);
            BoardRecognitionResult result = CreateSuccessResult(snapshot, viewport, false);
            CacheRecognition(request, thresholds, snapshot, viewport);
            return result;
        }

        private BoardRecognitionResult TryReuseCachedRecognition(BoardRecognitionRequest request, RecognitionThresholds thresholds)
        {
            RecognitionCacheEntry cache = previousRecognition;
            if (cache == null || !cache.Matches(request, thresholds))
                return null;

            BoardSnapshot snapshot = CloneCachedSnapshot(cache.Snapshot, true);
            snapshot.NeedsPrintWindowFallback = ShouldRequestPrintWindowFallback(request.Frame, snapshot);
            return CreateSuccessResult(snapshot, MergeViewport(cache.Viewport, request.Frame.Viewport), true);
        }

        private void ReuseSnapshotBackingIfUnchanged(BoardSnapshot snapshot)
        {
            BoardSnapshot previousSnapshot = previousRecognition == null ? null : previousRecognition.Snapshot;
            if (!CanReuseSnapshotBacking(snapshot, previousSnapshot))
                return;

            snapshot.BoardState = previousSnapshot.BoardState;
            snapshot.BlackStoneCount = previousSnapshot.BlackStoneCount;
            snapshot.WhiteStoneCount = previousSnapshot.WhiteStoneCount;
            snapshot.LastMove = previousSnapshot.LastMove;
            snapshot.Payload = previousSnapshot.Payload;
            snapshot.ProtocolLines = previousSnapshot.ProtocolLines;
            snapshot.StateSignature = previousSnapshot.StateSignature;
            snapshot.IsUnchangedFromPrevious = true;
            snapshot.ReusedPayload = true;
        }

        private static bool CanReuseSnapshotBacking(BoardSnapshot snapshot, BoardSnapshot previousSnapshot)
        {
            if (snapshot == null || previousSnapshot == null)
                return false;
            if (snapshot.Width != previousSnapshot.Width || snapshot.Height != previousSnapshot.Height)
                return false;
            if (snapshot.StateSignature != previousSnapshot.StateSignature)
                return false;

            return AreBoardStatesEqual(snapshot.BoardState, previousSnapshot.BoardState);
        }

        private void CacheRecognition(
            BoardRecognitionRequest request,
            RecognitionThresholds thresholds,
            BoardSnapshot snapshot,
            BoardViewport viewport)
        {
            previousRecognition = RecognitionCacheEntry.Create(request, thresholds, snapshot, viewport);
        }

        private static BoardRecognitionResult CreateSuccessResult(BoardSnapshot snapshot, BoardViewport viewport, bool usedCachedSnapshot)
        {
            return new BoardRecognitionResult
            {
                Success = true,
                Snapshot = snapshot,
                Viewport = viewport,
                FailureKind = BoardRecognitionFailureKind.None,
                UsedCachedSnapshot = usedCachedSnapshot
            };
        }

        private static BoardSnapshot CloneCachedSnapshot(BoardSnapshot snapshot, bool unchanged)
        {
            if (snapshot == null)
                return null;

            return new BoardSnapshot
            {
                Width = snapshot.Width,
                Height = snapshot.Height,
                BoardState = snapshot.BoardState,
                IsValid = snapshot.IsValid,
                IsAllBlack = snapshot.IsAllBlack,
                IsAllWhite = snapshot.IsAllWhite,
                BlackStoneCount = snapshot.BlackStoneCount,
                WhiteStoneCount = snapshot.WhiteStoneCount,
                LastMove = snapshot.LastMove,
                NeedsPrintWindowFallback = snapshot.NeedsPrintWindowFallback,
                Payload = snapshot.Payload,
                ProtocolLines = snapshot.ProtocolLines,
                StateSignature = snapshot.StateSignature,
                IsUnchangedFromPrevious = unchanged,
                ReusedPayload = unchanged || snapshot.ReusedPayload
            };
        }

        private static BoardViewport MergeViewport(BoardViewport cachedViewport, BoardViewport currentViewport)
        {
            if (cachedViewport == null && currentViewport == null)
                return null;

            BoardViewport merged = CloneViewport(cachedViewport) ?? new BoardViewport();
            PixelRect currentSourceBounds = GetSourceBounds(currentViewport);
            if (currentSourceBounds != null)
                merged.SourceBounds = currentSourceBounds;
            if (currentViewport != null)
                merged.ScreenBounds = CloneRect(currentViewport.ScreenBounds);
            if (merged.ScreenBounds == null && cachedViewport != null)
                merged.ScreenBounds = CloneRect(cachedViewport.ScreenBounds);
            if (currentViewport != null && currentViewport.CellWidth > 0d)
                merged.CellWidth = currentViewport.CellWidth;
            if (currentViewport != null && currentViewport.CellHeight > 0d)
                merged.CellHeight = currentViewport.CellHeight;
            return merged;
        }

        private static BoardViewport CloneViewport(BoardViewport viewport)
        {
            if (viewport == null)
                return null;

            return new BoardViewport
            {
                SourceBounds = CloneRect(viewport.SourceBounds),
                ScreenBounds = CloneRect(viewport.ScreenBounds),
                CellWidth = viewport.CellWidth,
                CellHeight = viewport.CellHeight
            };
        }

        private static PixelRect CloneRect(PixelRect rect)
        {
            if (rect == null)
                return null;

            return new PixelRect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private static PixelRect GetSourceBounds(BoardViewport viewport)
        {
            return viewport == null ? null : CloneRect(viewport.SourceBounds);
        }

        private static bool AreBoardStatesEqual(BoardCellState[] left, BoardCellState[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        private static bool TryResolveFrameSignature(BoardFrame frame, out ulong contentSignature, out int width, out int height, out int stride)
        {
            contentSignature = 0UL;
            width = 0;
            height = 0;
            stride = 0;
            if (frame == null || frame.PixelBuffer == null)
                return false;

            PixelBuffer pixelBuffer = frame.PixelBuffer;
            if (frame.ContentSignature == 0UL)
                frame.ContentSignature = BoardContentHash.Compute(pixelBuffer);

            contentSignature = frame.ContentSignature;
            width = pixelBuffer.Width;
            height = pixelBuffer.Height;
            stride = pixelBuffer.Stride;
            return contentSignature != 0UL && width > 0 && height > 0 && stride > 0;
        }

        private static bool AreRectsEqual(PixelRect left, PixelRect right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;

            return left.X == right.X
                && left.Y == right.Y
                && left.Width == right.Width
                && left.Height == right.Height;
        }

        private static bool AreThresholdsEqual(RecognitionThresholds left, RecognitionThresholds right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;

            return left.BlackPercent == right.BlackPercent
                && left.WhitePercent == right.WhitePercent
                && left.BlackOffset == right.BlackOffset
                && left.WhiteOffset == right.WhiteOffset
                && left.GrayOffset == right.GrayOffset
                && left.RedBlueMarkerThreshold == right.RedBlueMarkerThreshold;
        }

        private static bool HasValidBoardSize(BoardDimensions boardSize)
        {
            return boardSize != null && boardSize.Width > 0 && boardSize.Height > 0;
        }

        private static BoardSnapshot AnalyzeBoard(
            BoardRecognitionRequest request,
            LegacyPixelMap pixels,
            BoardViewport viewport,
            RecognitionThresholds thresholds)
        {
            PixelRect sourceBounds = viewport.SourceBounds;
            BoardDimensions boardSize = request.Frame.BoardSize;
            double cellWidth = sourceBounds.Width / (double)boardSize.Width;
            double cellHeight = sourceBounds.Height / (double)boardSize.Height;
            int cellWidthInt = Math.Max(1, (int)Math.Round(cellWidth));
            int cellHeightInt = Math.Max(1, (int)Math.Round(cellHeight));
            LegacyBoardAnalysis analysis = CreateAnalysis(
                pixels,
                sourceBounds,
                boardSize,
                thresholds,
                request.InferLastMove,
                cellWidth,
                cellHeight,
                cellWidthInt,
                cellHeightInt);

            return BuildSnapshot(boardSize, analysis);
        }

        private static LegacyBoardAnalysis CreateAnalysis(
            LegacyPixelMap pixels,
            PixelRect sourceBounds,
            BoardDimensions boardSize,
            RecognitionThresholds thresholds,
            bool inferLastMove,
            double cellWidth,
            double cellHeight,
            int cellWidthInt,
            int cellHeightInt)
        {
            BoardCellState[] boardState = new BoardCellState[boardSize.Width * boardSize.Height];
            StoneSummary blackSummary = new StoneSummary(BoardCellState.Black, BoardCellState.BlackLastMove);
            StoneSummary whiteSummary = new StoneSummary(BoardCellState.White, BoardCellState.WhiteLastMove);
            MarkerSummary markerSummary = new MarkerSummary();

            for (int y = 0; y < boardSize.Height; y++)
            {
                AnalyzeRow(
                    pixels,
                    sourceBounds,
                    boardSize.Width,
                    y,
                    thresholds,
                    boardState,
                    blackSummary,
                    whiteSummary,
                    markerSummary,
                    inferLastMove,
                    cellWidth,
                    cellHeight,
                    cellWidthInt,
                    cellHeightInt);
            }

            BoardCoordinate lastMove = inferLastMove
                ? ApplyLastMoveInference(boardState, boardSize.Width, blackSummary, whiteSummary, markerSummary)
                : null;

            return new LegacyBoardAnalysis
            {
                BoardState = boardState,
                BlackStoneCount = blackSummary.Count,
                WhiteStoneCount = whiteSummary.Count,
                LastMove = lastMove
            };
        }

        private static void AnalyzeRow(
            LegacyPixelMap pixels,
            PixelRect sourceBounds,
            int boardWidth,
            int y,
            RecognitionThresholds thresholds,
            BoardCellState[] boardState,
            StoneSummary blackSummary,
            StoneSummary whiteSummary,
            MarkerSummary markerSummary,
            bool inferLastMove,
            double cellWidth,
            double cellHeight,
            int cellWidthInt,
            int cellHeightInt)
        {
            for (int x = 0; x < boardWidth; x++)
            {
                int regionStartX = sourceBounds.X + (int)Math.Round(x * cellWidth);
                int regionStartY = sourceBounds.Y + (int)Math.Round(y * cellHeight);
                RegionMetrics metrics = AnalyzeRegion(
                    pixels,
                    regionStartX,
                    regionStartY,
                    cellWidthInt,
                    cellHeightInt,
                    thresholds,
                    sourceBounds);
                BoardCellState state = DetermineCellState(metrics, thresholds);
                int index = (y * boardWidth) + x;
                boardState[index] = state;
                ObserveStone(state, metrics, x, y, blackSummary, whiteSummary);
                if (inferLastMove && state != BoardCellState.Empty)
                    markerSummary.Observe(metrics.RedPercent, metrics.BluePercent, thresholds.RedBlueMarkerThreshold, x, y);
            }
        }

        private static RegionMetrics AnalyzeRegion(
            LegacyPixelMap pixels,
            int startX,
            int startY,
            int regionWidth,
            int regionHeight,
            RecognitionThresholds thresholds,
            PixelRect sourceBounds)
        {
            int boundedStartX = ClampRegionStart(startX, regionWidth, sourceBounds.X, sourceBounds.Width);
            int boundedStartY = ClampRegionStart(startY, regionHeight, sourceBounds.Y, sourceBounds.Height);
            int pixelCount = regionWidth * regionHeight;
            int pureWhiteValue = 255 - PureWhiteOffset;
            int whiteValue = 255 - thresholds.WhiteOffset;
            int almostWhiteValue = 255 - AlmostWhiteOffset;
            int sampleY = (int)Math.Round(regionHeight / (double)OneQuarterDivisor);
            int sampleWidth = (int)Math.Round(regionWidth * (double)SampleWidthNumerator / SampleWidthDenominator);
            int blackCount = 0;
            int whiteCount = 0;
            int pureWhiteCount = 0;
            int almostWhiteCount = 0;
            int redCount = 0;
            int blueCount = 0;
            bool hasTrueWhiteEvidence = false;

            for (int y = 0; y < regionHeight; y++)
            {
                for (int x = 0; x < regionWidth; x++)
                {
                    LegacyRgbInfo rgb = pixels.GetPixel(boundedStartX + x, boundedStartY + y);
                    CountRegionMetrics(
                        thresholds,
                        pureWhiteValue,
                        whiteValue,
                        almostWhiteValue,
                        sampleY,
                        sampleWidth,
                        x,
                        y,
                        rgb,
                        ref blackCount,
                        ref whiteCount,
                        ref pureWhiteCount,
                        ref almostWhiteCount,
                        ref redCount,
                        ref blueCount,
                        ref hasTrueWhiteEvidence);
                }
            }

            return new RegionMetrics(
                (100 * blackCount) / pixelCount,
                (100 * whiteCount) / pixelCount,
                (100 * pureWhiteCount) / pixelCount,
                (100 * almostWhiteCount) / pixelCount,
                (100 * redCount) / pixelCount,
                (100 * blueCount) / pixelCount,
                hasTrueWhiteEvidence);
        }

        private static void CountRegionMetrics(
            RecognitionThresholds thresholds,
            int pureWhiteValue,
            int whiteValue,
            int almostWhiteValue,
            int sampleY,
            int sampleWidth,
            int x,
            int y,
            LegacyRgbInfo rgb,
            ref int blackCount,
            ref int whiteCount,
            ref int pureWhiteCount,
            ref int almostWhiteCount,
            ref int redCount,
            ref int blueCount,
            ref bool hasTrueWhiteEvidence)
        {
            bool isGray = Math.Abs(rgb.Red - rgb.Green) < thresholds.GrayOffset
                && Math.Abs(rgb.Green - rgb.Blue) < thresholds.GrayOffset
                && Math.Abs(rgb.Blue - rgb.Red) < thresholds.GrayOffset;
            if (isGray)
                CountGrayMetrics(rgb, thresholds.BlackOffset, whiteValue, pureWhiteValue, almostWhiteValue, ref blackCount, ref whiteCount, ref pureWhiteCount, ref almostWhiteCount);

            if (rgb.Red <= StrongColorMaxValue && rgb.Green <= StrongColorMaxValue && rgb.Blue >= StrongColorMinValue)
                blueCount++;
            if (rgb.Red >= StrongColorMinValue && rgb.Green <= StrongColorMaxValue && rgb.Blue <= StrongColorMaxValue)
                redCount++;

            bool sampleCandidate = !hasTrueWhiteEvidence && y == sampleY && x < sampleWidth;
            if (sampleCandidate && (rgb.Red < pureWhiteValue || rgb.Green < pureWhiteValue || rgb.Blue < pureWhiteValue))
                hasTrueWhiteEvidence = true;
        }

        private static void CountGrayMetrics(
            LegacyRgbInfo rgb,
            int blackOffset,
            int whiteValue,
            int pureWhiteValue,
            int almostWhiteValue,
            ref int blackCount,
            ref int whiteCount,
            ref int pureWhiteCount,
            ref int almostWhiteCount)
        {
            if (rgb.Red <= blackOffset && rgb.Green <= blackOffset && rgb.Blue <= blackOffset)
                blackCount++;
            if (rgb.Red >= whiteValue && rgb.Green >= whiteValue && rgb.Blue >= whiteValue)
                whiteCount++;
            if (rgb.Red >= pureWhiteValue && rgb.Green >= pureWhiteValue && rgb.Blue >= pureWhiteValue)
                pureWhiteCount++;
            if (rgb.Red >= almostWhiteValue && rgb.Green >= almostWhiteValue && rgb.Blue >= almostWhiteValue)
                almostWhiteCount++;
        }

        private static BoardCellState DetermineCellState(RegionMetrics metrics, RecognitionThresholds thresholds)
        {
            if (metrics.BlackPercent >= thresholds.BlackPercent)
                return BoardCellState.Black;
            if (IsWhiteStone(metrics, thresholds.WhitePercent))
                return BoardCellState.White;
            return BoardCellState.Empty;
        }

        private static bool IsWhiteStone(RegionMetrics metrics, int whitePercentThreshold)
        {
            if (metrics.WhitePercent < whitePercentThreshold)
                return false;
            if (!metrics.HasTrueWhiteEvidence && metrics.AlmostWhitePercent - metrics.PureWhitePercent < WhiteHeuristicGap)
                return false;
            return true;
        }

        private static void ObserveStone(
            BoardCellState state,
            RegionMetrics metrics,
            int x,
            int y,
            StoneSummary blackSummary,
            StoneSummary whiteSummary)
        {
            if (state == BoardCellState.Black)
            {
                blackSummary.Observe(metrics.BlackPercent, x, y);
                return;
            }

            if (state == BoardCellState.White)
                whiteSummary.Observe(metrics.WhitePercent, x, y);
        }

        private static BoardCoordinate ApplyLastMoveInference(
            BoardCellState[] boardState,
            int boardWidth,
            StoneSummary blackSummary,
            StoneSummary whiteSummary,
            MarkerSummary markerSummary)
        {
            BoardCoordinate lastMove = TryApplyMarkerLastMove(boardState, boardWidth, blackSummary, whiteSummary, markerSummary);
            if (lastMove != null)
                return lastMove;

            lastMove = TryApplyDeviationLastMove(boardState, boardWidth, blackSummary, whiteSummary);
            if (lastMove != null)
                return lastMove;

            return TryApplyStoneCountLastMove(boardState, boardWidth, blackSummary, whiteSummary);
        }

        private static BoardCoordinate TryApplyMarkerLastMove(
            BoardCellState[] boardState,
            int boardWidth,
            StoneSummary blackSummary,
            StoneSummary whiteSummary,
            MarkerSummary markerSummary)
        {
            bool redOnly = markerSummary.RedCount == 1 && markerSummary.BlueCount != 1;
            bool blueOnly = markerSummary.RedCount != 1 && markerSummary.BlueCount == 1;
            if (!redOnly && !blueOnly)
                return null;

            return PromoteLastMove(boardState, boardWidth, markerSummary.Candidate, blackSummary, whiteSummary);
        }

        private static BoardCoordinate TryApplyDeviationLastMove(
            BoardCellState[] boardState,
            int boardWidth,
            StoneSummary blackSummary,
            StoneSummary whiteSummary)
        {
            if (blackSummary.Count < 2 || whiteSummary.Count < 2)
                return null;

            double blackOffset = CalculateDeviation(blackSummary);
            double whiteOffset = CalculateDeviation(whiteSummary);
            BoardCoordinate candidate = blackOffset >= whiteOffset
                ? blackSummary.MinCoordinate
                : whiteSummary.MinCoordinate;

            return PromoteLastMove(boardState, boardWidth, candidate, blackSummary, whiteSummary);
        }

        private static double CalculateDeviation(StoneSummary summary)
        {
            if (summary.Count <= 1)
                return 0d;

            double average = (summary.TotalPercent - summary.MinPercent) / (double)(summary.Count - 1);
            return Math.Abs(summary.MinPercent - average);
        }

        private static BoardCoordinate TryApplyStoneCountLastMove(
            BoardCellState[] boardState,
            int boardWidth,
            StoneSummary blackSummary,
            StoneSummary whiteSummary)
        {
            if (blackSummary.Count <= 0 || whiteSummary.Count <= 0)
                return null;

            if (blackSummary.Count > whiteSummary.Count)
                return PromoteLastMove(boardState, boardWidth, blackSummary.MinCoordinate, blackSummary, whiteSummary);
            if (whiteSummary.Count > blackSummary.Count)
                return PromoteLastMove(boardState, boardWidth, whiteSummary.MinCoordinate, blackSummary, whiteSummary);
            return null;
        }

        private static BoardCoordinate PromoteLastMove(
            BoardCellState[] boardState,
            int boardWidth,
            BoardCoordinate candidate,
            StoneSummary blackSummary,
            StoneSummary whiteSummary)
        {
            if (candidate == null)
                return null;

            int index = (candidate.Y * boardWidth) + candidate.X;
            if (index < 0 || index >= boardState.Length)
                return null;

            if (boardState[index] == blackSummary.NormalState)
            {
                boardState[index] = blackSummary.LastMoveState;
                return new BoardCoordinate(candidate.X, candidate.Y);
            }

            if (boardState[index] == whiteSummary.NormalState)
            {
                boardState[index] = whiteSummary.LastMoveState;
                return new BoardCoordinate(candidate.X, candidate.Y);
            }

            return null;
        }

        private static BoardSnapshot BuildSnapshot(BoardDimensions boardSize, LegacyBoardAnalysis analysis)
        {
            List<string> protocolLines = new List<string>(boardSize.Height);
            StringBuilder payloadBuilder = new StringBuilder(boardSize.Width * boardSize.Height * 2);
            bool isAllBlack = true;
            bool isAllWhite = true;
            ulong stateSignature = BoardContentHash.Start();

            for (int y = 0; y < boardSize.Height; y++)
            {
                string line = BuildProtocolLine(boardSize.Width, y, analysis.BoardState, ref isAllBlack, ref isAllWhite, ref stateSignature);
                protocolLines.Add("re=" + line);
                payloadBuilder.Append(line);
                payloadBuilder.Append('\n');
            }

            stateSignature = BoardContentHash.Finish(stateSignature, boardSize.Width, boardSize.Height, analysis.BoardState.Length);

            return new BoardSnapshot
            {
                Width = boardSize.Width,
                Height = boardSize.Height,
                BoardState = analysis.BoardState,
                IsValid = !isAllBlack && !isAllWhite,
                IsAllBlack = isAllBlack,
                IsAllWhite = isAllWhite,
                BlackStoneCount = analysis.BlackStoneCount,
                WhiteStoneCount = analysis.WhiteStoneCount,
                LastMove = analysis.LastMove,
                Payload = payloadBuilder.ToString(),
                ProtocolLines = protocolLines,
                StateSignature = stateSignature
            };
        }

        private static string BuildProtocolLine(
            int boardWidth,
            int y,
            IList<BoardCellState> boardState,
            ref bool isAllBlack,
            ref bool isAllWhite,
            ref ulong stateSignature)
        {
            StringBuilder lineBuilder = new StringBuilder(boardWidth * 2);
            for (int x = 0; x < boardWidth; x++)
            {
                BoardCellState state = boardState[(y * boardWidth) + x];
                UpdateBoardValidityFlags(state, ref isAllBlack, ref isAllWhite);
                if (x > 0)
                    lineBuilder.Append(',');

                int encodedState = EncodeCellState(state);
                lineBuilder.Append(encodedState);
                stateSignature = BoardContentHash.AppendInt(stateSignature, encodedState);
            }

            return lineBuilder.ToString();
        }

        private static void UpdateBoardValidityFlags(BoardCellState state, ref bool isAllBlack, ref bool isAllWhite)
        {
            if (state == BoardCellState.Empty)
            {
                isAllBlack = false;
                isAllWhite = false;
                return;
            }

            if (state == BoardCellState.White || state == BoardCellState.WhiteLastMove)
                isAllBlack = false;
            if (state == BoardCellState.Black || state == BoardCellState.BlackLastMove)
                isAllWhite = false;
        }

        private static bool ShouldRequestPrintWindowFallback(BoardFrame frame, BoardSnapshot snapshot)
        {
            if (frame == null || snapshot == null || !snapshot.IsAllBlack)
                return false;

            return frame.PreferPrintWindow && !frame.UsedPrintWindow;
        }

        private static int EncodeCellState(BoardCellState state)
        {
            switch (state)
            {
                case BoardCellState.Black:
                    return BlackStoneValue;
                case BoardCellState.White:
                    return WhiteStoneValue;
                case BoardCellState.BlackLastMove:
                    return BlackLastMoveValue;
                case BoardCellState.WhiteLastMove:
                    return WhiteLastMoveValue;
                default:
                    return EmptyStoneValue;
            }
        }

        private static int ClampRegionStart(int start, int size, int min, int span)
        {
            int maxStart = min + span - size;
            if (start < min)
                return min;
            if (start > maxStart)
                return maxStart;
            return start;
        }

        private sealed class RecognitionCacheEntry
        {
            public ulong FrameContentSignature { get; set; }
            public int FrameWidth { get; set; }
            public int FrameHeight { get; set; }
            public int FrameStride { get; set; }
            public SyncMode SyncMode { get; set; }
            public int BoardWidth { get; set; }
            public int BoardHeight { get; set; }
            public bool InferLastMove { get; set; }
            public PixelRect RequestedSourceBounds { get; set; }
            public RecognitionThresholds Thresholds { get; set; }
            public BoardSnapshot Snapshot { get; set; }
            public BoardViewport Viewport { get; set; }

            public bool Matches(BoardRecognitionRequest request, RecognitionThresholds thresholds)
            {
                ulong signature;
                int width;
                int height;
                int stride;
                if (!TryResolveFrameSignature(request.Frame, out signature, out width, out height, out stride))
                    return false;
                if (signature != FrameContentSignature || width != FrameWidth || height != FrameHeight || stride != FrameStride)
                    return false;
                if (request.Frame.SyncMode != SyncMode || request.InferLastMove != InferLastMove)
                    return false;
                if (request.Frame.BoardSize.Width != BoardWidth || request.Frame.BoardSize.Height != BoardHeight)
                    return false;
                if (!AreRectsEqual(GetSourceBounds(request.Frame.Viewport), RequestedSourceBounds))
                    return false;

                return AreThresholdsEqual(thresholds, Thresholds);
            }

            public static RecognitionCacheEntry Create(
                BoardRecognitionRequest request,
                RecognitionThresholds thresholds,
                BoardSnapshot snapshot,
                BoardViewport viewport)
            {
                ulong signature;
                int width;
                int height;
                int stride;
                TryResolveFrameSignature(request.Frame, out signature, out width, out height, out stride);
                return new RecognitionCacheEntry
                {
                    FrameContentSignature = signature,
                    FrameWidth = width,
                    FrameHeight = height,
                    FrameStride = stride,
                    SyncMode = request.Frame.SyncMode,
                    BoardWidth = request.Frame.BoardSize.Width,
                    BoardHeight = request.Frame.BoardSize.Height,
                    InferLastMove = request.InferLastMove,
                    RequestedSourceBounds = GetSourceBounds(request.Frame.Viewport),
                    Thresholds = thresholds == null ? null : thresholds.Clone(),
                    Snapshot = CloneCachedSnapshot(snapshot, snapshot != null && snapshot.IsUnchangedFromPrevious),
                    Viewport = CloneViewport(viewport)
                };
            }
        }
    }
}
