using System;
using System.Collections.Generic;

namespace readboard
{
    internal static class FoxMatchBarSeatResolver
    {
        private const double OcrDirectorySimilarityThreshold = 0.8;

        public static AutoPlayColorResolution Resolve(
            string savedFoxNickname,
            string leftOcrFragment,
            string rightOcrFragment,
            IEnumerable<string> foxNicknameDirectory)
        {
            List<string> directory = CopyDirectory(foxNicknameDirectory);
            string leftName = UniqueDirectoryName(leftOcrFragment, directory);
            string rightName = UniqueDirectoryName(rightOcrFragment, directory);
            bool leftIsMe = EqualsAfterDecorations(savedFoxNickname, leftName);
            bool rightIsMe = EqualsAfterDecorations(savedFoxNickname, rightName);

            if (leftIsMe && !rightIsMe)
                return AutoPlayColorResolution.Known("white", AutoPlayColorStatus.RecognizedWhite);
            if (rightIsMe && !leftIsMe)
                return AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack);

            return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.NicknameNotMatched);
        }

        public static string SnapToDirectory(string ocrFragment, IEnumerable<string> foxNicknameDirectory)
        {
            return UniqueDirectoryName(ocrFragment, CopyDirectory(foxNicknameDirectory));
        }

        private static List<string> CopyDirectory(IEnumerable<string> foxNicknameDirectory)
        {
            List<string> directory = new List<string>();
            if (foxNicknameDirectory == null)
                return directory;

            foreach (string name in foxNicknameDirectory)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    directory.Add(name);
            }

            return directory;
        }

        private static string UniqueDirectoryName(string ocrFragment, List<string> directory)
        {
            string ocr = StripDecorations(ocrFragment);
            if (ocr.Length == 0)
                return null;

            string match = null;
            foreach (string name in directory)
            {
                string candidate = StripDecorations(name);
                if (candidate.Length == 0)
                    continue;
                if (Similarity(ocr, candidate) < OcrDirectorySimilarityThreshold)
                    continue;
                if (match != null)
                    return null;
                match = name;
            }

            return match;
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

        private static double Similarity(string left, string right)
        {
            if (left.Length == 0 && right.Length == 0)
                return 1;
            if (left.Length == 0 || right.Length == 0)
                return 0;

            int distance = Levenshtein(left, right);
            int longest = left.Length > right.Length ? left.Length : right.Length;
            return 1d - (distance / (double)longest);
        }

        private static int Levenshtein(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.Ordinal))
                return 0;

            int[] previous = new int[right.Length + 1];
            int[] current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++)
                previous[j] = j;

            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                char leftChar = left[i - 1];
                for (int j = 1; j <= right.Length; j++)
                {
                    int substitution = previous[j - 1] + (leftChar == right[j - 1] ? 0 : 1);
                    int deletion = previous[j] + 1;
                    int insertion = current[j - 1] + 1;
                    int best = substitution;
                    if (deletion < best)
                        best = deletion;
                    if (insertion < best)
                        best = insertion;
                    current[j] = best;
                }

                int[] swap = previous;
                previous = current;
                current = swap;
            }

            return previous[right.Length];
        }
    }
}
