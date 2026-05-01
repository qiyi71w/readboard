using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Diagnostics
{
    public sealed class BoardDebugDiagnosticsWriterTests
    {
        [Fact]
        public void RecordRecognitionSuccess_WritesFrameMetadataRecognitionAndLog()
        {
            using (DiagnosticWorkspace workspace = DiagnosticWorkspace.Create())
            {
                BoardDebugDiagnosticsWriter writer = new BoardDebugDiagnosticsWriter(workspace.RootPath, () => true);

                writer.RecordRecognitionSuccess(CreateRecord());

                string eventDirectory = Assert.Single(Directory.GetDirectories(workspace.RootPath));
                Assert.True(File.Exists(Path.Combine(eventDirectory, "frame.png")));
                Assert.Contains("\"EventName\":\"recognition-success\"", File.ReadAllText(Path.Combine(eventDirectory, "metadata.json")));
                Assert.Contains("payload=XO.", File.ReadAllText(Path.Combine(eventDirectory, "recognition.txt")));
                Assert.Contains("recognition-success", File.ReadAllText(Path.Combine(workspace.RootPath, "debug.log")));
            }
        }

        [Fact]
        public void RecordRecognitionSuccess_WritesBitmapFrameDimensionsToMetadata()
        {
            using (DiagnosticWorkspace workspace = DiagnosticWorkspace.Create())
            using (Bitmap image = new Bitmap(3, 2))
            {
                BoardDebugDiagnosticsWriter writer = new BoardDebugDiagnosticsWriter(workspace.RootPath, () => true);
                BoardDebugDiagnosticRecord record = CreateRecord();
                record.Frame.PixelBuffer = null;
                record.Frame.Image = image;

                writer.RecordRecognitionSuccess(record);

                string eventDirectory = Assert.Single(Directory.GetDirectories(workspace.RootPath));
                using (JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(eventDirectory, "metadata.json"))))
                {
                    Assert.Equal(3, metadata.RootElement.GetProperty("FrameWidth").GetInt32());
                    Assert.Equal(2, metadata.RootElement.GetProperty("FrameHeight").GetInt32());
                }
            }
        }

        [Fact]
        public void RecordRecognitionSuccess_SkipsDuplicateFrameAndSnapshot()
        {
            using (DiagnosticWorkspace workspace = DiagnosticWorkspace.Create())
            {
                BoardDebugDiagnosticsWriter writer = new BoardDebugDiagnosticsWriter(workspace.RootPath, () => true);
                BoardDebugDiagnosticRecord record = CreateRecord();

                writer.RecordRecognitionSuccess(record);
                writer.RecordRecognitionSuccess(record);

                Assert.Single(Directory.GetDirectories(workspace.RootPath));
            }
        }

        [Fact]
        public void EventDirectoryCounter_UsesThreadSafeIncrement()
        {
            string source = File.ReadAllText(Path.Combine(
                VerificationFixtureLocator.RepositoryRoot(),
                "readboard",
                "Core",
                "Diagnostics",
                "BoardDebugDiagnosticsWriter.cs"));

            Assert.Contains("Interlocked.Increment(ref eventCounter)", source);
        }

        [Fact]
        public void RecordCaptureFailure_WritesMetadataAndLogWithoutFrame()
        {
            using (DiagnosticWorkspace workspace = DiagnosticWorkspace.Create())
            {
                BoardDebugDiagnosticsWriter writer = new BoardDebugDiagnosticsWriter(workspace.RootPath, () => true);

                writer.RecordCaptureFailure(new BoardDebugDiagnosticRecord
                {
                    SyncMode = SyncMode.Background,
                    BoardWidth = 19,
                    BoardHeight = 19,
                    FailureReason = "Capture failed."
                });

                string eventDirectory = Assert.Single(Directory.GetDirectories(workspace.RootPath));
                Assert.False(File.Exists(Path.Combine(eventDirectory, "frame.png")));
                Assert.Contains("\"EventName\":\"capture-failure\"", File.ReadAllText(Path.Combine(eventDirectory, "metadata.json")));
                Assert.Contains("Capture failed.", File.ReadAllText(Path.Combine(workspace.RootPath, "debug.log")));
            }
        }

        private static BoardDebugDiagnosticRecord CreateRecord()
        {
            return new BoardDebugDiagnosticRecord
            {
                SyncMode = SyncMode.Fox,
                BoardWidth = 19,
                BoardHeight = 19,
                CapturePath = CapturePathKind.PixelBuffer,
                Frame = new BoardFrame
                {
                    SyncMode = SyncMode.Fox,
                    BoardSize = new BoardDimensions(19, 19),
                    PixelBuffer = new PixelBuffer
                    {
                        Format = PixelBufferFormat.Rgb24,
                        Width = 2,
                        Height = 2,
                        Stride = 6,
                        Pixels = new byte[]
                        {
                            255, 0, 0, 0, 255, 0,
                            0, 0, 255, 255, 255, 255
                        }
                    },
                    ContentSignature = 42
                },
                Snapshot = new BoardSnapshot
                {
                    Width = 3,
                    Height = 1,
                    IsValid = true,
                    BlackStoneCount = 1,
                    WhiteStoneCount = 1,
                    Payload = "XO.",
                    StateSignature = 7
                }
            };
        }

        private sealed class DiagnosticWorkspace : IDisposable
        {
            private DiagnosticWorkspace(string rootPath)
            {
                RootPath = rootPath;
            }

            public string RootPath { get; private set; }

            public static DiagnosticWorkspace Create()
            {
                string rootPath = Path.Combine(Path.GetTempPath(), "readboard-debug-diagnostics-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);
                return new DiagnosticWorkspace(rootPath);
            }

            public void Dispose()
            {
                if (Directory.Exists(RootPath))
                    Directory.Delete(RootPath, true);
            }
        }
    }
}
