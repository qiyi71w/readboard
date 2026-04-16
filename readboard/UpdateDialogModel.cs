namespace readboard
{
    public sealed class UpdateDialogModel
    {
        public string CurrentVersion { get; set; }

        public string LatestVersion { get; set; }

        public string ReleaseDate { get; set; }

        public string ReleaseNotes { get; set; }

        public string DownloadUrl { get; set; }

        public string UnavailableText { get; set; }

        public string EmptyReleaseNotesText { get; set; }

        public string MissingDownloadUrlMessage { get; set; }

        public string InvalidDownloadUrlFormatMessage { get; set; }

        public string UnsupportedDownloadUrlSchemeMessage { get; set; }

        public string OpenDownloadUrlFailedMessage { get; set; }
    }
}
