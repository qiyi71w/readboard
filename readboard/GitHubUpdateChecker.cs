using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace readboard
{
    public sealed class GitHubUpdateChecker
    {
        private const string ChannelManifestUrl =
            "https://raw.githubusercontent.com/qiyi71w/readboard/main/update-channels.json";
        private const string ReleaseApiUrlPrefix =
            "https://api.github.com/repos/qiyi71w/readboard/releases/tags/";
        private const string GitHubAcceptHeader = "application/vnd.github+json";
        private const string GitHubUserAgent = "readboard-update-checker";
        private const int RequestTimeoutMilliseconds = 15000;
        private const int SupportedSchemaVersion = 1;

        private readonly Func<string> _currentVersionProvider;
        private readonly Func<Version> _windowsVersionProvider;
        private readonly Func<Task<string>> _channelManifestJsonProvider;
        private readonly Func<string, Task<string>> _releaseJsonProvider;

        public GitHubUpdateChecker()
            : this(
                AppReleaseVersion.GetCurrentVersion,
                () => Environment.OSVersion.Version,
                DownloadChannelManifestJsonAsync,
                DownloadReleaseJsonAsync)
        {
        }

        internal GitHubUpdateChecker(
            Func<string> currentVersionProvider,
            Func<Version> windowsVersionProvider,
            Func<Task<string>> channelManifestJsonProvider,
            Func<string, Task<string>> releaseJsonProvider)
        {
            _currentVersionProvider = currentVersionProvider ??
                throw new ArgumentNullException("currentVersionProvider");
            _windowsVersionProvider = windowsVersionProvider ??
                throw new ArgumentNullException("windowsVersionProvider");
            _channelManifestJsonProvider = channelManifestJsonProvider ??
                throw new ArgumentNullException("channelManifestJsonProvider");
            _releaseJsonProvider = releaseJsonProvider ??
                throw new ArgumentNullException("releaseJsonProvider");
        }

        public async Task<UpdateCheckResult> CheckAsync()
        {
            string currentVersion = null;
            try
            {
                currentVersion = _currentVersionProvider();
                SemanticVersion currentSemanticVersion =
                    ParseSemanticVersion(currentVersion, "Current version");
                Version windowsVersion = _windowsVersionProvider() ??
                    throw new InvalidOperationException("Windows version is unavailable.");

                string manifestJson = await RequireTask(
                    _channelManifestJsonProvider(),
                    "Channel manifest request");
                UpdateChannel channel = SelectChannel(ParseManifest(manifestJson), windowsVersion);
                if (channel == null)
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateCheckStatus.NoMatchingChannel,
                        CurrentVersion = currentSemanticVersion.ToString()
                    };
                }

                string releaseJson = await RequireTask(
                    _releaseJsonProvider(channel.LatestTag),
                    "Release request");
                GitHubReleaseInfo release = ParseRelease(releaseJson, channel);
                SemanticVersion latestVersion =
                    ParseSemanticVersion(channel.LatestTag, "Channel latest tag");

                return CreateSuccessResult(
                    currentSemanticVersion,
                    latestVersion,
                    channel,
                    release);
            }
            catch (Exception exception)
            {
                return CreateFailureResult(currentVersion, exception);
            }
        }

        private static async Task<string> RequireTask(Task<string> task, string label)
        {
            if (task == null)
            {
                throw new InvalidOperationException(label + " returned no task.");
            }

            return await task;
        }

        private static UpdateCheckResult CreateSuccessResult(
            SemanticVersion currentVersion,
            SemanticVersion latestVersion,
            UpdateChannel channel,
            GitHubReleaseInfo release)
        {
            int comparison = currentVersion.CompareTo(latestVersion);
            UpdateCheckStatus status = comparison < 0
                ? UpdateCheckStatus.UpdateAvailable
                : comparison == 0
                    ? UpdateCheckStatus.UpToDate
                    : UpdateCheckStatus.OutsideChannel;

            return new UpdateCheckResult
            {
                Status = status,
                CurrentVersion = currentVersion.ToString(),
                LatestVersion = latestVersion.ToString(),
                ChannelId = channel.Id,
                ChannelStatus = channel.Status,
                Tag = release.Tag,
                PublishedAt = release.PublishedAt,
                ReleaseNotes = release.Body,
                ReleaseUrl = release.HtmlUrl,
                AssetName = release.AssetName,
                AssetDownloadUrl = release.AssetDownloadUrl,
                AssetSize = release.AssetSize
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
                ErrorMessage = baseException.Message
            };
        }

        private static List<UpdateChannel> ParseManifest(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Channel manifest response is empty.");
            }

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Channel manifest is not a JSON object.");
            }

            int schemaVersion = ReadRequiredInt32(root, "schemaVersion", "Channel manifest");
            if (schemaVersion != SupportedSchemaVersion)
            {
                throw new InvalidOperationException(
                    "Unsupported channel manifest schema version: " + schemaVersion);
            }

            JsonElement channelsElement = ReadRequiredProperty(root, "channels", "Channel manifest");
            if (channelsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Channel manifest field 'channels' is not an array.");
            }

            var channels = new List<UpdateChannel>();
            var channelIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement channelElement in channelsElement.EnumerateArray())
            {
                if (channelElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException("Channel entry is not a JSON object.");
                }

                UpdateChannel channel = ParseChannel(channelElement);
                if (!channelIds.Add(channel.Id))
                {
                    throw new InvalidOperationException("Duplicate update channel id: " + channel.Id);
                }

                channels.Add(channel);
            }

            ValidateNoOverlappingRanges(channels);
            return channels;
        }

        private static UpdateChannel ParseChannel(JsonElement element)
        {
            string id = ReadRequiredString(element, "id", "Channel");
            string status = ReadRequiredString(element, "status", "Channel '" + id + "'");
            if (!string.Equals(status, "active", StringComparison.Ordinal) &&
                !string.Equals(status, "retired", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Channel '" + id + "' has invalid status: " + status);
            }

            string latestTag = ReadRequiredString(element, "latestTag", "Channel '" + id + "'");
            ParseSemanticVersion(latestTag, "Channel '" + id + "' latest tag");

            Version minimum = ReadOptionalVersion(element, "minimumWindowsVersion", id);
            Version maximum = ReadOptionalVersion(
                element,
                "maximumWindowsVersionExclusive",
                id);
            if (minimum != null && maximum != null && minimum.CompareTo(maximum) >= 0)
            {
                throw new InvalidOperationException(
                    "Channel '" + id + "' has an empty or reversed Windows range.");
            }

            return new UpdateChannel
            {
                Id = id,
                Status = status,
                MinimumWindowsVersion = minimum,
                MaximumWindowsVersionExclusive = maximum,
                LatestTag = latestTag,
                AssetName = ReadRequiredString(element, "assetName", "Channel '" + id + "'"),
                Sha256 = ReadRequiredString(element, "sha256", "Channel '" + id + "'")
            };
        }

        private static void ValidateNoOverlappingRanges(IList<UpdateChannel> channels)
        {
            for (int firstIndex = 0; firstIndex < channels.Count; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < channels.Count; secondIndex++)
                {
                    if (RangesOverlap(channels[firstIndex], channels[secondIndex]))
                    {
                        throw new InvalidOperationException(
                            "Update channel Windows ranges overlap: " +
                            channels[firstIndex].Id + " and " + channels[secondIndex].Id);
                    }
                }
            }
        }

        private static bool RangesOverlap(UpdateChannel first, UpdateChannel second)
        {
            bool firstEndsAfterSecondStarts =
                first.MaximumWindowsVersionExclusive == null ||
                second.MinimumWindowsVersion == null ||
                second.MinimumWindowsVersion.CompareTo(first.MaximumWindowsVersionExclusive) < 0;
            bool secondEndsAfterFirstStarts =
                second.MaximumWindowsVersionExclusive == null ||
                first.MinimumWindowsVersion == null ||
                first.MinimumWindowsVersion.CompareTo(second.MaximumWindowsVersionExclusive) < 0;
            return firstEndsAfterSecondStarts && secondEndsAfterFirstStarts;
        }

        private static UpdateChannel SelectChannel(
            IEnumerable<UpdateChannel> channels,
            Version windowsVersion)
        {
            UpdateChannel match = null;
            foreach (UpdateChannel channel in channels)
            {
                if (!channel.Matches(windowsVersion))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException(
                        "Multiple update channels match Windows " + windowsVersion + ".");
                }

                match = channel;
            }

            return match;
        }

        private static GitHubReleaseInfo ParseRelease(string json, UpdateChannel channel)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Release response is empty.");
            }

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Release response is not a JSON object.");
            }

            string tag = ReadRequiredString(root, "tag_name", "Release");
            if (!string.Equals(tag, channel.LatestTag, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Release tag does not match channel tag '" + channel.LatestTag + "'.");
            }

            if (ReadRequiredBoolean(root, "draft", "Release") ||
                ReadRequiredBoolean(root, "prerelease", "Release"))
            {
                throw new InvalidOperationException("Channel release is not a published stable release.");
            }

            GitHubReleaseAssetInfo asset = ReadRequiredAsset(root, channel.AssetName);
            return new GitHubReleaseInfo
            {
                Tag = tag,
                Name = ReadOptionalString(root, "name", "Release"),
                Body = ReadOptionalString(root, "body", "Release"),
                HtmlUrl = ReadRequiredHttpsUrl(root, "html_url", "Release"),
                PublishedAt = ReadPublishedAt(root),
                AssetName = asset.Name,
                AssetDownloadUrl = asset.DownloadUrl,
                AssetSize = asset.Size
            };
        }

        private static GitHubReleaseAssetInfo ReadRequiredAsset(JsonElement root, string assetName)
        {
            JsonElement assets = ReadRequiredProperty(root, "assets", "Release");
            if (assets.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Release field 'assets' is not an array.");
            }

            foreach (JsonElement asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string candidateName = ReadOptionalString(asset, "name", "Release asset");
                if (!string.Equals(candidateName, assetName, StringComparison.Ordinal))
                {
                    continue;
                }

                return new GitHubReleaseAssetInfo
                {
                    Name = candidateName,
                    DownloadUrl = ReadRequiredHttpsUrl(
                        asset,
                        "browser_download_url",
                        "Release asset '" + assetName + "'"),
                    Size = ReadOptionalInt64(asset, "size", "Release asset '" + assetName + "'")
                };
            }

            throw new InvalidOperationException(
                "Release does not contain channel asset '" + assetName + "'.");
        }

        private static JsonElement ReadRequiredProperty(
            JsonElement element,
            string name,
            string label)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                throw new InvalidOperationException(label + " is missing '" + name + "'.");
            }

            return value;
        }

        private static string ReadRequiredString(JsonElement element, string name, string label)
        {
            JsonElement value = ReadRequiredProperty(element, name, label);
            if (value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new InvalidOperationException(label + " field '" + name + "' is not a non-empty string.");
            }

            return value.GetString();
        }

        private static string ReadOptionalString(JsonElement element, string name, string label)
        {
            if (!element.TryGetProperty(name, out JsonElement value) ||
                value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(label + " field '" + name + "' is not a string.");
            }

            return value.GetString();
        }

        private static int ReadRequiredInt32(JsonElement element, string name, string label)
        {
            JsonElement value = ReadRequiredProperty(element, name, label);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
            {
                throw new InvalidOperationException(label + " field '" + name + "' is not an integer.");
            }

            return result;
        }

        private static bool ReadRequiredBoolean(JsonElement element, string name, string label)
        {
            JsonElement value = ReadRequiredProperty(element, name, label);
            if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
            {
                throw new InvalidOperationException(label + " field '" + name + "' is not a boolean.");
            }

            return value.GetBoolean();
        }

        private static long? ReadOptionalInt64(JsonElement element, string name, string label)
        {
            if (!element.TryGetProperty(name, out JsonElement value) ||
                value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long result))
            {
                throw new InvalidOperationException(label + " field '" + name + "' is not an integer.");
            }

            return result;
        }

        private static Version ReadOptionalVersion(JsonElement element, string name, string channelId)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.String ||
                !Version.TryParse(value.GetString(), out Version version))
            {
                throw new InvalidOperationException(
                    "Channel '" + channelId + "' field '" + name + "' is not a valid Windows version.");
            }

            return version;
        }

        private static string ReadRequiredHttpsUrl(JsonElement element, string name, string label)
        {
            string value = ReadRequiredString(element, name, label);
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(label + " field '" + name + "' is not an HTTPS URL.");
            }

            return value;
        }

        private static DateTime? ReadPublishedAt(JsonElement root)
        {
            string value = ReadOptionalString(root, "published_at", "Release");
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime publishedAt))
            {
                return publishedAt;
            }

            throw new InvalidOperationException("Release field 'published_at' is not a valid date.");
        }

        private static SemanticVersion ParseSemanticVersion(string value, string label)
        {
            if (SemanticVersion.TryParse(value, out SemanticVersion version))
            {
                return version;
            }

            throw new InvalidOperationException(
                label + " is not a valid semantic version: " + value);
        }

        private static Task<string> DownloadChannelManifestJsonAsync()
        {
            return DownloadJsonAsync(ChannelManifestUrl);
        }

        private static Task<string> DownloadReleaseJsonAsync(string tag)
        {
            return DownloadJsonAsync(ReleaseApiUrlPrefix + Uri.EscapeDataString(tag));
        }

        private static async Task<string> DownloadJsonAsync(string url)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            using (handler)
            using (var client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromMilliseconds(RequestTimeoutMilliseconds);
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(GitHubAcceptHeader));
                client.DefaultRequestHeaders.UserAgent.ParseAdd(GitHubUserAgent);
                return await client.GetStringAsync(url);
            }
        }

        private sealed class UpdateChannel
        {
            public string Id { get; set; }

            public string Status { get; set; }

            public Version MinimumWindowsVersion { get; set; }

            public Version MaximumWindowsVersionExclusive { get; set; }

            public string LatestTag { get; set; }

            public string AssetName { get; set; }

            public string Sha256 { get; set; }

            public bool Matches(Version windowsVersion)
            {
                return (MinimumWindowsVersion == null ||
                        windowsVersion.CompareTo(MinimumWindowsVersion) >= 0) &&
                    (MaximumWindowsVersionExclusive == null ||
                        windowsVersion.CompareTo(MaximumWindowsVersionExclusive) < 0);
            }
        }

        private sealed class GitHubReleaseAssetInfo
        {
            public string Name { get; set; }

            public string DownloadUrl { get; set; }

            public long? Size { get; set; }
        }

        private readonly struct SemanticVersion : IComparable<SemanticVersion>
        {
            private readonly int _major;
            private readonly int _minor;
            private readonly int _patch;

            private SemanticVersion(int major, int minor, int patch)
            {
                _major = major;
                _minor = minor;
                _patch = patch;
            }

            public int CompareTo(SemanticVersion other)
            {
                int result = _major.CompareTo(other._major);
                if (result == 0)
                {
                    result = _minor.CompareTo(other._minor);
                }

                return result == 0 ? _patch.CompareTo(other._patch) : result;
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

            public static bool TryParse(string value, out SemanticVersion version)
            {
                version = default;
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                string normalized = value.Trim();
                if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(1);
                }

                int suffixIndex = normalized.IndexOfAny(new[] { '-', '+' });
                if (suffixIndex >= 0)
                {
                    normalized = normalized.Substring(0, suffixIndex);
                }

                string[] segments = normalized.Split('.');
                if (segments.Length != 3 ||
                    !int.TryParse(segments[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
                    !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
                    !int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
                {
                    return false;
                }

                version = new SemanticVersion(major, minor, patch);
                return true;
            }
        }
    }
}
