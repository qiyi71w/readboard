using System;
using System.Text;

namespace readboard
{
    internal enum MainWindowTitleDisplayMode
    {
        Hidden = 0,
        Syncing = 1,
        RetainedSnapshot = 2
    }

    internal static class MainWindowTitleFormatter
    {
        public static string FormatBaseTitle(string baseTitle, string releaseVersion)
        {
            string normalizedBaseTitle = Normalize(baseTitle, "readboard");
            string normalizedVersion = NormalizeVersion(releaseVersion);
            return string.IsNullOrEmpty(normalizedVersion)
                ? normalizedBaseTitle
                : normalizedBaseTitle + " " + normalizedVersion;
        }

        public static string Format(
            string baseTitle,
            MainWindowTitleDisplayMode displayMode,
            bool hasWindowHandle,
            FoxWindowContext context,
            string foxTag,
            string roomTag,
            string recordTag,
            string syncingTag,
            string titleMissingTag,
            string recordEndTag,
            string singleMoveFormat,
            string recordMoveFormat)
        {
            string normalizedBaseTitle = Normalize(baseTitle, "readboard");
            if (displayMode == MainWindowTitleDisplayMode.Hidden)
                return normalizedBaseTitle;

            FoxWindowContext normalizedContext = FoxWindowContext.CopyOf(context);
            if (!hasWindowHandle || normalizedContext.Kind == FoxWindowKind.Unknown)
                return FormatMissingTitle(normalizedBaseTitle, displayMode, foxTag, syncingTag, titleMissingTag);

            if (normalizedContext.Kind == FoxWindowKind.LiveRoom)
            {
                return normalizedBaseTitle
                    + LeadingTag(roomTag)
                    + Tag(normalizedContext.RoomToken)
                    + FormatSingleMove(singleMoveFormat, normalizedContext.LiveTitleMove);
            }

            if (normalizedContext.Kind == FoxWindowKind.RecordView)
            {
                StringBuilder builder = new StringBuilder(normalizedBaseTitle);
                builder.Append(LeadingTag(recordTag));
                builder.Append(FormatRecordMove(recordMoveFormat, singleMoveFormat, normalizedContext.RecordCurrentMove, normalizedContext.RecordTotalMove));
                if (normalizedContext.RecordAtEnd)
                    builder.Append(Tag(recordEndTag));
                return builder.ToString();
            }

            return FormatMissingTitle(normalizedBaseTitle, displayMode, foxTag, syncingTag, titleMissingTag);
        }

        private static string FormatMissingTitle(
            string normalizedBaseTitle,
            MainWindowTitleDisplayMode displayMode,
            string foxTag,
            string syncingTag,
            string titleMissingTag)
        {
            StringBuilder builder = new StringBuilder(normalizedBaseTitle);
            builder.Append(LeadingTag(foxTag));
            if (displayMode == MainWindowTitleDisplayMode.Syncing)
                builder.Append(Tag(syncingTag));
            builder.Append(Tag(titleMissingTag));
            return builder.ToString();
        }

        private static string FormatSingleMove(string singleMoveFormat, int? moveNumber)
        {
            return moveNumber.HasValue ? Tag(FormatMoveText(singleMoveFormat, "{0}", moveNumber.Value)) : string.Empty;
        }

        private static string FormatRecordMove(string recordMoveFormat, string singleMoveFormat, int? currentMove, int? totalMove)
        {
            if (!currentMove.HasValue && !totalMove.HasValue)
                return string.Empty;
            if (currentMove.HasValue && totalMove.HasValue)
                return Tag(FormatMoveText(recordMoveFormat, "{0}/{1}", currentMove.Value, totalMove.Value));

            int moveNumber = currentMove ?? totalMove.Value;
            return Tag(FormatMoveText(singleMoveFormat, "{0}", moveNumber));
        }

        private static string FormatMoveText(string format, string fallbackFormat, params object[] values)
        {
            return string.Format(Normalize(format, fallbackFormat), values);
        }

        private static string Tag(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : "[" + value.Trim() + "]";
        }

        private static string LeadingTag(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : " " + Tag(value);
        }

        private static string Normalize(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string NormalizeVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string trimmedValue = value.Trim();
            if (trimmedValue.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                return "v" + trimmedValue.Substring(1);
            return "v" + trimmedValue;
        }
    }
}
