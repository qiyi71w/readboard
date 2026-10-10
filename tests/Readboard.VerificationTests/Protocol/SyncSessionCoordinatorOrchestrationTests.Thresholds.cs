using System;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Protocol
{
    public sealed partial class SyncSessionCoordinatorOrchestrationTests
    {
        public static TheoryData<int, int, string, int, int> CustomThresholdCases
        {
            get
            {
                var cases = new TheoryData<int, int, string, int, int>();
                foreach (SyncMode mode in new[] { SyncMode.Background, SyncMode.Foreground })
                {
                    cases.Add((int)mode, (int)SettingsDraftField.BlackPercent, "75", (int)BoardCellState.Black, (int)BoardCellState.Empty);
                    cases.Add((int)mode, (int)SettingsDraftField.WhitePercent, "75", (int)BoardCellState.White, (int)BoardCellState.Empty);
                    cases.Add((int)mode, (int)SettingsDraftField.BlackOffset, "120", (int)BoardCellState.Empty, (int)BoardCellState.Black);
                    cases.Add((int)mode, (int)SettingsDraftField.WhiteOffset, "80", (int)BoardCellState.White, (int)BoardCellState.Empty);
                    cases.Add((int)mode, (int)SettingsDraftField.GrayOffset, "20", (int)BoardCellState.Black, (int)BoardCellState.Empty);
                }
                return cases;
            }
        }

        [Theory]
        [MemberData(nameof(CustomThresholdCases))]
        public void OneTimeSync_CustomThresholdsChangeRecognizedBoard(
            int modeValue, int fieldValue, string value, int before, int after)
        {
            SyncMode mode = (SyncMode)modeValue;
            SettingsDraftField field = (SettingsDraftField)fieldValue;
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            var draft = CreateThresholdDraft(() => active, candidate => active = candidate);
            var snapshot = (SyncCoordinatorHostSnapshot)CreateSnapshot(
                typeof(SyncCoordinatorHostSnapshot), mode, new IntPtr(5151));
            snapshot.BoardWidth = snapshot.BoardHeight = 2;
            snapshot.SelectionBounds = new PixelRect(0, 0, 20, 20);
            var host = new HostRecorder(snapshot);
            bool saveDuringCapture = false;
            var capture = new SequencedCaptureService(CreateThresholdFrame(mode, field));
            using var coordinator = new SyncSessionCoordinator(new RecordingTransport(), new LegacyProtocolAdapter());
            coordinator.AttachRuntime(new SyncSessionRuntimeDependencies
            {
                Host = (ISyncCoordinatorHost)CreateProxy(typeof(ISyncCoordinatorHost), (method, args) =>
                {
                    if (method.Name == "CaptureSnapshot")
                        snapshot.Thresholds = RecognitionThresholds.FromConfig(active);
                    return host.HandleCall(method, args);
                }),
                CaptureService = (IBoardCaptureService)CreateProxy(typeof(IBoardCaptureService), (method, args) =>
                {
                    if (saveDuringCapture)
                    {
                        Assert.Equal(SettingsDraftOperationOutcome.Saved, draft.Save().Outcome);
                        saveDuringCapture = false;
                    }
                    return capture.Capture((BoardCaptureRequest)args[0]);
                }),
                RecognitionService = new LegacyBoardRecognitionService(),
                PlacementService = new PassivePlacementService(),
                OverlayService = new PassiveOverlayService(),
                WindowDescriptorFactory = (IWindowDescriptorFactory)CreateProxy(
                    typeof(IWindowDescriptorFactory), new DescriptorFactoryRecorder().HandleCall)
            });

            Assert.True(coordinator.TryRunOneTimeSync());
            Assert.Equal((BoardCellState)before, host.LastRecognizedSnapshot.BoardState[0]);
            draft.Update(SettingsDraftUpdate.Text(field, value));
            Assert.True(coordinator.TryRunOneTimeSync());
            Assert.Equal((BoardCellState)before, host.LastRecognizedSnapshot.BoardState[0]);
            saveDuringCapture = true;
            Assert.True(coordinator.TryRunOneTimeSync());
            Assert.Equal((BoardCellState)before, host.LastRecognizedSnapshot.BoardState[0]);
            Assert.True(coordinator.TryRunOneTimeSync());
            Assert.Equal((BoardCellState)after, host.LastRecognizedSnapshot.BoardState[0]);
        }

        [Theory]
        [MemberData(nameof(CustomThresholdCases))]
        public void RecognitionCache_RecomputesSamePixelsAfterEffectiveThresholdChanges(
            int modeValue, int fieldValue, string value, int before, int after)
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            var draft = CreateThresholdDraft(() => active, candidate => active = candidate);
            var service = new LegacyBoardRecognitionService();
            var request = new BoardRecognitionRequest
            {
                Frame = CreateThresholdFrame((SyncMode)modeValue, (SettingsDraftField)fieldValue),
                InferLastMove = false
            };
            Assert.Equal((BoardCellState)before, service.Recognize(request).Snapshot.BoardState[0]);
            Assert.True(service.Recognize(request).UsedCachedSnapshot);
            draft.Update(SettingsDraftUpdate.Text((SettingsDraftField)fieldValue, value));
            Assert.Equal(SettingsDraftOperationOutcome.Saved, draft.Save().Outcome);
            request.Thresholds = RecognitionThresholds.FromConfig(active);
            BoardRecognitionResult changed = service.Recognize(request);
            Assert.False(changed.UsedCachedSnapshot);
            Assert.Equal((BoardCellState)after, changed.Snapshot.BoardState[0]);
            BoardRecognitionResult repeated = service.Recognize(request);
            Assert.True(repeated.UsedCachedSnapshot);
            Assert.Equal((BoardCellState)after, repeated.Snapshot.BoardState[0]);
        }

        [Theory]
        [InlineData((int)SyncMode.Fox, (int)BoardCellState.White)]
        [InlineData((int)SyncMode.FoxBackgroundPlace, (int)BoardCellState.White)]
        [InlineData((int)SyncMode.Yike, (int)BoardCellState.White)]
        [InlineData((int)SyncMode.Sina, (int)BoardCellState.White)]
        [InlineData((int)SyncMode.Tygem, (int)BoardCellState.Empty)]
        public void NativeRecognition_CustomValuesDoNotChangeResultOrInvalidateCache(int mode, int expected)
        {
            var frame = CreateThresholdFrame((SyncMode)mode, SettingsDraftField.WhiteOffset);
            frame.Viewport.SourceBounds = new PixelRect(0, 0, 18, 18);
            var request = new BoardRecognitionRequest { Frame = frame, InferLastMove = false };
            var service = new LegacyBoardRecognitionService();
            Assert.Equal((BoardCellState)expected, service.Recognize(request).Snapshot.BoardState[0]);
            request.Thresholds = new RecognitionThresholds
            {
                BlackPercent = 1, WhitePercent = 99, BlackOffset = 255, WhiteOffset = 1, GrayOffset = 255
            };
            BoardRecognitionResult changedCustom = service.Recognize(request);
            Assert.True(changedCustom.UsedCachedSnapshot);
            Assert.Equal((BoardCellState)expected, changedCustom.Snapshot.BoardState[0]);
        }

        [Theory]
        [InlineData((int)SyncMode.Background)]
        [InlineData((int)SyncMode.Foreground)]
        public void ZeroThresholds_UseDefaultRecognitionAndReuseEquivalentCache(int mode)
        {
            var request = new BoardRecognitionRequest
            {
                Frame = CreateThresholdFrame((SyncMode)mode, SettingsDraftField.BlackPercent),
                InferLastMove = false
            };
            var service = new LegacyBoardRecognitionService();
            Assert.Equal(BoardCellState.Black, service.Recognize(request).Snapshot.BoardState[0]);
            request.Thresholds = new RecognitionThresholds
            {
                BlackPercent = 0, WhitePercent = 0, BlackOffset = 0, WhiteOffset = 0, GrayOffset = 0
            };
            BoardRecognitionResult zeros = service.Recognize(request);
            Assert.True(zeros.UsedCachedSnapshot);
            Assert.Equal(new[] { BoardCellState.Black, BoardCellState.Empty, BoardCellState.Empty, BoardCellState.Empty },
                zeros.Snapshot.BoardState);
        }

        private static SettingsDraftRuntime CreateThresholdDraft(Func<AppConfig> current, Action<AppConfig> replace)
        {
            return new SettingsDraftRuntime(current(), () => AppConfig.CreateDefault("220430", "TEST"),
                new MainForm.MainFormSettingsDraftPersistence(current, _ => { }, replace, () => { }),
                new ThresholdEffects());
        }

        private sealed class ThresholdEffects : ISettingsDraftRuntimeEffects
        {
            public void ApplyLanguagePreference(string preference) { }
            public void ApplyTheme(int colorMode) { }
            public void ApplyBackgroundAnalysis(bool enabled) { }
        }

        private static BoardFrame CreateThresholdFrame(SyncMode mode, SettingsDraftField field)
        {
            byte[] pixels = new byte[20 * 20 * 3];
            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    int offset = (y * 20 + x) * 3;
                    byte red = 200, green = 150, blue = 100;
                    if (x < 10 && y < 10)
                    {
                        switch (field)
                        {
                            case SettingsDraftField.BlackPercent:
                                if (x < 5) red = green = blue = 40;
                                break;
                            case SettingsDraftField.WhitePercent:
                                if (x < 5) red = green = blue = 180;
                                break;
                            case SettingsDraftField.BlackOffset:
                                red = green = blue = 110;
                                break;
                            case SettingsDraftField.WhiteOffset:
                                red = green = blue = 160;
                                break;
                            case SettingsDraftField.GrayOffset:
                                red = blue = 60;
                                green = 90;
                                break;
                        }
                    }
                    pixels[offset] = red;
                    pixels[offset + 1] = green;
                    pixels[offset + 2] = blue;
                }
            }
            return new BoardFrame
            {
                SyncMode = mode,
                BoardSize = new BoardDimensions(2, 2),
                Viewport = new BoardViewport { SourceBounds = new PixelRect(0, 0, 20, 20) },
                PixelBuffer = new PixelBuffer
                {
                    Format = PixelBufferFormat.Rgb24,
                    Width = 20,
                    Height = 20,
                    Stride = 60,
                    Pixels = pixels
                }
            };
        }
    }
}
