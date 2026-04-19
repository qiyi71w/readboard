using readboard;

namespace Readboard.VerificationTests.Recognition
{
    internal sealed class ReplayBatchMetric
    {
        private ReplayBatchMetric()
        {
        }

        public int TotalRecognitions { get; private set; }
        public int CachedSnapshotCount { get; private set; }
        public int UnchangedSnapshotCount { get; private set; }
        public int ReusedPayloadCount { get; private set; }
        public bool ChangedFrameUsedCachedSnapshot { get; private set; }

        public static ReplayBatchMetric Measure(ReplayFixture fixture, int unchangedRepeats)
        {
            LegacyBoardRecognitionService service = new LegacyBoardRecognitionService();
            ReplayBatchMetric metric = new ReplayBatchMetric();

            for (int index = 0; index < unchangedRepeats; index++)
                metric.Observe(service.Recognize(fixture.CreateRecognitionRequest(ReplayVariant.Base, inferLastMove: false)));

            BoardRecognitionResult changed = service.Recognize(
                fixture.CreateRecognitionRequest(ReplayVariant.Changed, inferLastMove: false));
            metric.Observe(changed);
            metric.ChangedFrameUsedCachedSnapshot = changed.UsedCachedSnapshot;
            return metric;
        }

        private void Observe(BoardRecognitionResult result)
        {
            TotalRecognitions++;
            if (result.UsedCachedSnapshot)
                CachedSnapshotCount++;
            if (result.Snapshot.IsUnchangedFromPrevious)
                UnchangedSnapshotCount++;
            if (result.Snapshot.ReusedPayload)
                ReusedPayloadCount++;
        }
    }
}
