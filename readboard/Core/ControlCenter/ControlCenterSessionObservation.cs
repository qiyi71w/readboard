using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace readboard
{
    internal sealed class ControlCenterSemanticMessage
    {
        public ControlCenterSemanticMessage(
            string key,
            string diagnosticDetail = null,
            string level = "INFO")
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Semantic message key is required.", "key");
            if (string.IsNullOrWhiteSpace(level))
                throw new ArgumentException("Semantic message level is required.", "level");

            Key = key;
            DiagnosticDetail = diagnosticDetail;
            Level = level;
        }

        public string Key { get; private set; }
        public string DiagnosticDetail { get; private set; }
        public string Level { get; private set; }
    }

    internal enum ControlCenterSessionObservationApplyOutcome
    {
        Applied = 0,
        NoOp = 1,
        Stale = 2
    }

    internal sealed class ControlCenterSessionObservation
    {
        [Flags]
        private enum UpdateMask
        {
            None = 0,
            TargetWindowValid = 1 << 0,
            FoxWindowContext = 1 << 1,
            YikeWindowContext = 1 << 2,
            BoardRegion = 1 << 3,
            PlacementRegion = 1 << 4,
            SyncActivity = 1 << 5,
            AnalysisState = 1 << 6,
            RecentSync = 1 << 7,
            TitleTurn = 1 << 8,
            HostConnected = 1 << 9
        }

        private readonly UpdateMask updateMask;
        private readonly bool? targetWindowValid;
        private readonly FoxWindowContext foxWindowContext;
        private readonly YikeWindowContext yikeWindowContext;
        private readonly bool boardRegionRecognized;
        private readonly bool placementRegionResolved;
        private readonly bool quickSyncActive;
        private readonly bool continuousSyncActive;
        private readonly bool analysisRunning;
        private readonly bool analysisStateAvailable;
        private readonly string lastSync;
        private readonly int stoneCount;
        private readonly string duration;
        private readonly MainWindowTitleTurn titleTurn;
        private readonly bool hostConnected;
        private readonly ReadOnlyCollection<ControlCenterSemanticMessage> semanticMessages;

        public ControlCenterSessionObservation(long generation)
            : this(
                generation,
                UpdateMask.None,
                null,
                null,
                null,
                false,
                false,
                false,
                false,
                false,
                false,
                null,
                0,
                null,
                MainWindowTitleTurn.None,
                false,
                new List<ControlCenterSemanticMessage>())
        {
            if (generation < 0)
                throw new ArgumentOutOfRangeException("generation");
        }

        private ControlCenterSessionObservation(
            long generation,
            UpdateMask updateMask,
            bool? targetWindowValid,
            FoxWindowContext foxWindowContext,
            YikeWindowContext yikeWindowContext,
            bool boardRegionRecognized,
            bool placementRegionResolved,
            bool quickSyncActive,
            bool continuousSyncActive,
            bool analysisRunning,
            bool analysisStateAvailable,
            string lastSync,
            int stoneCount,
            string duration,
            MainWindowTitleTurn titleTurn,
            bool hostConnected,
            IList<ControlCenterSemanticMessage> semanticMessages)
        {
            if (generation < 0)
                throw new ArgumentOutOfRangeException("generation");
            if (semanticMessages == null)
                throw new ArgumentNullException("semanticMessages");

            Generation = generation;
            this.updateMask = updateMask;
            this.targetWindowValid = targetWindowValid;
            this.foxWindowContext = global::readboard.FoxWindowContext.CopyOf(foxWindowContext);
            this.yikeWindowContext = global::readboard.YikeWindowContext.CopyOf(yikeWindowContext);
            this.boardRegionRecognized = boardRegionRecognized;
            this.placementRegionResolved = placementRegionResolved;
            this.quickSyncActive = quickSyncActive;
            this.continuousSyncActive = continuousSyncActive;
            this.analysisRunning = analysisRunning;
            this.analysisStateAvailable = analysisStateAvailable;
            this.lastSync = lastSync;
            this.stoneCount = stoneCount;
            this.duration = duration;
            this.titleTurn = titleTurn;
            this.hostConnected = hostConnected;
            this.semanticMessages = new ReadOnlyCollection<ControlCenterSemanticMessage>(
                new List<ControlCenterSemanticMessage>(semanticMessages));
        }

        public long Generation { get; private set; }

        public bool HasTargetWindowValid
        {
            get { return (updateMask & UpdateMask.TargetWindowValid) != 0; }
        }

        public bool? TargetWindowValid
        {
            get { return targetWindowValid; }
        }

        public bool HasFoxWindowContext
        {
            get { return (updateMask & UpdateMask.FoxWindowContext) != 0; }
        }

        public FoxWindowContext FoxWindowContext
        {
            get { return global::readboard.FoxWindowContext.CopyOf(foxWindowContext); }
        }

        public bool HasYikeWindowContext
        {
            get { return (updateMask & UpdateMask.YikeWindowContext) != 0; }
        }

        public YikeWindowContext YikeWindowContext
        {
            get { return global::readboard.YikeWindowContext.CopyOf(yikeWindowContext); }
        }

        public bool HasBoardRegion
        {
            get { return (updateMask & UpdateMask.BoardRegion) != 0; }
        }

        public bool BoardRegionRecognized
        {
            get { return boardRegionRecognized; }
        }

        public bool HasPlacementRegion
        {
            get { return (updateMask & UpdateMask.PlacementRegion) != 0; }
        }

        public bool PlacementRegionResolved
        {
            get { return placementRegionResolved; }
        }

        public bool HasSyncActivity
        {
            get { return (updateMask & UpdateMask.SyncActivity) != 0; }
        }

        public bool QuickSyncActive
        {
            get { return quickSyncActive; }
        }

        public bool ContinuousSyncActive
        {
            get { return continuousSyncActive; }
        }

        public bool HasAnalysisState
        {
            get { return (updateMask & UpdateMask.AnalysisState) != 0; }
        }

        public bool AnalysisRunning
        {
            get { return analysisRunning; }
        }

        public bool AnalysisStateAvailable
        {
            get { return analysisStateAvailable; }
        }

        public bool HasRecentSync
        {
            get { return (updateMask & UpdateMask.RecentSync) != 0; }
        }

        public string LastSync
        {
            get { return lastSync; }
        }

        public int StoneCount
        {
            get { return stoneCount; }
        }

        public string Duration
        {
            get { return duration; }
        }

        public bool HasTitleTurn
        {
            get { return (updateMask & UpdateMask.TitleTurn) != 0; }
        }

        public MainWindowTitleTurn TitleTurn
        {
            get { return titleTurn; }
        }

        public bool HasHostConnected
        {
            get { return (updateMask & UpdateMask.HostConnected) != 0; }
        }

        public bool HostConnected
        {
            get { return hostConnected; }
        }

        public IReadOnlyList<ControlCenterSemanticMessage> SemanticMessages
        {
            get { return semanticMessages; }
        }

        public ControlCenterSessionObservation WithTargetWindowValid(bool? value)
        {
            return Copy(UpdateMask.TargetWindowValid, value, null, null, false, false, false, false, false, false, null, 0, null, MainWindowTitleTurn.None, false, null);
        }

        public ControlCenterSessionObservation WithFoxWindowContext(FoxWindowContext value)
        {
            return Copy(UpdateMask.FoxWindowContext, null, value, null, false, false, false, false, false, false, null, 0, null, MainWindowTitleTurn.None, false, null);
        }

        public ControlCenterSessionObservation WithYikeWindowContext(YikeWindowContext value)
        {
            return Copy(UpdateMask.YikeWindowContext, null, null, value, false, false, false, false, false, false, null, 0, null, MainWindowTitleTurn.None, false, null);
        }

        public ControlCenterSessionObservation WithBoardRegion(bool boardRecognized, bool placementResolved)
        {
            return Copy(UpdateMask.BoardRegion | UpdateMask.PlacementRegion, null, null, null, boardRecognized, placementResolved, false, false, false, false, null, 0, null, MainWindowTitleTurn.None, false, null);
        }

        public ControlCenterSessionObservation WithSyncActivity(bool quickActive, bool continuousActive)
        {
            return Copy(UpdateMask.SyncActivity, null, null, null, false, false, quickActive, continuousActive, false, false, null, 0, null, MainWindowTitleTurn.None, false, null);
        }

        public ControlCenterSessionObservation WithAnalysisState(bool running, bool available)
        {
            return Copy(UpdateMask.AnalysisState, null, null, null, false, false, false, false, running, available, null, 0, null, MainWindowTitleTurn.None, false, null);
        }

        public ControlCenterSessionObservation WithRecentSync(
            string lastSync,
            int stoneCount,
            string duration)
        {
            if (stoneCount < 0)
                throw new ArgumentOutOfRangeException("stoneCount");
            return Copy(UpdateMask.RecentSync, null, null, null, false, false, false, false, false, false, lastSync, stoneCount, duration, MainWindowTitleTurn.None, false, null);
        }

        public ControlCenterSessionObservation WithTitleTurn(MainWindowTitleTurn value)
        {
            return Copy(UpdateMask.TitleTurn, null, null, null, false, false, false, false, false, false, null, 0, null, value, false, null);
        }

        public ControlCenterSessionObservation WithHostConnected(bool connected)
        {
            return Copy(UpdateMask.HostConnected, null, null, null, false, false, false, false, false, false, null, 0, null, MainWindowTitleTurn.None, connected, null);
        }

        public ControlCenterSessionObservation WithSemanticLog(
            string level,
            string key,
            string diagnosticDetail = null)
        {
            if (string.IsNullOrWhiteSpace(level))
                throw new ArgumentException("Log level is required.", "level");
            return Copy(
                UpdateMask.None,
                null,
                null,
                null,
                false,
                false,
                false,
                false,
                false,
                false,
                null,
                0,
                null,
                MainWindowTitleTurn.None,
                false,
                new[] { new ControlCenterSemanticMessage(key, diagnosticDetail, level) });
        }

        public ControlCenterSessionObservation ClearRuntimeFrame()
        {
            return WithBoardRegion(false, false).WithRecentSync(null, 0, null);
        }

        internal string Fingerprint
        {
            get
            {
                StringBuilder builder = new StringBuilder();
                AppendFingerprintField(builder, Generation.ToString(CultureInfo.InvariantCulture));
                AppendFingerprintField(builder, ((int)updateMask).ToString(CultureInfo.InvariantCulture));
                if (HasTargetWindowValid)
                    AppendFingerprintField(builder, targetWindowValid.HasValue ? (targetWindowValid.Value ? "1" : "0") : "n");
                if (HasFoxWindowContext)
                    AppendFingerprintField(builder, BuildFoxContextSignature(foxWindowContext));
                if (HasYikeWindowContext)
                    AppendFingerprintField(builder, BuildYikeContextSignature(yikeWindowContext));
                if (HasBoardRegion)
                    AppendFingerprintField(builder, boardRegionRecognized ? "1" : "0");
                if (HasPlacementRegion)
                    AppendFingerprintField(builder, placementRegionResolved ? "1" : "0");
                if (HasSyncActivity)
                {
                    AppendFingerprintField(builder, quickSyncActive ? "1" : "0");
                    AppendFingerprintField(builder, continuousSyncActive ? "1" : "0");
                }
                if (HasAnalysisState)
                {
                    AppendFingerprintField(builder, analysisRunning ? "1" : "0");
                    AppendFingerprintField(builder, analysisStateAvailable ? "1" : "0");
                }
                if (HasRecentSync)
                {
                    AppendFingerprintField(builder, lastSync);
                    AppendFingerprintField(builder, stoneCount.ToString(CultureInfo.InvariantCulture));
                    AppendFingerprintField(builder, duration);
                }
                if (HasTitleTurn)
                    AppendFingerprintField(builder, ((int)titleTurn).ToString(CultureInfo.InvariantCulture));
                if (HasHostConnected)
                    AppendFingerprintField(builder, hostConnected ? "1" : "0");
                for (int i = 0; i < semanticMessages.Count; i++)
                {
                    AppendFingerprintField(builder, semanticMessages[i].Level);
                    AppendFingerprintField(builder, semanticMessages[i].Key);
                    AppendFingerprintField(builder, semanticMessages[i].DiagnosticDetail);
                }
                return builder.ToString();
            }
        }

        private static void AppendFingerprintField(StringBuilder builder, string value)
        {
            if (value == null)
            {
                builder.Append("-1:");
                return;
            }

            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(value);
        }

        private ControlCenterSessionObservation Copy(
            UpdateMask additionalMask,
            bool? targetWindowValid,
            FoxWindowContext foxWindowContext,
            YikeWindowContext yikeWindowContext,
            bool boardRegionRecognized,
            bool placementRegionResolved,
            bool quickSyncActive,
            bool continuousSyncActive,
            bool analysisRunning,
            bool analysisStateAvailable,
            string lastSync,
            int stoneCount,
            string duration,
            MainWindowTitleTurn titleTurn,
            bool hostConnected,
            IList<ControlCenterSemanticMessage> messages)
        {
            UpdateMask nextMask = updateMask | additionalMask;
            bool setTarget = (additionalMask & UpdateMask.TargetWindowValid) != 0;
            bool setFox = (additionalMask & UpdateMask.FoxWindowContext) != 0;
            bool setYike = (additionalMask & UpdateMask.YikeWindowContext) != 0;
            bool setBoard = (additionalMask & UpdateMask.BoardRegion) != 0;
            bool setPlacement = (additionalMask & UpdateMask.PlacementRegion) != 0;
            bool setSync = (additionalMask & UpdateMask.SyncActivity) != 0;
            bool setAnalysis = (additionalMask & UpdateMask.AnalysisState) != 0;
            bool setRecent = (additionalMask & UpdateMask.RecentSync) != 0;
            bool setTitle = (additionalMask & UpdateMask.TitleTurn) != 0;
            bool setHost = (additionalMask & UpdateMask.HostConnected) != 0;
            bool? nextTarget = setTarget || !HasTargetWindowValid ? targetWindowValid : this.targetWindowValid;
            FoxWindowContext nextFox = setFox || !HasFoxWindowContext ? foxWindowContext : this.foxWindowContext;
            YikeWindowContext nextYike = setYike || !HasYikeWindowContext ? yikeWindowContext : this.yikeWindowContext;
            bool nextBoard = setBoard || !HasBoardRegion ? boardRegionRecognized : this.boardRegionRecognized;
            bool nextPlacement = setPlacement || !HasPlacementRegion ? placementRegionResolved : this.placementRegionResolved;
            bool nextQuick = setSync || !HasSyncActivity ? quickSyncActive : this.quickSyncActive;
            bool nextContinuous = setSync || !HasSyncActivity ? continuousSyncActive : this.continuousSyncActive;
            bool nextAnalysis = setAnalysis || !HasAnalysisState ? analysisRunning : this.analysisRunning;
            bool nextAnalysisAvailable = setAnalysis || !HasAnalysisState ? analysisStateAvailable : this.analysisStateAvailable;
            string nextLastSync = setRecent || !HasRecentSync ? lastSync : this.lastSync;
            int nextStoneCount = setRecent || !HasRecentSync ? stoneCount : this.stoneCount;
            string nextDuration = setRecent || !HasRecentSync ? duration : this.duration;
            MainWindowTitleTurn nextTitleTurn = setTitle || !HasTitleTurn ? titleTurn : this.titleTurn;
            bool nextHostConnected = setHost || !HasHostConnected ? hostConnected : this.hostConnected;
            List<ControlCenterSemanticMessage> nextMessages = new List<ControlCenterSemanticMessage>(semanticMessages);
            if (messages != null)
                nextMessages.AddRange(messages);

            return new ControlCenterSessionObservation(
                Generation,
                nextMask,
                nextTarget,
                nextFox,
                nextYike,
                nextBoard,
                nextPlacement,
                nextQuick,
                nextContinuous,
                nextAnalysis,
                nextAnalysisAvailable,
                nextLastSync,
                nextStoneCount,
                nextDuration,
                nextTitleTurn,
                nextHostConnected,
                nextMessages);
        }

        private static string BuildFoxContextSignature(FoxWindowContext context)
        {
            if (context == null)
                return "unknown";
            StringBuilder builder = new StringBuilder();
            AppendFingerprintField(
                builder,
                ((int)context.Kind).ToString(CultureInfo.InvariantCulture));
            AppendFingerprintField(
                builder,
                ((int)context.LiveRoomState).ToString(CultureInfo.InvariantCulture));
            AppendFingerprintField(builder, context.RoomToken);
            AppendFingerprintField(
                builder,
                context.LiveTitleMove.HasValue
                    ? context.LiveTitleMove.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty);
            AppendFingerprintField(
                builder,
                context.RecordCurrentMove.HasValue
                    ? context.RecordCurrentMove.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty);
            AppendFingerprintField(
                builder,
                context.RecordTotalMove.HasValue
                    ? context.RecordTotalMove.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty);
            AppendFingerprintField(builder, context.RecordAtEnd ? "1" : "0");
            AppendFingerprintField(builder, context.TitleFingerprint);
            return builder.ToString();
        }

        private static string BuildYikeContextSignature(YikeWindowContext context)
        {
            if (context == null)
                return "unknown";

            StringBuilder builder = new StringBuilder();
            AppendFingerprintField(builder, context.RoomToken);
            AppendFingerprintField(
                builder,
                context.MoveNumber.HasValue
                    ? context.MoveNumber.Value.ToString(CultureInfo.InvariantCulture)
                    : null);
            return builder.ToString();
        }
    }

    internal sealed class ControlCenterSessionObservationApplyResult
    {
        public ControlCenterSessionObservationApplyResult(
            ControlCenterSessionObservationApplyOutcome outcome,
            ControlCenterRuntimeSnapshot snapshot,
            IReadOnlyList<ControlCenterSemanticMessage> semanticMessages)
        {
            Outcome = outcome;
            Snapshot = snapshot;
            SemanticMessages = semanticMessages ?? new List<ControlCenterSemanticMessage>();
        }

        public ControlCenterSessionObservationApplyOutcome Outcome { get; private set; }
        public ControlCenterRuntimeSnapshot Snapshot { get; private set; }
        public IReadOnlyList<ControlCenterSemanticMessage> SemanticMessages { get; private set; }
        public bool ShouldPublishSnapshot
        {
            get { return Outcome == ControlCenterSessionObservationApplyOutcome.Applied; }
        }
        public bool IsStale
        {
            get { return Outcome == ControlCenterSessionObservationApplyOutcome.Stale; }
        }
    }
}
