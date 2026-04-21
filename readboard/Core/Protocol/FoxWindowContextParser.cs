using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace readboard
{
    internal static class FoxWindowContextParser
    {
        private static readonly Regex RoomTokenPattern =
            new Regex(@">\s*([^\]\s>]+号)", RegexOptions.Compiled);

        private static readonly Regex LiveMovePattern =
            new Regex(@"\[第\s*(\d+)\s*手\]", RegexOptions.Compiled);

        private static readonly Regex RecordCurrentPattern =
            new Regex(@"第\s*(\d+)\s*手", RegexOptions.Compiled);

        private static readonly Regex RecordTotalPattern =
            new Regex(@"总\s*(\d+)\s*手", RegexOptions.Compiled);

        public static FoxWindowContext Parse(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return FoxWindowContext.Unknown();

            Match roomMatch = RoomTokenPattern.Match(title);
            if (roomMatch.Success)
            {
                return new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    RoomToken = roomMatch.Groups[1].Value,
                    LiveTitleMove = ParseNullableInt(LiveMovePattern.Match(title))
                };
            }

            Match totalMatch = RecordTotalPattern.Match(title);
            Match currentMatch = RecordCurrentPattern.Match(title);
            if (totalMatch.Success || currentMatch.Success)
            {
                int? totalMove = ParseNullableInt(totalMatch);
                int? currentMove = ParseNullableInt(currentMatch) ?? totalMove;
                return new FoxWindowContext
                {
                    Kind = FoxWindowKind.RecordView,
                    RecordCurrentMove = currentMove,
                    RecordTotalMove = totalMove,
                    RecordAtEnd = !currentMatch.Success && totalMove.HasValue,
                    TitleFingerprint = Fingerprint(title)
                };
            }

            return FoxWindowContext.Unknown();
        }

        private static int? ParseNullableInt(Match match)
        {
            if (!match.Success)
                return null;

            return int.Parse(match.Groups[1].Value);
        }

        private static string Fingerprint(string title)
        {
            string normalized = RecordCurrentPattern.Replace(
                RecordTotalPattern.Replace(title, "总#手"),
                "第#手");

            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                StringBuilder builder = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes)
                    builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
