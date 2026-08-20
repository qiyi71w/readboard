using System.Collections.Generic;

namespace readboard
{
    internal static class FoxMatchBarIdentityCandidates
    {
        public const string LeftSeatId = "left";
        public const string RightSeatId = "right";

        public static IList<FoxIdentityCandidate> Build(
            string leftOcrFragment,
            string rightOcrFragment,
            IEnumerable<string> foxNicknameDirectory)
        {
            List<FoxIdentityCandidate> candidates = new List<FoxIdentityCandidate>();
            AddSeat(
                candidates,
                LeftSeatId,
                "WebView_candidateLeftSeat",
                FoxMatchBarSeatResolver.SnapToDirectory(leftOcrFragment, foxNicknameDirectory));
            AddSeat(
                candidates,
                RightSeatId,
                "WebView_candidateRightSeat",
                FoxMatchBarSeatResolver.SnapToDirectory(rightOcrFragment, foxNicknameDirectory));
            return candidates;
        }

        private static void AddSeat(
            List<FoxIdentityCandidate> candidates,
            string id,
            string labelKey,
            string exactName)
        {
            if (string.IsNullOrWhiteSpace(exactName))
                return;

            candidates.Add(new FoxIdentityCandidate(
                id,
                SemanticMessage.Create(labelKey, exactName),
                exactName,
                null));
        }
    }
}
