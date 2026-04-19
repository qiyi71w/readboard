namespace readboard
{
    internal sealed class OverlayUpdateResult
    {
        public bool ShouldSend { get; set; }
        public OverlayVisibility Visibility { get; set; }
        public string ProtocolLine { get; set; }
    }
}
