using System;
using System.IO;
using System.Reflection;
using System.Threading;
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

            AssertTransportStopReturnsWithoutWaiting(transport, harness);
        }

        [Fact]
        public void ReadLoop_OnUnexpectedEof_RaisesDisconnectedOnce_AndIsConnectedIsFalse()
        {
            using PipeTransport transport = new PipeTransport();
            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes("place 3 4" + Environment.NewLine);
            using MemoryStream stream = new MemoryStream(inputBytes);
            using StreamReader reader = new StreamReader(stream, System.Text.Encoding.UTF8);

            int disconnectedCount = 0;
            bool isConnectedDuringCallback = true;
            using ManualResetEventSlim disconnectedEvent = new ManualResetEventSlim(false);
            string receivedMessage = null;

            transport.MessageReceived += (_, line) => receivedMessage = line;
            transport.Disconnected += (_, _) =>
            {
                Interlocked.Increment(ref disconnectedCount);
                isConnectedDuringCallback = transport.IsConnected;
                disconnectedEvent.Set();
            };

            int gen = 1;
            Thread workerThread = new Thread(new ThreadStart(delegate
            {
                InvokeReadLoop(transport, gen);
            }));
            workerThread.IsBackground = true;
            workerThread.Name = "PipeTransportTests.ReadLoopEof";

            SetPrivateField(transport, "inputReader", reader);
            SetPrivateField(transport, "started", true);
            SetPrivateField(transport, "running", true);
            SetPrivateField(transport, "generation", gen);
            SetPrivateField(transport, "readThread", workerThread);

            Assert.True(transport.IsConnected);
            workerThread.Start();

            try
            {
                VerificationCompletion.Wait(disconnectedEvent, "Disconnected event was not raised on unexpected EOF.");
                VerificationCompletion.Join(workerThread, "ReadLoop thread did not exit after EOF.");

                Assert.Equal("place 3 4", receivedMessage);
                Assert.Equal(1, disconnectedCount);
                Assert.False(isConnectedDuringCallback);
                Assert.False(transport.IsConnected);

                transport.Stop();
                Assert.Equal(1, disconnectedCount);
                Assert.False(transport.IsConnected);
            }
            finally
            {
                VerificationCompletion.Join(workerThread, "ReadLoop worker thread cleanup join failed.");
            }
        }

        [Fact]
        public void Stop_WhenRunning_DoesNotRaiseDisconnectedEvent()
        {
            using PipeTransport transport = new PipeTransport();
            using BlockingReadStream stream = new BlockingReadStream();
            using StreamReader reader = new StreamReader(stream, System.Text.Encoding.UTF8);

            int disconnectedCount = 0;
            transport.Disconnected += (_, _) => Interlocked.Increment(ref disconnectedCount);

            int gen = 1;
            Thread workerThread = new Thread(new ThreadStart(delegate
            {
                InvokeReadLoop(transport, gen);
            }));
            workerThread.IsBackground = true;
            workerThread.Name = "PipeTransportTests.StopNoDisconnected";

            SetPrivateField(transport, "inputReader", reader);
            SetPrivateField(transport, "started", true);
            SetPrivateField(transport, "running", true);
            SetPrivateField(transport, "generation", gen);
            SetPrivateField(transport, "readThread", workerThread);

            workerThread.Start();

            try
            {
                VerificationCompletion.Wait(stream.ReadStarted, "ReadLoop did not start reading.");
                Assert.True(transport.IsConnected);

                transport.Stop();

                VerificationCompletion.Join(workerThread, "ReadLoop thread did not exit after Stop.");

                Assert.Equal(0, disconnectedCount);
                Assert.False(transport.IsConnected);
            }
            finally
            {
                stream.Unblock();
                VerificationCompletion.Join(workerThread, "ReadLoop cleanup join failed.");
            }
        }

        [Fact]
        public void ReadLoop_OnIOException_RaisesDisconnectedOnce_AndIsConnectedIsFalse()
        {
            using PipeTransport transport = new PipeTransport();
            using ThrowingReadStream stream = new ThrowingReadStream();
            using StreamReader reader = new StreamReader(stream, System.Text.Encoding.UTF8);

            int disconnectedCount = 0;
            bool isConnectedDuringCallback = true;
            using ManualResetEventSlim disconnectedEvent = new ManualResetEventSlim(false);

            transport.Disconnected += (_, _) =>
            {
                Interlocked.Increment(ref disconnectedCount);
                isConnectedDuringCallback = transport.IsConnected;
                disconnectedEvent.Set();
            };

            int gen = 1;
            Thread workerThread = new Thread(new ThreadStart(delegate
            {
                InvokeReadLoop(transport, gen);
            }));
            workerThread.IsBackground = true;
            workerThread.Name = "PipeTransportTests.ReadLoopIoException";

            SetPrivateField(transport, "inputReader", reader);
            SetPrivateField(transport, "started", true);
            SetPrivateField(transport, "running", true);
            SetPrivateField(transport, "generation", gen);
            SetPrivateField(transport, "readThread", workerThread);

            workerThread.Start();

            try
            {
                VerificationCompletion.Wait(disconnectedEvent, "Disconnected event was not raised on read IOException.");
                VerificationCompletion.Join(workerThread, "ReadLoop thread did not exit after IOException.");

                Assert.Equal(1, disconnectedCount);
                Assert.False(isConnectedDuringCallback);
                Assert.False(transport.IsConnected);

                transport.Stop();
                Assert.Equal(1, disconnectedCount);
            }
            finally
            {
                VerificationCompletion.Join(workerThread, "ReadLoop worker thread cleanup join failed.");
            }
        }

        [Fact]
        public void OldReader_FromPreviousStart_DoesNotDisconnectFreshStart()
        {
            using PipeTransport transport = new PipeTransport();
            using BlockingReadStream oldStream = new BlockingReadStream();
            using StreamReader oldReader = new StreamReader(oldStream, System.Text.Encoding.UTF8);

            int disconnectedCount = 0;
            transport.Disconnected += (_, _) => Interlocked.Increment(ref disconnectedCount);

            int oldGen = 1;
            Thread oldWorkerThread = new Thread(new ThreadStart(delegate
            {
                InvokeReadLoop(transport, oldGen);
            }));
            oldWorkerThread.IsBackground = true;
            oldWorkerThread.Name = "PipeTransportTests.OldReader";

            SetPrivateField(transport, "inputReader", oldReader);
            SetPrivateField(transport, "started", true);
            SetPrivateField(transport, "running", true);
            SetPrivateField(transport, "generation", oldGen);
            SetPrivateField(transport, "readThread", oldWorkerThread);

            oldWorkerThread.Start();

            try
            {
                VerificationCompletion.Wait(oldStream.ReadStarted, "Old read thread did not start.");

                SetPrivateField(transport, "generation", 2);
                SetPrivateField(transport, "running", true);
                Assert.True(transport.IsConnected);

                oldStream.Unblock();
                VerificationCompletion.Join(oldWorkerThread, "Old worker thread did not exit.");

                Assert.Equal(0, disconnectedCount);
                Assert.True(transport.IsConnected);

                transport.Stop();
                Assert.Equal(0, disconnectedCount);
                Assert.False(transport.IsConnected);
            }
            finally
            {
                oldStream.Unblock();
                VerificationCompletion.Join(oldWorkerThread, "Old worker thread cleanup join failed.");
            }
        }

        [Fact]
        public void Send_OnIOException_RaisesDisconnected_WhenRunning()
        {
            using PipeTransport transport = new PipeTransport();
            using ConsoleRedirect redirect = ConsoleRedirect.CaptureStdOut(new ThrowingTextWriter());

            int disconnectedCount = 0;
            bool isConnectedDuringCallback = true;
            transport.Disconnected += (_, _) =>
            {
                Interlocked.Increment(ref disconnectedCount);
                isConnectedDuringCallback = transport.IsConnected;
            };

            SetPrivateField(transport, "started", true);
            SetPrivateField(transport, "running", true);
            SetPrivateField(transport, "generation", 1);
            Assert.True(transport.IsConnected);

            transport.Send("test message");

            Assert.Equal(1, disconnectedCount);
            Assert.False(isConnectedDuringCallback);
            Assert.False(transport.IsConnected);

            transport.Stop();
            Assert.Equal(1, disconnectedCount);
        }

        [Fact]
        public void Send_BeforeEverStarted_PropagatesUnexpectedIOException()
        {
            using PipeTransport transport = new PipeTransport();
            using ConsoleRedirect redirect = ConsoleRedirect.CaptureStdOut(new ThrowingTextWriter());

            Assert.Throws<IOException>(() => transport.Send("pre-start failure"));
        }

        [Fact]
        public void Send_AfterStopOrDisconnect_PreventsStaleWrites()
        {
            using ConsoleRedirect redirect = ConsoleRedirect.CaptureStdOut();
            using PipeTransport transport = new PipeTransport();

            SetPrivateField(transport, "started", true);
            SetPrivateField(transport, "running", false);

            transport.Send("stale line");
            transport.SendError("stale error");

            Assert.Equal(string.Empty, redirect.ReadToEnd());
        }

        [Fact]
        public void Send_OldWriteFailure_DoesNotCloseFreshStart()
        {
            using PipeTransport transport = new PipeTransport();
            int disconnectedCount = 0;
            transport.Disconnected += (_, _) => Interlocked.Increment(ref disconnectedCount);

            MethodInfo notifyMethod = typeof(PipeTransport).GetMethod("NotifyUnexpectedDisconnect", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(notifyMethod);

            SetPrivateField(transport, "started", true);
            SetPrivateField(transport, "running", true);
            SetPrivateField(transport, "generation", 2);

            notifyMethod.Invoke(transport, new object[] { 1 });

            Assert.Equal(0, disconnectedCount);
            Assert.True(transport.IsConnected);

            transport.Stop();
        }

        private static void InvokeRaiseMessageReceived(PipeTransport transport, string line)
        {
            MethodInfo method = typeof(PipeTransport).GetMethod("RaiseMessageReceived", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(transport, new object[] { line });
        }

        private static void InvokeReadLoop(PipeTransport transport, int gen)
        {
            MethodInfo method = typeof(PipeTransport).GetMethod("ReadLoop", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(transport, new object[] { gen });
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(target, value);
        }

        private static void AssertTransportStopReturnsWithoutWaiting(
            PipeTransport transport,
            BlockingBackgroundThreadHarness harness)
        {
            ManualResetEventSlim stopCompleted = new ManualResetEventSlim(false);
            Exception stopException = null;
            Thread stopThread = new Thread(new ThreadStart(delegate
            {
                try
                {
                    transport.Stop();
                }
                catch (Exception ex)
                {
                    stopException = ex;
                }
                finally
                {
                    stopCompleted.Set();
                }
            }));
            stopThread.IsBackground = true;
            stopThread.Name = "PipeTransportTests.Stop";
            stopThread.Start();

            try
            {
                VerificationCompletion.Wait(
                    stopCompleted,
                    "PipeTransport.Stop must return without joining a blocked read thread.");
                Assert.Null(stopException);
                Assert.True(harness.Thread.IsAlive);
            }
            finally
            {
                harness.Release();
                VerificationCompletion.Wait(
                    stopCompleted,
                    "PipeTransport.Stop did not finish after the read thread was released.");
                VerificationCompletion.Join(
                    stopThread,
                    "PipeTransport.Stop worker did not exit.");
                stopCompleted.Dispose();
            }
        }

        private sealed class ConsoleRedirect : IDisposable
        {
            private readonly TextWriter writer;
            private readonly TextWriter original;
            private readonly Action<TextWriter> restore;

            private ConsoleRedirect(TextWriter writer, TextWriter original, Action<TextWriter> replace, Action<TextWriter> restore)
            {
                this.writer = writer;
                this.original = original;
                this.restore = restore;
                replace(writer);
            }

            public static ConsoleRedirect CaptureStdOut()
            {
                return new ConsoleRedirect(new StringWriter(), Console.Out, Console.SetOut, Console.SetOut);
            }

            public static ConsoleRedirect CaptureStdOut(TextWriter writer)
            {
                return new ConsoleRedirect(writer, Console.Out, Console.SetOut, Console.SetOut);
            }

            public static ConsoleRedirect CaptureStdErr()
            {
                return new ConsoleRedirect(new StringWriter(), Console.Error, Console.SetError, Console.SetError);
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

        private sealed class BlockingReadStream : Stream
        {
            private readonly ManualResetEventSlim readStarted = new ManualResetEventSlim(false);
            private readonly ManualResetEventSlim unblock = new ManualResetEventSlim(false);

            public ManualResetEventSlim ReadStarted => readStarted;

            public void Unblock()
            {
                unblock.Set();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                readStarted.Set();
                unblock.Wait();
                return 0;
            }

            protected override void Dispose(bool disposing)
            {
                unblock.Set();
                base.Dispose(disposing);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private sealed class ThrowingReadStream : Stream
        {
            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new IOException("Simulated pipe read failure");
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private sealed class ThrowingTextWriter : TextWriter
        {
            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

            public override void WriteLine(string value)
            {
                throw new IOException("Simulated console write failure");
            }
        }
    }
}
