using System;
using System.Collections.Generic;
using readboard;
using Xunit;


namespace Readboard.VerificationTests.Host
{
    public sealed class ControlCenterSessionObservationTests
    {
        [Fact]
        public void CompositeObservation_AppliesAllFieldsAndPublishesOnce()
        {
            ControlCenterRuntime runtime = CreateRuntime();
            ControlCenterSessionObservation observation = new ControlCenterSessionObservation(0)
                .WithTargetWindowValid(true)
                .WithFoxWindowContext(new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    LiveRoomState = FoxLiveRoomState.Playing,
                    RoomToken = "room-1",
                    LiveTitleMove = 12
                })
                .WithYikeWindowContext(new YikeWindowContext
                {
                    RoomToken = "yike-room-1",
                    MoveNumber = 8
                })
                .WithBoardRegion(true, true)
                .WithSyncActivity(false, true)
                .WithAnalysisState(false, true)
                .WithRecentSync("12:34:56", 23, "18 ms")
                .WithTitleTurn(MainWindowTitleTurn.White)
                .WithHostConnected(true);

            ControlCenterSessionObservationApplyResult result = runtime.ApplyObservation(observation);

            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Applied, result.Outcome);
            Assert.True(result.ShouldPublishSnapshot);
            Assert.True(result.Snapshot.TargetWindowValid);
            Assert.Equal("room-1", result.Snapshot.FoxWindowContext.RoomToken);
            Assert.Equal(12, result.Snapshot.FoxWindowContext.LiveTitleMove);
            Assert.Equal("yike-room-1", result.Snapshot.YikeWindowContext.RoomToken);
            Assert.Equal(8, result.Snapshot.YikeWindowContext.MoveNumber);
            Assert.True(result.Snapshot.BoardRegionRecognized);
            Assert.True(result.Snapshot.PlacementRegionResolved);
            Assert.False(result.Snapshot.QuickSyncActive);
            Assert.True(result.Snapshot.ContinuousSyncActive);
            Assert.False(result.Snapshot.AnalysisRunning);
            Assert.True(result.Snapshot.AnalysisStateAvailable);
            Assert.Equal("12:34:56", result.Snapshot.LastSync);
            Assert.Equal(23, result.Snapshot.StoneCount);
            Assert.Equal("18 ms", result.Snapshot.Duration);
            Assert.Equal(MainWindowTitleTurn.White, result.Snapshot.TitleTurn);
            Assert.True(result.Snapshot.HostConnected);
        }
        [Fact]
        public void YikeContextRuntime_ProjectsCopyAndInvokesOwnedEffectsOnce()
        {
            ControlCenterRuntime controlCenter = CreateRuntime();
            var adapter = new RecordingYikeContextAdapter(controlCenter);
            var runtime = new YikeContextRuntime(adapter);
            YikeWindowContext source = new YikeWindowContext
            {
                RoomToken = "65191829",
                MoveNumber = 16
            };

            ControlCenterSessionObservationApplyResult result = runtime.Apply(source);

            source.RoomToken = "mutated";
            source.MoveNumber = 17;
            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Applied, result.Outcome);
            Assert.Equal("65191829", adapter.StoredContext.RoomToken);
            Assert.Equal(16, adapter.StoredContext.MoveNumber);
            Assert.Equal("65191829", adapter.CoordinatorContext.RoomToken);
            Assert.Equal(16, adapter.CoordinatorContext.MoveNumber);
            Assert.Equal("65191829", controlCenter.Snapshot.YikeWindowContext.RoomToken);
            Assert.Equal(16, controlCenter.Snapshot.YikeWindowContext.MoveNumber);
            Assert.Equal(1, adapter.TitleApplyCount);
            Assert.Equal(1, adapter.PublicationCount);
        }


        [Fact]
        public void RepeatedObservation_IsNoOpWithoutSecondPublicationEffect()
        {
            ControlCenterRuntime runtime = CreateRuntime();
            ControlCenterSessionObservation observation = new ControlCenterSessionObservation(0)
                .WithHostConnected(true)
                .WithSemanticLog("INFO", "WebView_hostConnected");

            ControlCenterSessionObservationApplyResult first = runtime.ApplyObservation(observation);
            ControlCenterSessionObservationApplyResult second = runtime.ApplyObservation(observation);

            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Applied, first.Outcome);
            Assert.Single(first.SemanticMessages);
            Assert.Equal(ControlCenterSessionObservationApplyOutcome.NoOp, second.Outcome);
            Assert.Empty(second.SemanticMessages);
            Assert.False(second.ShouldPublishSnapshot);
        }
        [Fact]
        public void SemanticLogFingerprintIncludesTypedArguments()
        {
            ControlCenterRuntime runtime = CreateRuntime();
            ControlCenterSessionObservation first = new ControlCenterSessionObservation(0)
                .WithSemanticLog("SYNC", "WebView_candidateRowNumber", null, 1);
            ControlCenterSessionObservation second = new ControlCenterSessionObservation(0)
                .WithSemanticLog("SYNC", "WebView_candidateRowNumber", null, 2);

            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Applied, runtime.ApplyObservation(first).Outcome);
            ControlCenterSessionObservationApplyResult result = runtime.ApplyObservation(second);

            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Applied, result.Outcome);
            Assert.Single(result.SemanticMessages);
            Assert.Equal(2, result.SemanticMessages[0].Arguments[0]);
        }


        [Fact]
        public void ObservationFingerprint_DoesNotCollideWhenStringFieldsContainDelimiters()
        {
            ControlCenterRuntime runtime = CreateRuntime();

            ControlCenterSessionObservation first = new ControlCenterSessionObservation(0)
                .WithRecentSync("a|1", 2, "3");
            ControlCenterSessionObservation second = new ControlCenterSessionObservation(0)
                .WithRecentSync("a", 1, "2|3");

            Assert.Equal(
                ControlCenterSessionObservationApplyOutcome.Applied,
                runtime.ApplyObservation(first).Outcome);
            ControlCenterSessionObservationApplyResult result = runtime.ApplyObservation(second);

            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Applied, result.Outcome);
            Assert.Equal("a", result.Snapshot.LastSync);
            Assert.Equal(1, result.Snapshot.StoneCount);
            Assert.Equal("2|3", result.Snapshot.Duration);
        }

        [Fact]
        public void ObservationFingerprint_DoesNotCollideWhenFoxFieldsContainDelimiters()
        {
            ControlCenterRuntime runtime = CreateRuntime();

            ControlCenterSessionObservation first = new ControlCenterSessionObservation(0)
                .WithFoxWindowContext(new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    LiveRoomState = FoxLiveRoomState.Playing,
                    RoomToken = "room;1",
                    LiveTitleMove = 2,
                    RecordCurrentMove = 3,
                    RecordTotalMove = 1,
                    TitleFingerprint = "tail"
                });
            ControlCenterSessionObservation second = new ControlCenterSessionObservation(0)
                .WithFoxWindowContext(new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    LiveRoomState = FoxLiveRoomState.Playing,
                    RoomToken = "room",
                    LiveTitleMove = 1,
                    RecordCurrentMove = 2,
                    RecordTotalMove = 3,
                    RecordAtEnd = true,
                    TitleFingerprint = "0;tail"
                });

            Assert.Equal(
                ControlCenterSessionObservationApplyOutcome.Applied,
                runtime.ApplyObservation(first).Outcome);
            ControlCenterSessionObservationApplyResult result = runtime.ApplyObservation(second);

            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Applied, result.Outcome);
            Assert.Equal("room", result.Snapshot.FoxWindowContext.RoomToken);
            Assert.Equal(1, result.Snapshot.FoxWindowContext.LiveTitleMove);
            Assert.Equal(2, result.Snapshot.FoxWindowContext.RecordCurrentMove);
            Assert.Equal(3, result.Snapshot.FoxWindowContext.RecordTotalMove);
            Assert.True(result.Snapshot.FoxWindowContext.RecordAtEnd);
            Assert.Equal("0;tail", result.Snapshot.FoxWindowContext.TitleFingerprint);
        }

        [Fact]
        public void ObservationFingerprint_DistinguishesNullAndEmptyContextFields()
        {
            ControlCenterRuntime runtime = CreateRuntime();
            ControlCenterSessionObservation nullFox = new ControlCenterSessionObservation(0)
                .WithFoxWindowContext(new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    RoomToken = null,
                    TitleFingerprint = null
                });
            ControlCenterSessionObservation emptyFox = new ControlCenterSessionObservation(0)
                .WithFoxWindowContext(new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    RoomToken = string.Empty,
                    TitleFingerprint = string.Empty
                });

            Assert.Equal(
                ControlCenterSessionObservationApplyOutcome.Applied,
                runtime.ApplyObservation(nullFox).Outcome);
            ControlCenterSessionObservationApplyResult foxResult = runtime.ApplyObservation(emptyFox);

            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Applied, foxResult.Outcome);
            Assert.Equal(string.Empty, foxResult.Snapshot.FoxWindowContext.RoomToken);
            Assert.Equal(string.Empty, foxResult.Snapshot.FoxWindowContext.TitleFingerprint);
        }

        [Fact]
        public void ObservationFingerprint_DoesNotNormalizeYikeContextFields()
        {
            ControlCenterRuntime runtime = CreateRuntime();
            ControlCenterSessionObservation seedYike = new ControlCenterSessionObservation(0)
                .WithYikeWindowContext(new YikeWindowContext { RoomToken = "seed" });
            ControlCenterSessionObservation nullYike = new ControlCenterSessionObservation(0)
                .WithYikeWindowContext(new YikeWindowContext { RoomToken = null });
            ControlCenterSessionObservation underscoreYike = new ControlCenterSessionObservation(0)
                .WithYikeWindowContext(new YikeWindowContext { RoomToken = "_" });

            Assert.Equal(
                ControlCenterSessionObservationApplyOutcome.Applied,
                runtime.ApplyObservation(seedYike).Outcome);
            Assert.Equal(
                ControlCenterSessionObservationApplyOutcome.Applied,
                runtime.ApplyObservation(nullYike).Outcome);
            ControlCenterSessionObservationApplyResult yikeResult = runtime.ApplyObservation(underscoreYike);

            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Applied, yikeResult.Outcome);
            Assert.Equal("_", yikeResult.Snapshot.YikeWindowContext.RoomToken);
        }

        [Fact]
        public void RepeatedObservation_ReappliesAfterRuntimeSessionMutation()
        {
            ControlCenterRuntime runtime = CreateRuntime();
            FoxWindowContext firstContext = new FoxWindowContext
            {
                Kind = FoxWindowKind.LiveRoom,
                LiveRoomState = FoxLiveRoomState.Playing,
                RoomToken = "first"
            };
            ControlCenterSessionObservation observation = new ControlCenterSessionObservation(0)
                .WithFoxWindowContext(firstContext);

            Assert.Equal(
                ControlCenterSessionObservationApplyOutcome.Applied,
                runtime.ApplyObservation(observation).Outcome);
            runtime.UpdateAutoPlayObservation(
                "external",
                new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    LiveRoomState = FoxLiveRoomState.Playing,
                    RoomToken = "second"
                },
                null);

            ControlCenterSessionObservationApplyResult reapplied = runtime.ApplyObservation(observation);

            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Applied, reapplied.Outcome);
            Assert.Equal("first", reapplied.Snapshot.FoxWindowContext.RoomToken);
        }

        [Fact]
        public void OlderGeneration_IsIgnoredAfterNewerObservationIsAccepted()
        {
            ControlCenterRuntime runtime = CreateRuntime();
            long newerGeneration = runtime.BeginSessionObservationGeneration();
            ControlCenterSessionObservationApplyResult newer = runtime.ApplyObservation(
                new ControlCenterSessionObservation(newerGeneration)
                    .WithHostConnected(true));

            ControlCenterSessionObservationApplyResult stale = runtime.ApplyObservation(
                new ControlCenterSessionObservation(0)
                    .WithBoardRegion(true, true)
                    .WithRecentSync("late", 99, "late")
                    .WithSemanticLog("WARN", "WebView_late"));

            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Applied, newer.Outcome);
            Assert.Equal(ControlCenterSessionObservationApplyOutcome.Stale, stale.Outcome);
            Assert.False(stale.Snapshot.BoardRegionRecognized);
            Assert.Equal(0, stale.Snapshot.StoneCount);
            Assert.Null(stale.Snapshot.LastSync);
            Assert.True(stale.IsStale);
            Assert.Empty(stale.SemanticMessages);
            Assert.False(stale.ShouldPublishSnapshot);
        }

        private static ControlCenterRuntime CreateRuntime()
        {
            return new ControlCenterRuntime(ControlCenterPreferences.FromConfig(AppConfig.CreateDefault("220430", "TEST")), new RecordingSessionAdapter(), new RecordingPersistence(), new RejectingControlCenterActionAdapter());
        }

        private sealed class RecordingSessionAdapter : IControlCenterSessionAdapter
        {
            public bool HasActiveSyncOperation { get; set; }

            public void Apply(
                ControlCenterPreferences preferences,
                ControlCenterSessionState sessionState)
            {
            }
        }

        private sealed class RecordingYikeContextAdapter : IYikeContextAdapter
        {
            private readonly ControlCenterRuntime controlCenter;

            public RecordingYikeContextAdapter(ControlCenterRuntime controlCenter)
            {
                this.controlCenter = controlCenter;
            }

            public YikeWindowContext StoredContext { get; private set; }
            public YikeWindowContext CoordinatorContext { get; private set; }
            public int TitleApplyCount { get; private set; }
            public int PublicationCount { get; private set; }

            public long CaptureObservationGeneration()
            {
                return controlCenter.CaptureSessionObservationGeneration();
            }

            public void StoreContext(YikeWindowContext context)
            {
                StoredContext = context;
            }

            public void SetCoordinatorContext(YikeWindowContext context)
            {
                CoordinatorContext = context;
            }

            public void ApplyTitle()
            {
                TitleApplyCount++;
            }

            public ControlCenterSessionObservationApplyResult ApplyObservation(
                ControlCenterSessionObservation observation)
            {
                ControlCenterSessionObservationApplyResult result =
                    controlCenter.ApplyObservation(observation);
                if (result.ShouldPublishSnapshot)
                    PublicationCount++;
                return result;
            }
        }

        private sealed class RecordingPersistence : IControlCenterPreferencePersistence
        {
            public void Save(ControlCenterPreferences preferences)
            {
            }
        }

    }
}
