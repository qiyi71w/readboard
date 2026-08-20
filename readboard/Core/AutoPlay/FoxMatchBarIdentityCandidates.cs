using System;
using System.Collections.Generic;

namespace readboard
{
    internal static class FoxMatchBarIdentityCandidates
    {
        public static IList<FoxIdentityCandidate> Build(IEnumerable<FoxPlayerListEntry> players)
        {
            List<FoxIdentityCandidate> candidates = new List<FoxIdentityCandidate>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (players == null)
                return candidates;

            foreach (FoxPlayerListEntry player in players)
            {
                if (player == null || !FoxMatchBarSeatResolver.IsPlayerNickname(player.Nickname))
                    continue;
                if (!seen.Add(player.Nickname))
                    continue;

                candidates.Add(new FoxIdentityCandidate(
                    player.Nickname,
                    SemanticMessage.Create("WebView_candidateNickname", player.Nickname),
                    player.Nickname,
                    null));
            }

            return candidates;
        }
    }
}
