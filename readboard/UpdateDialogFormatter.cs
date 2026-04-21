using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace readboard
{
    internal static class UpdateDialogFormatter
    {
        private static readonly Regex VersionPattern = new Regex(
            @"^v?(?<core>\d+(?:\.\d+){1,3})(?<suffix>(?:[-+][0-9A-Za-z\.-]+)*)$",
            RegexOptions.Compiled);
        private static readonly Regex MarkdownLinkPattern = new Regex(
            @"\[(?<text>[^\]]+)\]\((?<url>[^)]+)\)",
            RegexOptions.Compiled);
        private static readonly Regex HeadingPattern = new Regex(
            @"^\s{0,3}#{1,6}\s*",
            RegexOptions.Compiled);
        private static readonly Regex BulletPattern = new Regex(
            @"^\s*[*+-]\s+",
            RegexOptions.Compiled);
        private static readonly Regex OrderedListPattern = new Regex(
            @"^\s*\d+\.\s+",
            RegexOptions.Compiled);

        public static string FormatVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string trimmedValue = value.Trim();
            Match match = VersionPattern.Match(trimmedValue);
            if (!match.Success)
            {
                return trimmedValue;
            }

            Version parsedVersion;
            if (!TryCreateVersion(match.Groups["core"].Value, out parsedVersion))
            {
                return trimmedValue;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "v{0}.{1}.{2}{3}",
                parsedVersion.Major,
                parsedVersion.Minor,
                parsedVersion.Build < 0 ? 0 : parsedVersion.Build,
                match.Groups["suffix"].Value);
        }

        public static string FormatReleaseDate(DateTime? publishedAt)
        {
            if (!publishedAt.HasValue)
            {
                return null;
            }

            DateTime timestamp = NormalizeTimestamp(publishedAt.Value).ToUniversalTime();
            return timestamp.ToString(
                "yyyy-MM-dd HH:mm 'UTC'",
                CultureInfo.InvariantCulture);
        }

        public static string FormatReleaseNotes(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] rawLines = normalized.Split('\n');
            var formattedLines = new List<string>(rawLines.Length);
            bool lastLineWasBlank = false;

            foreach (string rawLine in rawLines)
            {
                string line = FormatReleaseNotesLine(rawLine);
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (formattedLines.Count > 0 && !lastLineWasBlank)
                    {
                        formattedLines.Add(string.Empty);
                        lastLineWasBlank = true;
                    }

                    continue;
                }

                formattedLines.Add(line);
                lastLineWasBlank = false;
            }

            TrimTrailingBlankLines(formattedLines);
            if (formattedLines.Count == 0)
            {
                return null;
            }

            return string.Join(Environment.NewLine, formattedLines);
        }

        private static DateTime NormalizeTimestamp(DateTime timestamp)
        {
            return timestamp.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
                : timestamp;
        }

        private static bool TryCreateVersion(string value, out Version version)
        {
            version = null;
            try
            {
                version = new Version(value);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static string FormatReleaseNotesLine(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                return string.Empty;
            }

            string trimmedLine = rawLine.Trim();
            string content = SimplifyInlineMarkdown(trimmedLine);
            if (content.Length == 0)
            {
                return string.Empty;
            }

            if (HeadingPattern.IsMatch(trimmedLine))
            {
                return HeadingPattern.Replace(content, string.Empty).Trim();
            }

            if (BulletPattern.IsMatch(trimmedLine))
            {
                return "- " + BulletPattern.Replace(content, string.Empty).Trim();
            }

            if (OrderedListPattern.IsMatch(trimmedLine))
            {
                string prefix = OrderedListPattern.Match(trimmedLine).Value.Trim();
                string orderedContent = SimplifyInlineMarkdown(
                    OrderedListPattern.Replace(trimmedLine, string.Empty)).Trim();
                return prefix + " " + orderedContent;
            }

            if (trimmedLine.StartsWith("> ", StringComparison.Ordinal))
            {
                return SimplifyInlineMarkdown(trimmedLine.Substring(2)).Trim();
            }

            return content.Trim();
        }

        private static string SimplifyInlineMarkdown(string value)
        {
            string withoutLinks = MarkdownLinkPattern.Replace(
                value,
                match => BuildLinkReplacement(
                    match.Groups["text"].Value,
                    match.Groups["url"].Value));

            var builder = new StringBuilder(withoutLinks.Length);
            for (int i = 0; i < withoutLinks.Length; i++)
            {
                char current = withoutLinks[i];
                if (current == '`')
                {
                    continue;
                }

                if ((current == '*' || current == '_') &&
                    IsRepeatedMarker(withoutLinks, i))
                {
                    i++;
                    continue;
                }

                if (current == '~' && IsRepeatedMarker(withoutLinks, i))
                {
                    i++;
                    continue;
                }

                builder.Append(current);
            }

            return builder.ToString().Trim();
        }

        private static bool IsRepeatedMarker(string value, int index)
        {
            return index + 1 < value.Length && value[index + 1] == value[index];
        }

        private static string BuildLinkReplacement(string text, string url)
        {
            string trimmedText = text == null ? string.Empty : text.Trim();
            string trimmedUrl = url == null ? string.Empty : url.Trim();

            if (trimmedText.Length == 0)
            {
                return trimmedUrl;
            }

            if (trimmedUrl.Length == 0)
            {
                return trimmedText;
            }

            return trimmedText + " (" + trimmedUrl + ")";
        }

        private static void TrimTrailingBlankLines(IList<string> lines)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    return;
                }

                lines.RemoveAt(i);
            }
        }
    }
}
