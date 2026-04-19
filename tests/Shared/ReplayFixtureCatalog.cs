using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using readboard;

namespace Readboard.VerificationTests
{
    internal enum ReplayVariant
    {
        Base = 0,
        Changed = 1
    }

    internal static class ReplayFixtureCatalog
    {
        private const string ManifestPath = "recognition/replay/foreground-5x5.json";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static ReplayFixture LoadForeground5x5()
        {
            string json = File.ReadAllText(VerificationFixtureLocator.FixturePath(ManifestPath));
            ReplayFixtureManifest manifest = JsonSerializer.Deserialize<ReplayFixtureManifest>(json, JsonOptions);
            if (manifest == null)
                throw new InvalidOperationException("Replay fixture manifest is missing.");

            return ReplayFixture.FromManifest(manifest);
        }
    }

    internal sealed class ReplayFixture
    {
        private readonly string baseImagePath;
        private readonly string changedImagePath;

        private ReplayFixture(
            string name,
            int boardWidth,
            int boardHeight,
            string baseImagePath,
            string changedImagePath,
            string[] baseRows,
            string[] changedRows)
        {
            Name = name;
            BoardWidth = boardWidth;
            BoardHeight = boardHeight;
            this.baseImagePath = baseImagePath;
            this.changedImagePath = changedImagePath;
            BaseProtocolLines = PrefixRows(baseRows);
            ChangedProtocolLines = PrefixRows(changedRows);
        }

        public string Name { get; private set; }
        public int BoardWidth { get; private set; }
        public int BoardHeight { get; private set; }
        public string[] BaseProtocolLines { get; private set; }
        public string[] ChangedProtocolLines { get; private set; }

        public static ReplayFixture FromManifest(ReplayFixtureManifest manifest)
        {
            return new ReplayFixture(
                manifest.Name,
                manifest.BoardWidth,
                manifest.BoardHeight,
                VerificationFixtureLocator.FixturePath(manifest.BaseImage),
                VerificationFixtureLocator.FixturePath(manifest.ChangedImage),
                manifest.BaseRows,
                manifest.ChangedRows);
        }

        public BoardCaptureResult CreateCaptureResult(ReplayVariant variant)
        {
            return BoardCaptureResult.CreateSuccess(CreateFrame(variant), CapturePathKind.PixelBuffer);
        }

        public BoardRecognitionRequest CreateRecognitionRequest(ReplayVariant variant, bool inferLastMove)
        {
            return new BoardRecognitionRequest
            {
                Frame = CreateFrame(variant),
                InferLastMove = inferLastMove
            };
        }

        private BoardFrame CreateFrame(ReplayVariant variant)
        {
            PixelBuffer pixelBuffer = PpmFixtureLoader.LoadPixelBuffer(GetImagePath(variant));
            PixelRect bounds = new PixelRect(0, 0, pixelBuffer.Width, pixelBuffer.Height);
            return new BoardFrame
            {
                SyncMode = SyncMode.Foreground,
                BoardSize = new BoardDimensions(BoardWidth, BoardHeight),
                Viewport = new BoardViewport
                {
                    SourceBounds = bounds,
                    CellWidth = pixelBuffer.Width / (double)BoardWidth,
                    CellHeight = pixelBuffer.Height / (double)BoardHeight
                },
                PixelBuffer = pixelBuffer,
                ContentSignature = BoardContentHash.Compute(pixelBuffer)
            };
        }

        private string GetImagePath(ReplayVariant variant)
        {
            return variant == ReplayVariant.Base ? baseImagePath : changedImagePath;
        }

        private static string[] PrefixRows(string[] rows)
        {
            string[] protocolLines = new string[rows.Length];
            for (int index = 0; index < rows.Length; index++)
                protocolLines[index] = "re=" + rows[index];
            return protocolLines;
        }
    }

    internal static class PpmFixtureLoader
    {
        private const int HeaderTokenCount = 4;
        private const int MaxChannelValue = 255;
        private const int ChannelsPerPixel = 3;

        public static PixelBuffer LoadPixelBuffer(string path)
        {
            string[] tokens = ReadTokens(path);
            ValidateHeader(tokens);
            return CreatePixelBuffer(tokens);
        }

        private static string[] ReadTokens(string path)
        {
            List<string> tokens = new List<string>();
            foreach (string line in File.ReadLines(path))
            {
                string content = StripComment(line);
                if (string.IsNullOrWhiteSpace(content))
                    continue;
                tokens.AddRange(content.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            }
            return tokens.ToArray();
        }

        private static void ValidateHeader(string[] tokens)
        {
            if (tokens.Length < HeaderTokenCount)
                throw new InvalidDataException("PPM fixture is incomplete.");
            if (!string.Equals(tokens[0], "P3", StringComparison.Ordinal))
                throw new InvalidDataException("Only ASCII PPM fixtures are supported.");
            if (!string.Equals(tokens[3], MaxChannelValue.ToString(), StringComparison.Ordinal))
                throw new InvalidDataException("PPM max channel must be 255.");
        }

        private static PixelBuffer CreatePixelBuffer(string[] tokens)
        {
            int width = int.Parse(tokens[1]);
            int height = int.Parse(tokens[2]);
            int stride = width * ChannelsPerPixel;
            byte[] pixels = new byte[stride * height];
            int sourceIndex = HeaderTokenCount;
            for (int index = 0; index < pixels.Length; index++)
                pixels[index] = byte.Parse(tokens[sourceIndex++]);

            return new PixelBuffer
            {
                Format = PixelBufferFormat.Rgb24,
                Width = width,
                Height = height,
                Stride = stride,
                Pixels = pixels
            };
        }

        private static string StripComment(string line)
        {
            int markerIndex = line.IndexOf('#');
            return markerIndex >= 0 ? line.Substring(0, markerIndex) : line;
        }
    }

    internal sealed class ReplayFixtureManifest
    {
        public string Name { get; set; }
        public int BoardWidth { get; set; }
        public int BoardHeight { get; set; }
        public string BaseImage { get; set; }
        public string ChangedImage { get; set; }
        public string[] BaseRows { get; set; }
        public string[] ChangedRows { get; set; }
    }
}
