using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
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
        private readonly Func<string> _latestReleaseJsonProvider;

        public GitHubUpdateChecker()
            : this(AppReleaseVersion.GetCurrentVersion, DownloadLatestReleaseJson)
        {
        }

        internal GitHubUpdateChecker(
            Func<string> currentVersionProvider,
            Func<string> latestReleaseJsonProvider)
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
            return Task<UpdateCheckResult>.Factory.StartNew(
                CheckCore,
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Default);
        }

        private UpdateCheckResult CheckCore()
        {
            string currentVersion = null;

            try
            {
                currentVersion = _currentVersionProvider();
                SemanticVersion currentSemanticVersion =
                    ParseSemanticVersion(currentVersion, "Current version");
                GitHubReleaseInfo latestRelease = ParseLatestRelease(_latestReleaseJsonProvider());
                SemanticVersion latestSemanticVersion =
                    ParseSemanticVersion(latestRelease.Tag, "Latest release tag");
                return CreateSuccessResult(
                    currentSemanticVersion,
                    latestSemanticVersion,
                    latestRelease);
            }
            catch (Exception exception)
            {
                return CreateFailureResult(currentVersion, exception);
            }
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
                Name = ReadRequiredString(payload, "name", true),
                Body = ReadRequiredString(payload, "body", true),
                HtmlUrl = ReadRequiredString(payload, "html_url", false),
                PublishedAt = ReadPublishedAt(payload)
            };
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

        private static string DownloadLatestReleaseJson()
        {
            EnableTls12();
            HttpWebRequest request = CreateLatestReleaseRequest();

            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            {
                if (responseStream == null)
                {
                    throw new InvalidOperationException("Latest release response stream is empty.");
                }

                using (var reader = new StreamReader(responseStream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static void EnableTls12()
        {
            ServicePointManager.SecurityProtocol =
                ServicePointManager.SecurityProtocol | Tls12SecurityProtocol;
        }

        private static HttpWebRequest CreateLatestReleaseRequest()
        {
            var request = (HttpWebRequest)WebRequest.Create(LatestReleaseApiUrl);
            request.Accept = GitHubAcceptHeader;
            request.UserAgent = GitHubUserAgent;
            request.AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = RequestTimeoutMilliseconds;
            request.ReadWriteTimeout = RequestTimeoutMilliseconds;
            return request;
        }

        private struct SemanticVersion : IComparable<SemanticVersion>
        {
            private const int VersionSegmentCount = 3;

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
                    normalizedValue = normalizedValue.Substring(1);
                }

                return normalizedValue;
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
