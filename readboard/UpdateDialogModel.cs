using System;
using System.Threading.Tasks;

namespace readboard
{
    public sealed class UpdateDialogModel
    {
        public string CurrentVersion { get; set; }

        public string LatestVersion { get; set; }

        public DateTime? PublishedAt { get; set; }

        public string ReleaseNotes { get; set; }

        public string DownloadUrl { get; set; }

        public string UnavailableText { get; set; }

        public string EmptyReleaseNotesText { get; set; }

        public string MissingDownloadUrlMessage { get; set; }

        public string InvalidDownloadUrlFormatMessage { get; set; }

        public string UnsupportedDownloadUrlSchemeMessage { get; set; }

        public string OpenDownloadUrlFailedMessage { get; set; }

        public bool HostedInstallAvailable { get; set; }

        public string HostedReleaseTag { get; set; }

        public string HostedAssetName { get; set; }

        public string HostedAssetDownloadUrl { get; set; }

        public string DownloadButtonText { get; set; }

        public string DownloadAndInstallButtonText { get; set; }

        public string DownloadingButtonText { get; set; }

        public string WaitingForHostInstallText { get; set; }

        public string HostCancelledText { get; set; }

        public string HostFailedText { get; set; }

        public string HostTimedOutText { get; set; }

        public string ManualDownloadFallbackMessage { get; set; }

        public Func<UpdateDialogModel, Task<string>> PrepareHostedUpdateAsync { get; set; }

        public Action<string, string> NotifyHostedUpdateReady { get; set; }
    }
}
