using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Logging
{
    public sealed class LoggingJsonlSerializerTests
    {
        [Fact]
        public void Serialize_EmitsOneUtf8JsonLineWithTaggedFields()
        {
            LoggingRecord record = CreateRecord();
            string line;
            Assert.True(LoggingJsonlSerializer.TrySerialize(record, out line));
            Assert.DoesNotContain("\n", line);
            Assert.DoesNotContain("\r", line);

            using (JsonDocument document = JsonDocument.Parse(line))
            {
                JsonElement root = document.RootElement;
                Assert.Equal("2026-08-21T17:03:00.123Z", root.GetProperty("ts").GetString());
                Assert.Equal("INFO", root.GetProperty("level").GetString());
                Assert.Equal("app", root.GetProperty("stream").GetString());
                Assert.Equal("sync.frame.accepted", root.GetProperty("eventId").GetString());
                Assert.Equal("recognition", root.GetProperty("module").GetString());
                AssertTagged(root.GetProperty("hostSessionId"), "dGVzdEhvc3RTZXNzaW9u", "sessionId");
                AssertTagged(root.GetProperty("processSessionId"), "dGVzdFByb2Nlc3NJRA", "sessionId");
                Assert.Equal(19, root.GetProperty("fields").GetProperty("boardSize").GetProperty("value").GetInt32());
                Assert.Equal("safe", root.GetProperty("fields").GetProperty("boardSize").GetProperty("privacy").GetString());
                Assert.Equal("sync.frame.accepted", root.GetProperty("semantic").GetProperty("key").GetString());
                Assert.Equal(1, root.GetProperty("semantic").GetProperty("args").GetProperty("count").GetProperty("value").GetInt32());
            }
        }

        [Fact]
        public void Serialize_RejectsSecretBeforeAnyBytes()
        {
            LoggingRecord record = CreateRecord();
            record.Fields["token"] = LoggingField.Tagged("super-secret-value", LoggingPrivacy.Secret);

            string line;
            Assert.False(LoggingJsonlSerializer.TrySerialize(record, out line));
            Assert.Null(line);
            Assert.True(LoggingJsonlSerializer.ContainsSecret(record));
        }

        [Fact]
        public void Serialize_StoresSemanticKeyNotLocalizedText()
        {
            LoggingRecord record = CreateRecord();
            record.SemanticKey = "test.range";
            record.SemanticArgs = new Dictionary<string, LoggingField>
            {
                { "0", LoggingField.Safe(20) },
                { "1", LoggingField.Safe(255) }
            };

            string line;
            Assert.True(LoggingJsonlSerializer.TrySerialize(record, out line));
            Assert.Contains("\"key\":\"test.range\"", line);
            Assert.DoesNotContain("Enter an integer", line);
            Assert.DoesNotContain("请输入", line);
        }

        [Fact]
        public void Serialize_SanitizesExceptionText()
        {
            LoggingRecord record = CreateRecord();
            record.Exception = new InvalidOperationException(
                "cookie=abc\r\npassword=hunter2\nmachinekey=XYZ\n" + new string('x', 4000));

            string line;
            Assert.True(LoggingJsonlSerializer.TrySerialize(record, out line));
            Assert.DoesNotContain("\n", line);
            Assert.DoesNotContain("hunter2", line);
            Assert.DoesNotContain("abc", line);
            Assert.DoesNotContain("XYZ", line);
            Assert.Contains("cookie=redacted", line);
            JsonElement exception = JsonDocument.Parse(line).RootElement.GetProperty("fields").GetProperty("exception");
            Assert.True(exception.GetProperty("value").GetString().Length <= LoggingLimits.MaxExceptionChars);
            Assert.Equal("safe", exception.GetProperty("privacy").GetString());
        }
        [Fact]
        public void Serialize_SanitizesAuthorizationBearerTokenBytes()
        {
            LoggingRecord record = CreateRecord();
            record.Exception = new InvalidOperationException(
                "Authorization: Bearer abc.secret-token Authorization: Basic dXNlcjpwYXNz");

            string line;
            Assert.True(LoggingJsonlSerializer.TrySerialize(record, out line));
            Assert.Contains("authorization=redacted", line);
            Assert.DoesNotContain("abc.secret-token", line);
            Assert.DoesNotContain("Bearer abc", line);
            Assert.DoesNotContain("dXNlcjpwYXNz", line);
            Assert.DoesNotContain("Basic dXNlcjpwYXNz", line);
        }


        private static LoggingRecord CreateRecord()
        {
            return new LoggingRecord
            {
                TimestampUtc = new DateTime(2026, 8, 21, 17, 3, 0, 123, DateTimeKind.Utc),
                Level = LogLevel.Information,
                Stream = LoggingStreams.App,
                EventId = "sync.frame.accepted",
                Module = "recognition",
                HostSessionId = "dGVzdEhvc3RTZXNzaW9u",
                ProcessSessionId = "dGVzdFByb2Nlc3NJRA",
                Fields = new Dictionary<string, LoggingField>
                {
                    { "boardSize", LoggingField.Safe(19) }
                },
                SemanticKey = "sync.frame.accepted",
                SemanticArgs = new Dictionary<string, LoggingField>
                {
                    { "count", LoggingField.Safe(1) }
                }
            };
        }

        private static void AssertTagged(JsonElement element, string value, string privacy)
        {
            Assert.Equal(value, element.GetProperty("value").GetString());
            Assert.Equal(privacy, element.GetProperty("privacy").GetString());
        }
    }
}
