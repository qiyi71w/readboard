using System;

namespace readboard
{
    public sealed class GitHubReleaseInfo
    {
        public string Tag { get; internal set; }

        public string Name { get; internal set; }

        public string Body { get; internal set; }

        public string HtmlUrl { get; internal set; }

        public DateTime? PublishedAt { get; internal set; }
    }
}
