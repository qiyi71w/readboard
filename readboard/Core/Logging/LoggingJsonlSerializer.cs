using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Logging;

namespace readboard
{
    internal static class LoggingSanitizer
    {
        private static readonly Regex SecretPattern = new Regex(
            "(?i)(cookie|set-cookie|token|password|passwd|machinekey|machine-key|credential|api[-_]?key)\\s*[=:]\\s*\\S+",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex AuthorizationPattern = new Regex(
            "(?i)authorization\\s*[=:]\\s*\\S+(?:\\s+\\S+)?",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex BearerPattern = new Regex(
            "(?i)\\bbearer\\s+\\S+",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex ProtocolPayloadPattern = new Regex(
            "play>[^\\s]*",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string SanitizeText(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            if (maxChars <= 0)
                maxChars = LoggingLimits.MaxFieldChars;

            string redacted = ProtocolPayloadPattern.Replace(
                BearerPattern.Replace(
                    AuthorizationPattern.Replace(
                        SecretPattern.Replace(text, "$1=redacted"),
                        "authorization=redacted"),
                    "bearer redacted"),
                "play>redacted");
            StringBuilder builder = new StringBuilder(Math.Min(redacted.Length, maxChars));
            for (int i = 0; i < redacted.Length && builder.Length < maxChars; i++)
            {
                char value = redacted[i];
                if (value == '\r' || value == '\n' || value == '\t')
                    builder.Append(' ');
                else if (value >= ' ')
                    builder.Append(value);
            }
            return builder.ToString();
        }

        public static string SanitizeException(Exception exception)
        {
            if (exception == null)
                return null;

            StringBuilder builder = new StringBuilder();
            builder.Append(exception.GetType().FullName);
            if (!string.IsNullOrEmpty(exception.Message))
            {
                builder.Append(": ");
                builder.Append(exception.Message);
            }
            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                builder.Append(' ');
                builder.Append(exception.StackTrace);
            }
            return SanitizeText(builder.ToString(), LoggingLimits.MaxExceptionChars);
        }
    }

    internal static class LoggingJsonlSerializer
    {
        public static bool ContainsSecret(LoggingRecord record)
        {
            if (record == null)
                return false;
            if (ContainsSecret(record.Fields))
                return true;
            return ContainsSecret(record.SemanticArgs);
        }

        public static bool TrySerialize(LoggingRecord record, out string line)
        {
            line = null;
            if (record == null || ContainsSecret(record))
                return false;

            JsonWriterOptions writerOptions = new JsonWriterOptions();
            writerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            using (MemoryStream buffer = new MemoryStream())
            {
                Utf8JsonWriter writer = new Utf8JsonWriter(buffer, writerOptions);
                writer.WriteStartObject();
                writer.WriteString("ts", FormatTimestamp(record.TimestampUtc));
                writer.WriteString("level", FormatLevel(record.Level));
                writer.WriteString("stream", string.IsNullOrEmpty(record.Stream) ? LoggingStreams.App : record.Stream);
                writer.WriteString("eventId", string.IsNullOrEmpty(record.EventId) ? "runtime.log" : record.EventId);
                if (!string.IsNullOrEmpty(record.Module))
                    writer.WriteString("module", record.Module);
                WriteTaggedString(writer, "hostSessionId", record.HostSessionId, LoggingPrivacy.SessionId);
                WriteTaggedString(writer, "processSessionId", record.ProcessSessionId, LoggingPrivacy.SessionId);
                WriteTaggedString(writer, "correlationId", record.CorrelationId, LoggingPrivacy.SessionId);
                WriteFields(writer, record);
                WriteSemantic(writer, record);
                WriteTail(writer, record.CrashTail);
                writer.WriteEndObject();
                writer.Flush();
                line = Encoding.UTF8.GetString(buffer.ToArray());
            }

            if (line.IndexOf('\r') >= 0 || line.IndexOf('\n') >= 0)
                line = line.Replace('\r', ' ').Replace('\n', ' ');
            return true;
        }

        private static bool ContainsSecret(IDictionary<string, LoggingField> fields)
        {
            if (fields == null)
                return false;
            foreach (KeyValuePair<string, LoggingField> entry in fields)
            {
                if (entry.Value != null && entry.Value.Privacy == LoggingPrivacy.Secret)
                    return true;
            }
            return false;
        }

        private static void WriteFields(Utf8JsonWriter writer, LoggingRecord record)
        {
            bool hasFields = record.Fields != null && record.Fields.Count > 0;
            bool hasException = record.Exception != null;
            if (!hasFields && !hasException)
                return;

            writer.WritePropertyName("fields");
            writer.WriteStartObject();
            if (record.Fields != null)
            {
                foreach (KeyValuePair<string, LoggingField> entry in record.Fields)
                    WriteField(writer, entry.Key, entry.Value);
            }
            if (hasException)
            {
                if (record.Fields == null || !record.Fields.ContainsKey("exceptionType"))
                {
                    WriteField(
                        writer,
                        "exceptionType",
                        LoggingField.Safe(record.Exception.GetType().FullName));
                }
                if (record.Fields == null || !record.Fields.ContainsKey("exception"))
                {
                    WriteField(
                        writer,
                        "exception",
                        LoggingField.Safe(LoggingSanitizer.SanitizeException(record.Exception)));
                }
            }
            writer.WriteEndObject();
        }

        private static void WriteSemantic(Utf8JsonWriter writer, LoggingRecord record)
        {
            if (string.IsNullOrEmpty(record.SemanticKey) && (record.SemanticArgs == null || record.SemanticArgs.Count == 0))
                return;

            writer.WritePropertyName("semantic");
            writer.WriteStartObject();
            writer.WriteString("key", record.SemanticKey ?? record.EventId);
            writer.WritePropertyName("args");
            writer.WriteStartObject();
            if (record.SemanticArgs != null)
            {
                foreach (KeyValuePair<string, LoggingField> entry in record.SemanticArgs)
                    WriteField(writer, entry.Key, entry.Value);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        private static void WriteTail(Utf8JsonWriter writer, IList<LoggingRecord> tail)
        {
            if (tail == null || tail.Count == 0)
                return;

            writer.WritePropertyName("tail");
            writer.WriteStartArray();
            for (int i = 0; i < tail.Count; i++)
            {
                LoggingRecord item = tail[i];
                if (item == null)
                    continue;
                writer.WriteStartObject();
                writer.WriteString("ts", FormatTimestamp(item.TimestampUtc));
                writer.WriteString("level", FormatLevel(item.Level));
                writer.WriteString("stream", string.IsNullOrEmpty(item.Stream) ? LoggingStreams.App : item.Stream);
                writer.WriteString("eventId", string.IsNullOrEmpty(item.EventId) ? "runtime.log" : item.EventId);
                if (!string.IsNullOrEmpty(item.SemanticKey))
                    writer.WriteString("semanticKey", item.SemanticKey);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static void WriteTaggedString(
            Utf8JsonWriter writer,
            string name,
            string value,
            LoggingPrivacy privacy)
        {
            if (string.IsNullOrEmpty(value))
                return;
            writer.WritePropertyName(name);
            writer.WriteStartObject();
            writer.WriteString("value", value);
            writer.WriteString("privacy", LoggingWireContract.FormatPrivacy(privacy));
            writer.WriteEndObject();
        }

        private static void WriteField(Utf8JsonWriter writer, string name, LoggingField field)
        {
            if (string.IsNullOrEmpty(name) || field == null || field.Privacy == LoggingPrivacy.Secret)
                return;

            writer.WritePropertyName(name);
            writer.WriteStartObject();
            writer.WritePropertyName("value");
            WriteValue(writer, field.Value, field.Privacy);
            writer.WriteString("privacy", LoggingWireContract.FormatPrivacy(field.Privacy));
            writer.WriteEndObject();
        }

        private static void WriteValue(Utf8JsonWriter writer, object value, LoggingPrivacy privacy)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            string text = value as string;
            if (text != null)
            {
                int cap = privacy == LoggingPrivacy.Safe
                    ? LoggingLimits.MaxFieldChars
                    : LoggingLimits.MaxFieldChars;
                writer.WriteStringValue(LoggingSanitizer.SanitizeText(text, cap));
                return;
            }
            if (value is bool)
            {
                writer.WriteBooleanValue((bool)value);
                return;
            }
            if (value is byte || value is sbyte || value is short || value is ushort
                || value is int || value is uint || value is long)
            {
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                return;
            }
            if (value is ulong)
            {
                writer.WriteNumberValue((ulong)value);
                return;
            }
            if (value is float || value is double || value is decimal)
            {
                writer.WriteNumberValue(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                return;
            }

            writer.WriteStringValue(
                LoggingSanitizer.SanitizeText(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    LoggingLimits.MaxFieldChars));
        }

        internal static string FormatTimestamp(DateTime timestampUtc)
        {
            DateTime utc = timestampUtc.Kind == DateTimeKind.Utc
                ? timestampUtc
                : timestampUtc.ToUniversalTime();
            return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        internal static string FormatLevel(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace:
                    return "TRACE";
                case LogLevel.Debug:
                    return "DEBUG";
                case LogLevel.Warning:
                    return "WARN";
                case LogLevel.Error:
                    return "ERROR";
                case LogLevel.Critical:
                    return "CRITICAL";
                default:
                    return "INFO";
            }
        }
    }
}
