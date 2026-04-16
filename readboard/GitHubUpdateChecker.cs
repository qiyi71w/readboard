using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace readboard
{
    public sealed class GitHubUpdateChecker
    {
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/qiyi71w/readboard/releases/latest";
        private const string GitHubAcceptHeader = "application/vnd.github+json";
        private const string GitHubUserAgent = "readboard-update-checker";
        private const int RequestTimeoutMilliseconds = 15000;
        private const int Tls12ProtocolValue = 3072;
        private static readonly SecurityProtocolType Tls12SecurityProtocol =
            (SecurityProtocolType)Tls12ProtocolValue;

        private readonly Func<string> _currentVersionProvider;
        private readonly Func<Task<string>> _latestReleaseJsonProvider;

        public GitHubUpdateChecker()
            : this(AppReleaseVersion.GetCurrentVersion, DownloadLatestReleaseJsonAsync)
        {
        }

        internal GitHubUpdateChecker(
            Func<string> currentVersionProvider,
            Func<Task<string>> latestReleaseJsonProvider)
        {
            if (currentVersionProvider == null)
            {
                throw new ArgumentNullException("currentVersionProvider");
            }

            if (latestReleaseJsonProvider == null)
            {
                throw new ArgumentNullException("latestReleaseJsonProvider");
            }

            _currentVersionProvider = currentVersionProvider;
            _latestReleaseJsonProvider = latestReleaseJsonProvider;
        }

        public Task<UpdateCheckResult> CheckAsync()
        {
            string currentVersion = null;
            SemanticVersion currentSemanticVersion;

            try
            {
                currentVersion = _currentVersionProvider();
                currentSemanticVersion = ParseSemanticVersion(currentVersion, "Current version");
            }
            catch (Exception exception)
            {
                return CreateCompletedTask(CreateFailureResult(currentVersion, exception));
            }

            Task<string> latestReleaseJsonTask;
            try
            {
                latestReleaseJsonTask = _latestReleaseJsonProvider();
            }
            catch (Exception exception)
            {
                return CreateCompletedTask(CreateFailureResult(currentVersion, exception));
            }

            if (latestReleaseJsonTask == null)
            {
                return CreateCompletedTask(
                    CreateFailureResult(
                        currentVersion,
                        new InvalidOperationException("Latest release request returned no task.")));
            }

            var completion = new TaskCompletionSource<UpdateCheckResult>();
            latestReleaseJsonTask.ContinueWith(
                task => CompleteCheck(
                    completion,
                    currentVersion,
                    currentSemanticVersion,
                    task),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return completion.Task;
        }

        private static UpdateCheckResult CreateSuccessResult(
            SemanticVersion currentVersion,
            SemanticVersion latestVersion,
            GitHubReleaseInfo latestRelease)
        {
            return new UpdateCheckResult
            {
                Status = latestVersion.CompareTo(currentVersion) > 0
                    ? UpdateCheckStatus.UpdateAvailable
                    : UpdateCheckStatus.UpToDate,
                CurrentVersion = currentVersion.ToString(),
                LatestVersion = latestVersion.ToString(),
                PublishedAt = latestRelease.PublishedAt,
                ReleaseNotes = latestRelease.Body,
                ReleaseUrl = latestRelease.HtmlUrl,
                ErrorMessage = null
            };
        }

        private static UpdateCheckResult CreateFailureResult(
            string currentVersion,
            Exception exception)
        {
            Exception baseException = exception is AggregateException
                ? exception.GetBaseException()
                : exception;

            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                CurrentVersion = currentVersion,
                LatestVersion = null,
                PublishedAt = null,
                ReleaseNotes = null,
                ReleaseUrl = null,
                ErrorMessage = baseException.Message
            };
        }

        private static Task<UpdateCheckResult> CreateCompletedTask(UpdateCheckResult result)
        {
            var completion = new TaskCompletionSource<UpdateCheckResult>();
            completion.SetResult(result);
            return completion.Task;
        }

        private static void CompleteCheck(
            TaskCompletionSource<UpdateCheckResult> completion,
            string currentVersion,
            SemanticVersion currentSemanticVersion,
            Task<string> latestReleaseJsonTask)
        {
            try
            {
                if (latestReleaseJsonTask.IsCanceled)
                {
                    throw new TaskCanceledException("Latest release request was canceled.");
                }

                if (latestReleaseJsonTask.IsFaulted)
                {
                    throw latestReleaseJsonTask.Exception;
                }

                GitHubReleaseInfo latestRelease =
                    ParseLatestRelease(latestReleaseJsonTask.Result);
                SemanticVersion latestSemanticVersion =
                    ParseSemanticVersion(latestRelease.Tag, "Latest release tag");
                completion.SetResult(
                    CreateSuccessResult(
                        currentSemanticVersion,
                        latestSemanticVersion,
                        latestRelease));
            }
            catch (Exception exception)
            {
                completion.SetResult(CreateFailureResult(currentVersion, exception));
            }
        }

        private static SemanticVersion ParseSemanticVersion(string value, string label)
        {
            SemanticVersion semanticVersion;
            if (SemanticVersion.TryParse(value, out semanticVersion))
            {
                return semanticVersion;
            }

            throw new InvalidOperationException(
                label + " is not a valid semantic version: " + value);
        }

        private static GitHubReleaseInfo ParseLatestRelease(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Latest release response is empty.");
            }

            var serializer = new JavaScriptSerializer();
            var payload = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (payload == null)
            {
                throw new InvalidOperationException("Latest release response is not a JSON object.");
            }

            return new GitHubReleaseInfo
            {
                Tag = ReadRequiredString(payload, "tag_name", false),
                Name = ReadOptionalString(payload, "name"),
                Body = ReadOptionalString(payload, "body"),
                HtmlUrl = ReadRequiredString(payload, "html_url", false),
                PublishedAt = ReadPublishedAt(payload)
            };
        }

        private static string ReadOptionalString(
            IDictionary<string, object> payload,
            string key)
        {
            object value;
            if (!payload.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            string stringValue = value as string;
            if (stringValue == null)
            {
                throw new InvalidOperationException(
                    "Latest release field '" + key + "' is not a string.");
            }

            return stringValue;
        }

        private static string ReadRequiredString(
            IDictionary<string, object> payload,
            string key,
            bool allowEmpty)
        {
            object value;
            if (!payload.TryGetValue(key, out value))
            {
                throw new InvalidOperationException(
                    "Latest release JSON is missing '" + key + "'.");
            }

            string stringValue = value as string;
            if (stringValue == null)
            {
                throw new InvalidOperationException(
                    "Latest release field '" + key + "' is not a string.");
            }

            if (!allowEmpty && string.IsNullOrWhiteSpace(stringValue))
            {
                throw new InvalidOperationException(
                    "Latest release field '" + key + "' is empty.");
            }

            return stringValue;
        }

        private static DateTime ReadPublishedAt(IDictionary<string, object> payload)
        {
            string publishedAtValue = ReadRequiredString(payload, "published_at", false);
            DateTime publishedAt;
            bool isValidDate = DateTime.TryParse(
                publishedAtValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out publishedAt);
            if (isValidDate)
            {
                return publishedAt;
            }

            throw new InvalidOperationException(
                "Latest release field 'published_at' is not a valid date: " + publishedAtValue);
        }

        private static Task<string> DownloadLatestReleaseJsonAsync()
        {
            EnableGlobalTls12();

            var handler = new HttpClientHandler
            {
                AutomaticDecompression =
                    DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            HttpClient client = CreateLatestReleaseClient(handler);

            Task<string> downloadTask;
            try
            {
                downloadTask = client.GetStringAsync(LatestReleaseApiUrl);
            }
            catch
            {
                DisposeHttpResources(client, handler);
                throw;
            }

            var completion = new TaskCompletionSource<string>();
            downloadTask.ContinueWith(
                task => CompleteDownload(completion, client, handler, task),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return completion.Task;
        }

        // net40 only exposes TLS selection through the process-wide ServicePointManager switch.
        private static void EnableGlobalTls12()
        {
            ServicePointManager.SecurityProtocol =
                ServicePointManager.SecurityProtocol | Tls12SecurityProtocol;
        }

        private static HttpClient CreateLatestReleaseClient(HttpClientHandler handler)
        {
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromMilliseconds(RequestTimeoutMilliseconds);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(GitHubAcceptHeader));
            client.DefaultRequestHeaders.UserAgent.ParseAdd(GitHubUserAgent);
            return client;
        }

        private static void CompleteDownload(
            TaskCompletionSource<string> completion,
            HttpClient client,
            HttpClientHandler handler,
            Task<string> downloadTask)
        {
            try
            {
                if (downloadTask.IsCanceled)
                {
                    throw new TaskCanceledException("Latest release request was canceled.");
                }

                if (downloadTask.IsFaulted)
                {
                    throw downloadTask.Exception;
                }

                completion.SetResult(downloadTask.Result);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                DisposeHttpResources(client, handler);
            }
        }

        private static void DisposeHttpResources(
            HttpClient client,
            HttpClientHandler handler)
        {
            if (client != null)
            {
                client.Dispose();
            }

            if (handler != null)
            {
                handler.Dispose();
            }
        }

        private struct SemanticVersion : IComparable<SemanticVersion>
        {
            private const int VersionSegmentCount = 3;
            private const int VersionPrefixLength = 1;
            private const char PreReleaseSeparator = '-';
            private const char BuildMetadataSeparator = '+';

            private readonly int _major;
            private readonly int _minor;
            private readonly int _patch;

            public SemanticVersion(int major, int minor, int patch)
            {
                _major = major;
                _minor = minor;
                _patch = patch;
            }

            public int CompareTo(SemanticVersion other)
            {
                int majorComparison = _major.CompareTo(other._major);
                if (majorComparison != 0)
                {
                    return majorComparison;
                }

                int minorComparison = _minor.CompareTo(other._minor);
                if (minorComparison != 0)
                {
                    return minorComparison;
                }

                return _patch.CompareTo(other._patch);
            }

            public override string ToString()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.{1}.{2}",
                    _major,
                    _minor,
                    _patch);
            }

            public static bool TryParse(string value, out SemanticVersion semanticVersion)
            {
                semanticVersion = default(SemanticVersion);

                string normalizedValue = Normalize(value);
                if (normalizedValue == null)
                {
                    return false;
                }

                string[] segments = normalizedValue.Split('.');
                if (segments.Length != VersionSegmentCount)
                {
                    return false;
                }

                int major;
                int minor;
                int patch;
                if (!TryParseSegment(segments[0], out major) ||
                    !TryParseSegment(segments[1], out minor) ||
                    !TryParseSegment(segments[2], out patch))
                {
                    return false;
                }

                semanticVersion = new SemanticVersion(major, minor, patch);
                return true;
            }

            private static string Normalize(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                string normalizedValue = value.Trim();
                if (normalizedValue.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedValue = normalizedValue.Substring(VersionPrefixLength);
                }

                int suffixIndex = FindSuffixIndex(normalizedValue);
                if (suffixIndex >= 0)
                {
                    normalizedValue = normalizedValue.Substring(0, suffixIndex);
                }

                return normalizedValue;
            }

            private static int FindSuffixIndex(string value)
            {
                int preReleaseIndex = value.IndexOf(PreReleaseSeparator);
                int buildMetadataIndex = value.IndexOf(BuildMetadataSeparator);
                if (preReleaseIndex < 0)
                {
                    return buildMetadataIndex;
                }

                if (buildMetadataIndex < 0)
                {
                    return preReleaseIndex;
                }

                return Math.Min(preReleaseIndex, buildMetadataIndex);
            }

            private static bool TryParseSegment(string value, out int number)
            {
                return int.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out number);
            }
        }
    }
}
