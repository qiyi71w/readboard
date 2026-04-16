using System;

namespace readboard
{
    public sealed class UpdateCheckResult
    {
        public UpdateCheckStatus Status { get; internal set; }

        public string CurrentVersion { get; internal set; }

        public string LatestVersion { get; internal set; }

        public DateTime? PublishedAt { get; internal set; }

        public string ReleaseNotes { get; internal set; }

        public string ReleaseUrl { get; internal set; }

        public string ErrorMessage { get; internal set; }
    }
}
