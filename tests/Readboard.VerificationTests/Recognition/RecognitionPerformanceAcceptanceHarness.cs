using System;
using System.Diagnostics;
using readboard;

namespace Readboard.VerificationTests.Recognition
{
    internal static class RecognitionPerformanceAcceptanceHarness
    {
        internal const double MinimumLatencyReductionRatio = 0.25d;
        internal const double MinimumAllocationReductionRatio = 0.25d;
        private const int WarmupPassCount = 8;
        private const int MeasurementPassCount = 1500;

        public static RecognitionPerformanceAcceptanceReport MeasureDefaultAcceptance()
        {
            return Measure(CreateDefaultScenario());
        }

        public static RecognitionReplayScenario CreateDefaultScenario()
        {
            ReplayFixture fixture = ReplayFixtureCatalog.LoadForeground5x5();
            return RecognitionReplayScenario.Create(
                "replay-cache-vs-equivalent-uncached-baseline",
                CreateDefaultSteps(fixture));
        }

        private static RecognitionReplayStep[] CreateDefaultSteps(ReplayFixture fixture)
        {
            return new[]
            {
                CreateStep(fixture, ReplayVariant.Base, "base-1"),
                CreateStep(fixture, ReplayVariant.Base, "base-2"),
                CreateStep(fixture, ReplayVariant.Base, "base-3"),
                CreateStep(fixture, ReplayVariant.Base, "base-4"),
                CreateStep(fixture, ReplayVariant.Changed, "changed-1"),
                CreateStep(fixture, ReplayVariant.Changed, "changed-2"),
                CreateStep(fixture, ReplayVariant.Changed, "changed-3"),
                CreateStep(fixture, ReplayVariant.Base, "base-5"),
                CreateStep(fixture, ReplayVariant.Base, "base-6"),
                CreateStep(fixture, ReplayVariant.Base, "base-7"),
                CreateStep(fixture, ReplayVariant.Changed, "changed-4"),
                CreateStep(fixture, ReplayVariant.Changed, "changed-5")
            };
        }

        private static RecognitionReplayStep CreateStep(
            ReplayFixture fixture,
            ReplayVariant variant,
            string label)
        {
            return RecognitionReplayStep.Create(
                label,
                fixture.CreateRecognitionRequest(variant, inferLastMove: false),
                variant == ReplayVariant.Base ? fixture.BaseProtocolLines : fixture.ChangedProtocolLines);
        }

        public static RecognitionPerformanceAcceptanceReport Measure(RecognitionReplayScenario scenario)
        {
            WarmUpScenario(scenario, useEquivalentUncachedBaseline: false);
            WarmUpScenario(scenario, useEquivalentUncachedBaseline: true);

            RecognitionMeasurement cached = MeasureVariant(scenario, useEquivalentUncachedBaseline: false);
            RecognitionMeasurement baseline = MeasureVariant(scenario, useEquivalentUncachedBaseline: true);
            RecognitionParityReport parity = MeasureParity(scenario);
            return RecognitionPerformanceAcceptanceReport.Create(scenario, cached, baseline, parity);
        }

        private static void WarmUpScenario(RecognitionReplayScenario scenario, bool useEquivalentUncachedBaseline)
        {
            for (int index = 0; index < WarmupPassCount; index++)
                MeasurePass(scenario, useEquivalentUncachedBaseline);
        }

        private static RecognitionMeasurement MeasureVariant(
            RecognitionReplayScenario scenario,
            bool useEquivalentUncachedBaseline)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            Stopwatch stopwatch = Stopwatch.StartNew();
            RecognitionMetricCounters counters = new RecognitionMetricCounters();
            for (int index = 0; index < MeasurementPassCount; index++)
                counters.Accumulate(MeasurePass(scenario, useEquivalentUncachedBaseline));

            stopwatch.Stop();
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            return RecognitionMeasurement.Create(counters, stopwatch.Elapsed, allocatedBytes);
        }

        private static RecognitionMetricCounters MeasurePass(
            RecognitionReplayScenario scenario,
            bool useEquivalentUncachedBaseline)
        {
            RecognitionMetricCounters counters = new RecognitionMetricCounters();
            if (useEquivalentUncachedBaseline)
            {
                for (int index = 0; index < scenario.Steps.Length; index++)
                    ObserveResult(counters, new LegacyBoardRecognitionService().Recognize(scenario.Steps[index].Request));
                return counters;
            }

            LegacyBoardRecognitionService service = new LegacyBoardRecognitionService();
            for (int index = 0; index < scenario.Steps.Length; index++)
                ObserveResult(counters, service.Recognize(scenario.Steps[index].Request));
            return counters;
        }

        private static void ObserveResult(RecognitionMetricCounters counters, BoardRecognitionResult result)
        {
            counters.TotalRecognitions++;
            if (result.UsedCachedSnapshot)
                counters.CachedSnapshotCount++;
            if (result.Snapshot != null && result.Snapshot.IsUnchangedFromPrevious)
                counters.UnchangedSnapshotCount++;
            if (result.Snapshot != null && result.Snapshot.ReusedPayload)
                counters.ReusedPayloadCount++;
        }

        private static RecognitionParityReport MeasureParity(RecognitionReplayScenario scenario)
        {
            LegacyBoardRecognitionService cachedService = new LegacyBoardRecognitionService();
            RecognitionParityReport report = RecognitionParityReport.Create();

            for (int index = 0; index < scenario.Steps.Length; index++)
            {
                RecognitionReplayStep step = scenario.Steps[index];
                BoardRecognitionResult cached = cachedService.Recognize(step.Request);
                BoardRecognitionResult baseline = new LegacyBoardRecognitionService().Recognize(step.Request);
                report.Observe(step, cached, baseline, AreResultsEquivalent(cached, baseline));
            }

            return report;
        }

        private static bool AreResultsEquivalent(BoardRecognitionResult left, BoardRecognitionResult right)
        {
            if (left == null || right == null)
                return left == right;

            return left.Success == right.Success
                && left.FailureKind == right.FailureKind
                && string.Equals(left.FailureReason, right.FailureReason, StringComparison.Ordinal)
                && AreSnapshotsEquivalent(left.Snapshot, right.Snapshot)
                && AreViewportsEquivalent(left.Viewport, right.Viewport);
        }

        private static bool AreSnapshotsEquivalent(BoardSnapshot left, BoardSnapshot right)
        {
            if (left == null || right == null)
                return left == right;

            return left.Width == right.Width
                && left.Height == right.Height
                && left.IsValid == right.IsValid
                && left.IsAllBlack == right.IsAllBlack
                && left.IsAllWhite == right.IsAllWhite
                && left.BlackStoneCount == right.BlackStoneCount
                && left.WhiteStoneCount == right.WhiteStoneCount
                && AreCoordinatesEquivalent(left.LastMove, right.LastMove)
                && string.Equals(left.Payload, right.Payload, StringComparison.Ordinal)
                && AreBoardStatesEquivalent(left.BoardState, right.BoardState)
                && AreProtocolLinesEquivalent(left.ProtocolLines, right.ProtocolLines);
        }

        private static bool AreCoordinatesEquivalent(BoardCoordinate left, BoardCoordinate right)
        {
            if (left == null || right == null)
                return left == right;

            return left.X == right.X && left.Y == right.Y;
        }

        private static bool AreBoardStatesEquivalent(BoardCellState[] left, BoardCellState[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;

            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                    return false;
            }

            return true;
        }

        internal static bool AreProtocolLinesEquivalent(
            System.Collections.Generic.IList<string> left,
            System.Collections.Generic.IList<string> right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Count != right.Count)
                return false;

            for (int index = 0; index < left.Count; index++)
            {
                if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static bool AreViewportsEquivalent(BoardViewport left, BoardViewport right)
        {
            if (left == null || right == null)
                return left == right;

            return AreRectsEquivalent(left.SourceBounds, right.SourceBounds)
                && AreRectsEquivalent(left.ScreenBounds, right.ScreenBounds)
                && left.CellWidth.Equals(right.CellWidth)
                && left.CellHeight.Equals(right.CellHeight);
        }

        private static bool AreRectsEquivalent(PixelRect left, PixelRect right)
        {
            if (left == null || right == null)
                return left == right;

            return left.X == right.X
                && left.Y == right.Y
                && left.Width == right.Width
                && left.Height == right.Height;
        }
    }

    internal sealed class RecognitionPerformanceAcceptanceReport
    {
        private RecognitionPerformanceAcceptanceReport(
            RecognitionReplayScenario scenario,
            RecognitionMeasurement cached,
            RecognitionMeasurement equivalentUncachedBaseline,
            RecognitionParityReport parity)
        {
            Scenario = scenario;
            Cached = cached;
            EquivalentUncachedBaseline = equivalentUncachedBaseline;
            Parity = parity;
        }

        public RecognitionReplayScenario Scenario { get; private set; }
        public RecognitionMeasurement Cached { get; private set; }
        public RecognitionMeasurement EquivalentUncachedBaseline { get; private set; }
        public RecognitionParityReport Parity { get; private set; }

        public double LatencyReductionRatio
        {
            get { return CalculateReduction(EquivalentUncachedBaseline.AverageElapsedMilliseconds, Cached.AverageElapsedMilliseconds); }
        }

        public double AllocationReductionRatio
        {
            get { return CalculateReduction(EquivalentUncachedBaseline.AverageAllocatedBytes, Cached.AverageAllocatedBytes); }
        }

        public bool MeetsLatencyAcceptance
        {
            get { return LatencyReductionRatio >= RecognitionPerformanceAcceptanceHarness.MinimumLatencyReductionRatio; }
        }

        public bool MeetsAllocationAcceptance
        {
            get { return AllocationReductionRatio >= RecognitionPerformanceAcceptanceHarness.MinimumAllocationReductionRatio; }
        }

        public bool MeetsAccuracyAcceptance
        {
            get
            {
                return Parity.CachedExpectedMismatchCount == 0
                    && Parity.BaselineExpectedMismatchCount == 0
                    && Parity.CachedVsBaselineMismatchCount == 0;
            }
        }

        public string DescribeLatencyFailure()
        {
            return string.Format(
                "Cached replay reduced average latency by {0:P2}; acceptance requires at least {1:P0}. Cached={2:F6}ms baseline={3:F6}ms.",
                LatencyReductionRatio,
                RecognitionPerformanceAcceptanceHarness.MinimumLatencyReductionRatio,
                Cached.AverageElapsedMilliseconds,
                EquivalentUncachedBaseline.AverageElapsedMilliseconds);
        }

        public string DescribeAllocationFailure()
        {
            return string.Format(
                "Cached replay reduced average allocation by {0:P2}; acceptance requires at least {1:P0}. Cached={2:F2}B baseline={3:F2}B.",
                AllocationReductionRatio,
                RecognitionPerformanceAcceptanceHarness.MinimumAllocationReductionRatio,
                Cached.AverageAllocatedBytes,
                EquivalentUncachedBaseline.AverageAllocatedBytes);
        }

        public string DescribeAccuracyFailure()
        {
            return string.Format(
                "Cached expected mismatches={0}, baseline expected mismatches={1}, cached-vs-baseline mismatches={2}.",
                Parity.CachedExpectedMismatchCount,
                Parity.BaselineExpectedMismatchCount,
                Parity.CachedVsBaselineMismatchCount);
        }

        public static RecognitionPerformanceAcceptanceReport Create(
            RecognitionReplayScenario scenario,
            RecognitionMeasurement cached,
            RecognitionMeasurement equivalentUncachedBaseline,
            RecognitionParityReport parity)
        {
            return new RecognitionPerformanceAcceptanceReport(scenario, cached, equivalentUncachedBaseline, parity);
        }

        private static double CalculateReduction(double baseline, double candidate)
        {
            if (baseline <= 0d)
                return 0d;
            return (baseline - candidate) / baseline;
        }
    }

    internal sealed class RecognitionMeasurement
    {
        private RecognitionMeasurement(
            int totalRecognitions,
            int cachedSnapshotCount,
            int unchangedSnapshotCount,
            int reusedPayloadCount,
            TimeSpan elapsed,
            long allocatedBytes)
        {
            TotalRecognitions = totalRecognitions;
            CachedSnapshotCount = cachedSnapshotCount;
            UnchangedSnapshotCount = unchangedSnapshotCount;
            ReusedPayloadCount = reusedPayloadCount;
            Elapsed = elapsed;
            AllocatedBytes = allocatedBytes;
        }

        public int TotalRecognitions { get; private set; }
        public int CachedSnapshotCount { get; private set; }
        public int UnchangedSnapshotCount { get; private set; }
        public int ReusedPayloadCount { get; private set; }
        public TimeSpan Elapsed { get; private set; }
        public long AllocatedBytes { get; private set; }

        public double AverageElapsedMilliseconds
        {
            get { return TotalRecognitions == 0 ? 0d : Elapsed.TotalMilliseconds / TotalRecognitions; }
        }

        public double AverageAllocatedBytes
        {
            get { return TotalRecognitions == 0 ? 0d : AllocatedBytes / (double)TotalRecognitions; }
        }

        public static RecognitionMeasurement Create(
            RecognitionMetricCounters counters,
            TimeSpan elapsed,
            long allocatedBytes)
        {
            return new RecognitionMeasurement(
                counters.TotalRecognitions,
                counters.CachedSnapshotCount,
                counters.UnchangedSnapshotCount,
                counters.ReusedPayloadCount,
                elapsed,
                allocatedBytes);
        }
    }

    internal sealed class RecognitionParityReport
    {
        private RecognitionParityReport()
        {
        }

        public int CachedExpectedMismatchCount { get; private set; }
        public int BaselineExpectedMismatchCount { get; private set; }
        public int CachedVsBaselineMismatchCount { get; private set; }
        public int CachedSnapshotCount { get; private set; }

        public void Observe(
            RecognitionReplayStep step,
            BoardRecognitionResult cached,
            BoardRecognitionResult baseline,
            bool resultsEquivalent)
        {
            if (cached != null && cached.UsedCachedSnapshot)
                CachedSnapshotCount++;
            if (!RecognitionPerformanceAcceptanceHarness.AreProtocolLinesEquivalent(GetProtocolLines(cached), step.ExpectedProtocolLines))
                CachedExpectedMismatchCount++;
            if (!RecognitionPerformanceAcceptanceHarness.AreProtocolLinesEquivalent(GetProtocolLines(baseline), step.ExpectedProtocolLines))
                BaselineExpectedMismatchCount++;
            if (!resultsEquivalent)
                CachedVsBaselineMismatchCount++;
        }

        private static System.Collections.Generic.IList<string> GetProtocolLines(BoardRecognitionResult result)
        {
            return result == null || result.Snapshot == null
                ? null
                : result.Snapshot.ProtocolLines;
        }

        public static RecognitionParityReport Create()
        {
            return new RecognitionParityReport();
        }
    }

    internal sealed class RecognitionReplayScenario
    {
        private RecognitionReplayScenario(string name, RecognitionReplayStep[] steps)
        {
            Name = name;
            Steps = steps;
        }

        public string Name { get; private set; }
        public RecognitionReplayStep[] Steps { get; private set; }

        public static RecognitionReplayScenario Create(string name, RecognitionReplayStep[] steps)
        {
            return new RecognitionReplayScenario(name, steps);
        }
    }

    internal sealed class RecognitionReplayStep
    {
        private RecognitionReplayStep(
            string label,
            BoardRecognitionRequest request,
            string[] expectedProtocolLines)
        {
            Label = label;
            Request = request;
            ExpectedProtocolLines = expectedProtocolLines;
        }

        public string Label { get; private set; }
        public BoardRecognitionRequest Request { get; private set; }
        public string[] ExpectedProtocolLines { get; private set; }

        public static RecognitionReplayStep Create(
            string label,
            BoardRecognitionRequest request,
            string[] expectedProtocolLines)
        {
            return new RecognitionReplayStep(label, request, expectedProtocolLines);
        }
    }

    internal sealed class RecognitionMetricCounters
    {
        public int TotalRecognitions { get; set; }
        public int CachedSnapshotCount { get; set; }
        public int UnchangedSnapshotCount { get; set; }
        public int ReusedPayloadCount { get; set; }

        public void Accumulate(RecognitionMetricCounters other)
        {
            TotalRecognitions += other.TotalRecognitions;
            CachedSnapshotCount += other.CachedSnapshotCount;
            UnchangedSnapshotCount += other.UnchangedSnapshotCount;
            ReusedPayloadCount += other.ReusedPayloadCount;
        }
    }
}
