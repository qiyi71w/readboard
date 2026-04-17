using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Xunit;
using readboard;
using Readboard.VerificationTests.Support;

namespace Readboard.VerificationTests.Transport
{
    public sealed class PipeTransportTests
    {
        [Fact]
        public void Send_WritesLineToStandardOutput()
        {
            using ConsoleRedirect redirect = ConsoleRedirect.CaptureStdOut();
            using PipeTransport transport = new PipeTransport();

            transport.Send("ready");

            Assert.Equal("ready" + Environment.NewLine, redirect.ReadToEnd());
        }

        [Fact]
        public void SendError_WritesLegacyErrorPrefixToStandardError()
        {
            using ConsoleRedirect redirect = ConsoleRedirect.CaptureStdErr();
            using PipeTransport transport = new PipeTransport();

            transport.SendError("boom");

            Assert.Equal("error: boom" + Environment.NewLine, redirect.ReadToEnd());
        }

        [Fact]
        public void RaiseMessageReceived_IgnoresBlankLines()
        {
            using PipeTransport transport = new PipeTransport();
            string received = null;
            transport.MessageReceived += (_, line) => received = line;

            InvokeRaiseMessageReceived(transport, string.Empty);
            InvokeRaiseMessageReceived(transport, "place 3 4");

            Assert.Equal("place 3 4", received);
        }

        [Fact]
        public void Stop_DoesNotBlockOnReadThreadJoinDuringShutdown()
        {
            using PipeTransport transport = new PipeTransport();
            using BlockingBackgroundThreadHarness harness = BlockingBackgroundThreadHarness.Start("PipeTransportReadThread");
            SetPrivateField(transport, "readThread", harness.Thread);

            Stopwatch stopwatch = Stopwatch.StartNew();
            transport.Stop();
            stopwatch.Stop();

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromMilliseconds(250),
                "Pipe transport shutdown should not wait for the read thread to finish.");
        }

        private static void InvokeRaiseMessageReceived(PipeTransport transport, string line)
        {
            MethodInfo method = typeof(PipeTransport).GetMethod("RaiseMessageReceived", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(transport, new object[] { line });
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(target, value);
        }

        private sealed class ConsoleRedirect : IDisposable
        {
            private readonly StringWriter writer = new StringWriter();
            private readonly TextWriter original;
            private readonly Action<TextWriter> restore;

            private ConsoleRedirect(TextWriter original, Action<TextWriter> replace, Action<TextWriter> restore)
            {
                this.original = original;
                this.restore = restore;
                replace(writer);
            }

            public static ConsoleRedirect CaptureStdOut()
            {
                return new ConsoleRedirect(Console.Out, Console.SetOut, Console.SetOut);
            }

            public static ConsoleRedirect CaptureStdErr()
            {
                return new ConsoleRedirect(Console.Error, Console.SetError, Console.SetError);
            }

            public string ReadToEnd()
            {
                return writer.ToString();
            }

            public void Dispose()
            {
                restore(original);
                writer.Dispose();
            }
        }
    }
}
