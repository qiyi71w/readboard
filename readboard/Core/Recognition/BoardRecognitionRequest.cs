namespace readboard
{
    internal sealed class BoardRecognitionRequest
    {
        public BoardRecognitionRequest()
        {
            Thresholds = RecognitionThresholds.CreateDefault();
            InferLastMove = true;
        }

        public BoardFrame Frame { get; set; }
        public RecognitionThresholds Thresholds { get; set; }
        public bool InferLastMove { get; set; }
    }
}
