namespace readboard
{
    internal enum OverlayVisibility
    {
        Hidden = 0,
        Visible = 1
    }

    internal sealed class OverlayUpdateRequest
    {
        public OverlayVisibility Visibility { get; set; }
        public BoardFrame Frame { get; set; }
        public string LegacyTypeToken { get; set; }
        public string HiddenCommandText { get; set; }
    }
}
