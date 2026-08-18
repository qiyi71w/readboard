using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace readboard
{
    internal sealed class SemanticMessage
    {
        public SemanticMessage(
            string key,
            IEnumerable<object> arguments = null,
            string diagnosticDetail = null,
            string level = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("A semantic message key is required.", "key");
            if (level != null && string.IsNullOrWhiteSpace(level))
                throw new ArgumentException("A semantic message level is required.", "level");

            Key = key;
            Arguments = new List<object>(arguments ?? Array.Empty<object>()).AsReadOnly();
            DiagnosticDetail = diagnosticDetail;
            Level = level;
        }

        public string Key { get; private set; }
        public IReadOnlyList<object> Arguments { get; private set; }
        public string DiagnosticDetail { get; private set; }
        public string Level { get; private set; }

        public static SemanticMessage Create(string key, params object[] arguments)
        {
            return new SemanticMessage(key, arguments);
        }

        public static SemanticMessage CreateWithDiagnostic(
            string key,
            string diagnosticDetail,
            params object[] arguments)
        {
            return new SemanticMessage(key, arguments, diagnosticDetail);
        }

        public static SemanticMessage CreateLog(
            string level,
            string key,
            params object[] arguments)
        {
            return new SemanticMessage(key, arguments, null, level);
        }

        public static SemanticMessage CreateLogWithDiagnostic(
            string level,
            string key,
            string diagnosticDetail,
            params object[] arguments)
        {
            return new SemanticMessage(key, arguments, diagnosticDetail, level);
        }
    }

    internal static class SemanticMessageResolver
    {
        private static readonly Regex PlaceholderPattern = new Regex(
            "\\{(\\d+)(?:,[^}]*)?(?::[^}]*)?\\}",
            RegexOptions.CultureInvariant);

        public static string Resolve(
            SemanticMessage message,
            Func<string, string> getLocalizedText,
            Func<string, string> getDefaultText)
        {
            if (message == null)
                return null;

            string localized = getLocalizedText == null
                ? null
                : getLocalizedText(message.Key);
            string defaultText = getDefaultText == null
                ? null
                : getDefaultText(message.Key);
            string template = ResolveText(message.Key, localized, defaultText);

            string result;
            if (!TryFormat(template, message.Arguments, out result))
            {
                template = ResolveText(message.Key, defaultText, null);
                if (!TryFormat(template, message.Arguments, out result))
                    result = message.Key;
            }

            if (string.IsNullOrWhiteSpace(message.DiagnosticDetail))
                return result;
            return string.IsNullOrWhiteSpace(result)
                ? message.DiagnosticDetail
                : result + ": " + message.DiagnosticDetail;
        }

        public static string ResolveText(
            string key,
            string localizedText,
            string defaultText)
        {
            string fallback = string.IsNullOrWhiteSpace(defaultText)
                ? key
                : defaultText;
            if (string.IsNullOrWhiteSpace(localizedText))
                return fallback;
            if (!string.IsNullOrWhiteSpace(defaultText)
                && !HasSameFormatPlaceholders(localizedText, defaultText))
            {
                return fallback;
            }
            return IsWellFormedFormatTemplate(localizedText)
                ? localizedText
                : fallback;
        }

        internal static bool HasSameFormatPlaceholders(string value, string expected)
        {
            Dictionary<string, int> valueCounts = CountFormatPlaceholders(value);
            Dictionary<string, int> expectedCounts = CountFormatPlaceholders(expected);
            if (valueCounts.Count != expectedCounts.Count)
                return false;
            foreach (KeyValuePair<string, int> entry in valueCounts)
            {
                int expectedCount;
                if (!expectedCounts.TryGetValue(entry.Key, out expectedCount)
                    || expectedCount != entry.Value)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TryFormat(
            string template,
            IReadOnlyList<object> arguments,
            out string result)
        {
            object[] values = new object[arguments == null ? 0 : arguments.Count];
            for (int index = 0; index < values.Length; index++)
                values[index] = arguments[index];
            try
            {
                result = string.Format(CultureInfo.CurrentCulture, template ?? string.Empty, values);
                return true;
            }
            catch (FormatException)
            {
                result = null;
                return false;
            }
        }

        private static bool IsWellFormedFormatTemplate(string value)
        {
            MatchCollection matches = PlaceholderPattern.Matches(value ?? string.Empty);
            int maximumIndex = -1;
            for (int index = 0; index < matches.Count; index++)
            {
                int placeholderIndex;
                if (int.TryParse(matches[index].Groups[1].Value, out placeholderIndex))
                    maximumIndex = Math.Max(maximumIndex, placeholderIndex);
            }

            object[] values = new object[maximumIndex + 1];
            try
            {
                string.Format(CultureInfo.CurrentCulture, value ?? string.Empty, values);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static Dictionary<string, int> CountFormatPlaceholders(string value)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            MatchCollection matches = PlaceholderPattern.Matches(value ?? string.Empty);
            foreach (Match match in matches)
            {
                string placeholder = match.Value;
                int count;
                counts.TryGetValue(placeholder, out count);
                counts[placeholder] = count + 1;
            }
            return counts;
        }
    }
}
