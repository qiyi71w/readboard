using System;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class UpdateBridgeTests
    {
        [Fact]
        public void HostedInstall_RequiresPipeActiveCapabilityAndCompleteAsset()
        {
            UpdateCheckResult result = CreateResult();

            Assert.True(MainForm.CanOfferWebViewHostedInstall(
                TransportKind.Pipe,
                true,
                true,
                false,
                result));
            Assert.False(MainForm.CanOfferWebViewHostedInstall(
                TransportKind.Tcp,
                true,
                true,
                false,
                result));
            Assert.False(MainForm.CanOfferWebViewHostedInstall(
                TransportKind.Pipe,
                false,
                true,
                false,
                result));
            Assert.False(MainForm.CanOfferWebViewHostedInstall(
                TransportKind.Pipe,
                true,
                false,
                false,
                result));

            result.AssetDownloadUrl = null;
            Assert.False(MainForm.CanOfferWebViewHostedInstall(
                TransportKind.Pipe,
                true,
                true,
                false,
                result));
        }

        [Fact]
        public void ManualDownload_UsesFixedRepositoryReleasePage()
        {
            Uri uri = MainForm.GetWebViewManualDownloadUri();
            var startInfo = MainForm.CreateWebViewDownloadStartInfo(uri);

            Assert.Equal(
                "https://github.com/qiyi71w/readboard/releases/latest",
                uri.AbsoluteUri);
            Assert.Equal(uri.AbsoluteUri, startInfo.FileName);
            Assert.True(startInfo.UseShellExecute);
        }

        [Theory]
        [InlineData("https://github.com/qiyi71w/readboard/releases/tag/v3.0.2", "https://github.com/qiyi71w/readboard/releases/tag/v3.0.2")]
        [InlineData("https://example.com/qiyi71w/readboard/releases/tag/v3.0.2", "https://github.com/qiyi71w/readboard/releases/latest")]
        [InlineData("http://github.com/qiyi71w/readboard/releases/tag/v3.0.2", "https://github.com/qiyi71w/readboard/releases/latest")]
        public void ManualDownload_UsesCheckedReleaseUrlOnlyWithinRepository(string releaseUrl, string expected)
        {
            UpdateCheckResult result = new UpdateCheckResult { ReleaseUrl = releaseUrl };

            Assert.Equal(expected, MainForm.ResolveWebViewManualDownloadUri(result).AbsoluteUri);
        }

        [Fact]
        public void DownloadStartInfo_RejectsNullUri()
        {
            Assert.Throws<ArgumentNullException>(
                () => MainForm.CreateWebViewDownloadStartInfo(null));
        }

        [Theory]
        [InlineData("{\"type\":\"about.checkUpdate\",\"payload\":{}}", "Check")]
        [InlineData("{\"type\":\"update.close\",\"payload\":{}}", "Close")]
        [InlineData("{\"type\":\"update.install\",\"payload\":{}}", "Install")]
        [InlineData("{\"type\":\"update.openDownload\",\"payload\":{}}", "OpenDownload")]
        public void StrictUpdateJson_IsConvertedToTypedIntent(
            string json,
            string expectedIntentName)
        {
            Assert.True(MainForm.TryParseWebViewCommand(json, out ReadBoardUiCommand command));

            Assert.True(MainForm.TryParseWebViewUpdateIntent(command, out ReadBoardUpdateIntent actualIntent));
            Assert.Equal(expectedIntentName, actualIntent.ToString());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("not-json")]
        [InlineData("{\"type\":\"update.install\",\"payload\":{\"extra\":true}}")]
        public void InvalidUpdateJson_DoesNotProduceTypedIntent(string json)
        {
            Assert.False(MainForm.TryParseWebViewCommand(json, out ReadBoardUiCommand command));
            Assert.False(MainForm.TryParseWebViewUpdateIntent(command, out _));
        }
        [Theory]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(3, false)]
        public void HostedProcessing_CloseCapabilityFollowsReadBoardOwnershipStage(
            int activeStep,
            bool expected)
        {
            Assert.Equal(
                expected,
                MainForm.IsWebViewUpdateProcessingCloseEnabled(activeStep));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void OpenDownload_RejectedWhenAuthoritativeStateDisablesIt(
            bool open,
            bool openDownloadEnabled)
        {
            Assert.True(MainForm.ShouldPublishWebViewUpdateOpenDownloadRejection(
                open,
                openDownloadEnabled));
        }

        [Fact]
        public void OpenDownload_AcceptedOnlyWhenAuthoritativeStateEnablesIt()
        {
            Assert.False(MainForm.ShouldPublishWebViewUpdateOpenDownloadRejection(true, true));
        }

        [Fact]
        public void UpdateAvailable_ProjectsHostedAndManualCapabilities()
        {
            UpdateCheckResult result = CreateResult();
            result.Status = UpdateCheckStatus.UpdateAvailable;
            result.CurrentVersion = "3.0.1";
            result.LatestVersion = "3.0.2";
            result.ReleaseNotes = "notes";

            ReadBoardUpdateUiState hosted = MainForm.ResolveWebViewUpdateCheckResultState(result, true);
            Assert.Equal("available", hosted.Status);
            Assert.True(hosted.InstallEnabled);
            Assert.False(hosted.OpenDownloadEnabled);
            Assert.Null(hosted.TitleMessage);
            Assert.Null(hosted.MessageMessage);
            Assert.Equal("notes", hosted.ReleaseNotes);

            ReadBoardUpdateUiState manual = MainForm.ResolveWebViewUpdateCheckResultState(result, false);
            Assert.Equal("manual", manual.Status);
            Assert.False(manual.InstallEnabled);
            Assert.True(manual.OpenDownloadEnabled);
            Assert.Equal("WebView_hostedInstallUnsupported", manual.TitleMessage.Key);
            Assert.Equal("WebView_manualDownload", manual.MessageMessage.Key);
        }

        [Theory]
        [InlineData(UpdateCheckStatus.UpToDate, "latest", "Update_upToDateRetired")]
        [InlineData(UpdateCheckStatus.OutsideChannel, "notice", "Update_outsideChannel")]
        [InlineData(UpdateCheckStatus.NoMatchingChannel, "notice", "Update_noMatchingChannel")]
        [InlineData(UpdateCheckStatus.Failed, "check-failed", "Update_checkFailed")]
        public void CheckResultStatuses_ProjectCompleteAuthoritativeState(
            UpdateCheckStatus status,
            string expectedStatus,
            string expectedTitleKey)
        {
            UpdateCheckResult result = CreateResult();
            result.Status = status;
            result.CurrentVersion = "3.0.1";
            result.LatestVersion = "3.0.2";
            result.ChannelStatus = "retired";
            result.IncompatibleNewerVersion = "3.2.0";
            result.IncompatibleMinimumWindowsVersion = "10.0.19041";
            result.ErrorMessage = "checker failed";

            ReadBoardUpdateUiState state = MainForm.ResolveWebViewUpdateCheckResultState(result, false);

            Assert.True(state.Open);
            Assert.Equal(expectedStatus, state.Status);
            Assert.True(state.CloseEnabled);
            if (status != UpdateCheckStatus.Failed)
            {
                Assert.Equal("3.0.1", state.CurrentVersion);
                Assert.Equal("3.0.2", state.LatestVersion);
            }
            Assert.Equal(expectedTitleKey, state.TitleMessage.Key);
            if (status == UpdateCheckStatus.Failed)
            {
                Assert.Equal("checker failed", state.DetailMessage.DiagnosticDetail);
            }
            else
            {
                Assert.Contains(
                    state.DetailMessages,
                    message => message.Key == "Update_retiredFinalVersion");
                Assert.Contains(
                    state.DetailMessages,
                    message => message.Key == "Update_newerVersionRequiresWindows");
            }
        }


        private static UpdateCheckResult CreateResult()
        {
            return new UpdateCheckResult
            {
                Tag = "v3.0.2",
                AssetName = "readboard-github-release-v3.0.2.zip",
                AssetDownloadUrl = "https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip",
                AssetSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                ReleaseUrl = "https://github.com/qiyi71w/readboard/releases/tag/v3.0.2"
            };
        }
    }
}
