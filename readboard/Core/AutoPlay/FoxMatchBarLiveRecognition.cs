using System;
using System.Collections.Generic;

namespace readboard
{
    internal sealed class FoxPlayerListEntry
    {
        public FoxPlayerListEntry(string nickname, AutoPlayColorResolution stone)
        {
            Nickname = nickname ?? string.Empty;
            Stone = stone ?? AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);
        }

        public string Nickname { get; private set; }

        public AutoPlayColorResolution Stone { get; private set; }
    }

    internal sealed class FoxMatchBarReading
    {
        public static FoxMatchBarReading Empty { get; } = new FoxMatchBarReading(
            Array.Empty<FoxPlayerListEntry>(),
            string.Empty);

        public FoxMatchBarReading(IEnumerable<FoxPlayerListEntry> players)
            : this(players, string.Empty)
        {
        }

        public FoxMatchBarReading(IEnumerable<FoxPlayerListEntry> players, string diagnostic)
        {
            List<FoxPlayerListEntry> copy = new List<FoxPlayerListEntry>();
            if (players != null)
            {
                foreach (FoxPlayerListEntry player in players)
                {
                    if (player == null || string.IsNullOrWhiteSpace(player.Nickname))
                        continue;
                    copy.Add(player);
                }
            }

            Players = copy;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public IList<FoxPlayerListEntry> Players { get; private set; }

        public string Diagnostic { get; private set; }
    }

    internal sealed class FoxMatchBarLiveRecognition
    {
        public const int RetryIntervalMs = 1000;

        private IntPtr lastWindowHandle;
        private string lastRoomSignature = string.Empty;
        private string lastIdentitySignature = string.Empty;
        private DateTime lastAttemptUtc = DateTime.MinValue;
        private bool hasSample;

        public AutoPlayColorResolution CurrentResolution { get; private set; } =
            AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);

        public FoxMatchBarReading CurrentReading { get; private set; } = FoxMatchBarReading.Empty;

        public bool NeedsSample(
            IntPtr windowHandle,
            string roomSignature,
            string identitySignature,
            DateTime nowUtc,
            bool forceResample)
        {
            if (forceResample || !hasSample)
                return true;
            if (lastWindowHandle != windowHandle)
                return true;
            if (!string.Equals(lastRoomSignature, Normalize(roomSignature), StringComparison.Ordinal))
                return true;
            if (!string.Equals(lastIdentitySignature, Normalize(identitySignature), StringComparison.Ordinal))
                return true;
            if (CurrentResolution != null && CurrentResolution.IsKnown)
                return false;
            return (nowUtc - lastAttemptUtc).TotalMilliseconds >= RetryIntervalMs;
        }

        public AutoPlayColorResolution AcceptSample(
            IntPtr windowHandle,
            string roomSignature,
            string identitySignature,
            DateTime nowUtc,
            FoxMatchBarReading reading)
        {
            reading = reading ?? FoxMatchBarReading.Empty;
            CurrentReading = reading;
            CurrentResolution = FoxMatchBarSeatResolver.Resolve(
                identitySignature,
                reading.Players);
            lastWindowHandle = windowHandle;
            lastRoomSignature = Normalize(roomSignature);
            lastIdentitySignature = Normalize(identitySignature);
            lastAttemptUtc = nowUtc;
            hasSample = true;
            return CurrentResolution;
        }

        public void Invalidate()
        {
            hasSample = false;
            lastWindowHandle = IntPtr.Zero;
            lastRoomSignature = string.Empty;
            lastIdentitySignature = string.Empty;
            lastAttemptUtc = DateTime.MinValue;
            CurrentResolution = AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);
            CurrentReading = FoxMatchBarReading.Empty;
        }

        private static string Normalize(string value)
        {
            return value ?? string.Empty;
        }
    }
}
