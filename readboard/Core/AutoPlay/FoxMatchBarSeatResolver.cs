using System;
using System.Collections.Generic;

namespace readboard
{
    internal static class FoxMatchBarSeatResolver
    {
        public static AutoPlayColorResolution Resolve(
            string savedFoxNickname,
            IEnumerable<FoxPlayerListEntry> players)
        {
            if (string.IsNullOrWhiteSpace(savedFoxNickname) || players == null)
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.NicknameNotMatched);

            List<FoxPlayerListEntry> list = new List<FoxPlayerListEntry>();
            foreach (FoxPlayerListEntry player in players)
            {
                if (player != null && !string.IsNullOrWhiteSpace(player.Nickname))
                    list.Add(player);
            }

            int index = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (!EqualsAfterDecorations(savedFoxNickname, list[i].Nickname))
                    continue;
                if (index >= 0)
                    return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.NicknameNotMatched);
                index = i;
            }

            if (index == 0)
                return AutoPlayColorResolution.Known("white", AutoPlayColorStatus.RecognizedWhite);
            if (index == 1)
                return AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack);

            return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.NicknameNotMatched);
        }

        public static bool IsPlayerNickname(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            string stripped = StripDecorations(name);
            return stripped.Length > 0
                && !IsAllDigits(stripped)
                && !IsRankToken(stripped)
                && !IsChromeName(name);
        }

        public static bool LooksLikeRankOrStat(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || IsChromeName(name))
                return false;
            string stripped = StripDecorations(name);
            return stripped.Length > 0 && (IsAllDigits(stripped) || IsRankToken(stripped));
        }

        public static IList<string> SelectNicknamesFollowedByRank(IEnumerable<string> names)
        {
            List<string> selected = new List<string>();
            if (names == null)
                return selected;

            List<string> copy = new List<string>();
            foreach (string name in names)
                copy.Add(name ?? string.Empty);

            for (int i = 0; i < copy.Count; i++)
            {
                if (!IsPlayerNickname(copy[i]))
                    continue;
                if (i + 1 >= copy.Count || !LooksLikeRankOrStat(copy[i + 1]))
                    continue;
                selected.Add(copy[i]);
            }

            return selected;
        }


        private static bool EqualsAfterDecorations(string savedFoxNickname, string directoryName)
        {
            if (directoryName == null)
                return false;

            string saved = StripDecorations(savedFoxNickname);
            string candidate = StripDecorations(directoryName);
            return saved.Length > 0
                && string.Equals(saved, candidate, StringComparison.Ordinal);
        }

        private static string StripDecorations(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            char[] buffer = new char[raw.Length];
            int length = 0;
            foreach (char ch in raw)
            {
                if (!char.IsLetter(ch) && !char.IsDigit(ch))
                    continue;
                buffer[length++] = char.ToLowerInvariant(ch);
            }

            return length == 0 ? string.Empty : new string(buffer, 0, length);
        }

        private static bool IsRankToken(string stripped)
        {
            if (stripped.Length < 2)
                return false;
            char last = stripped[stripped.Length - 1];
            if (last != '段' && last != '级')
                return false;
            for (int i = 0; i < stripped.Length - 1; i++)
            {
                if (!char.IsDigit(stripped[i]))
                    return false;
            }

            return true;
        }

        private static bool IsAllDigits(string stripped)
        {
            if (stripped.Length == 0)
                return false;
            for (int i = 0; i < stripped.Length; i++)
            {
                if (!char.IsDigit(stripped[i]))
                    return false;
            }

            return true;
        }

        private static bool IsChromeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;
            if (name.IndexOf("Rich Edit", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("滚动条", StringComparison.Ordinal) >= 0)
                return true;
            if (name.StartsWith("用户名", StringComparison.Ordinal))
                return true;
            if (name.StartsWith("小幅度", StringComparison.Ordinal)
                || name.StartsWith("大幅度", StringComparison.Ordinal))
                return true;
            switch (name)
            {
                case "标题":
                case "棋力":
                case "胜":
                case "负":
                case "财富":
                case "缩略":
                    return true;
                default:
                    return false;
            }
        }
    }
}
