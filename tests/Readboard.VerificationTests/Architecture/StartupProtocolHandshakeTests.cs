using System;
using System.Collections.Generic;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Architecture
{
    public sealed class StartupProtocolHandshakeTests
    {
        [Fact]
        public void Run_CompletesAllStartupGatesInOrder()
        {
            List<string> events = new List<string>();

            bool completed = StartupProtocolHandshake.Run(
                () => Record(events, "start", true),
                () => false,
                () => events.Add("drain"),
                () => events.Add("ready"),
                () => events.Add("replay"));

            Assert.True(completed);
            Assert.Equal(
                new[] { "start", "drain", "ready", "drain", "replay", "drain" },
                events);
        }

        [Fact]
        public void Run_StopsBeforeHandshakeWhenSessionStartFails()
        {
            List<string> events = new List<string>();

            bool completed = StartupProtocolHandshake.Run(
                () => Record(events, "start", false),
                () => false,
                () => events.Add("drain"),
                () => events.Add("ready"),
                () => events.Add("replay"));

            Assert.False(completed);
            Assert.Equal(new[] { "start" }, events);
        }

        [Theory]
        [InlineData("drain-1")]
        [InlineData("ready")]
        [InlineData("drain-2")]
        [InlineData("replay")]
        public void Run_HonorsShutdownGateAfterEachStartupPhase(string shutdownAfter)
        {
            List<string> events = new List<string>();
            bool shutdownRequested = false;
            int drainCount = 0;

            bool completed = StartupProtocolHandshake.Run(
                () =>
                {
                    events.Add("start");
                    return true;
                },
                () => shutdownRequested,
                () =>
                {
                    drainCount++;
                    string drainName = "drain-" + drainCount;
                    events.Add(drainName);
                    shutdownRequested |= shutdownAfter == drainName;
                },
                () =>
                {
                    events.Add("ready");
                    shutdownRequested |= shutdownAfter == "ready";
                },
                () =>
                {
                    events.Add("replay");
                    shutdownRequested |= shutdownAfter == "replay";
                });

            Assert.False(completed);
            Assert.Contains(shutdownAfter, events);
            Assert.DoesNotContain("drain-4", events);
            if (shutdownAfter == "drain-1")
                Assert.Equal(new[] { "start", "drain-1" }, events);
            else if (shutdownAfter == "ready" || shutdownAfter == "drain-2")
                Assert.Equal(new[] { "start", "drain-1", "ready", "drain-2" }, events);
            else
                Assert.Equal(
                    new[] { "start", "drain-1", "ready", "drain-2", "replay", "drain-3" },
                    events);
        }

        private static bool Record(List<string> events, string value, bool result)
        {
            events.Add(value);
            return result;
        }
    }
}
