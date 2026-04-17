using System;
using System.Collections.Generic;
using System.IO;
using readboard;

namespace Readboard.VerificationTests
{
    internal sealed class ProtocolFixtureCase
    {
        public string RawLine { get; set; }
        public ProtocolMessageKind ExpectedKind { get; set; }
        public int? ExpectedX { get; set; }
        public int? ExpectedY { get; set; }
    }

    internal sealed class ProtocolOutboundFixtureCase
    {
        public string Scenario { get; set; }
        public string RawLine { get; set; }
    }

    internal static class ProtocolFixtureCatalog
    {
        private const char Separator = '|';
        private const int MinimumColumns = 4;
        private const int MinimumOutboundColumns = 2;

        public static IReadOnlyList<ProtocolFixtureCase> LoadInboundCases()
        {
            string path = VerificationFixtureLocator.FixturePath(Path.Combine("protocol", "legacy-inbound-cases.txt"));
            List<ProtocolFixtureCase> cases = new List<ProtocolFixtureCase>();

            foreach (string rawLine in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(rawLine) || rawLine.StartsWith("#", StringComparison.Ordinal))
                    continue;

                string[] columns = rawLine.Split(Separator);
                if (columns.Length < MinimumColumns)
                    throw new InvalidDataException("Protocol fixture row is incomplete: " + rawLine);

                cases.Add(new ProtocolFixtureCase
                {
                    RawLine = columns[0],
                    ExpectedKind = (ProtocolMessageKind)Enum.Parse(typeof(ProtocolMessageKind), columns[1], ignoreCase: false),
                    ExpectedX = ParseNullableInt(columns[2]),
                    ExpectedY = ParseNullableInt(columns[3])
                });
            }

            return cases;
        }

        public static IReadOnlyList<ProtocolOutboundFixtureCase> LoadOutboundCases()
        {
            string path = VerificationFixtureLocator.FixturePath(Path.Combine("protocol", "legacy-outbound-cases.txt"));
            List<ProtocolOutboundFixtureCase> cases = new List<ProtocolOutboundFixtureCase>();

            foreach (string rawLine in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(rawLine) || rawLine.StartsWith("#", StringComparison.Ordinal))
                    continue;

                string[] columns = rawLine.Split(Separator);
                if (columns.Length < MinimumOutboundColumns)
                    throw new InvalidDataException("Outbound protocol fixture row is incomplete: " + rawLine);

                cases.Add(new ProtocolOutboundFixtureCase
                {
                    Scenario = columns[0],
                    RawLine = columns[1]
                });
            }

            return cases;
        }

        public static IReadOnlyList<string> LoadOutboundLines(string scenario)
        {
            List<string> lines = new List<string>();
            foreach (ProtocolOutboundFixtureCase fixtureCase in LoadOutboundCases())
            {
                if (string.Equals(fixtureCase.Scenario, scenario, StringComparison.Ordinal))
                    lines.Add(fixtureCase.RawLine);
            }

            return lines;
        }

        private static int? ParseNullableInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return int.Parse(value);
        }
    }
}
