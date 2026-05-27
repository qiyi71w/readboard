namespace readboard
{
    internal sealed class AutoPlayColorResolution
    {
        private AutoPlayColorResolution(string playColor, AutoPlayColorStatus status, bool isKnown)
        {
            PlayColor = playColor;
            Status = status;
            IsKnown = isKnown;
        }

        public string PlayColor { get; private set; }
        public AutoPlayColorStatus Status { get; private set; }
        public bool IsKnown { get; private set; }

        public static AutoPlayColorResolution Known(string playColor, AutoPlayColorStatus status)
        {
            return new AutoPlayColorResolution(playColor, status, true);
        }

        public static AutoPlayColorResolution Unknown(AutoPlayColorStatus status)
        {
            return new AutoPlayColorResolution(null, status, false);
        }
    }
}
