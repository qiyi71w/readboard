using System;
using System.Collections.Generic;

namespace readboard
{
    internal enum ControlCenterActionKind
    {
        QuickSync = 0,
        ContinuousSync = 1,
        OneTimeSync = 2,
        ToggleAnalysis = 3,
        SwapOrder = 4,
        ForceRebuild = 5,
        ClearBoard = 6,
        SelectBoard = 7
    }

    internal enum ControlCenterBoardSelectionMode
    {
        Inside = 0,
        Rectangle = 1,
        Line1 = 2
    }

    internal sealed class ControlCenterActionIntent
    {
        private ControlCenterActionIntent(ControlCenterActionKind kind)
        {
            Kind = kind;
        }

        public ControlCenterActionKind Kind { get; private set; }
        public ControlCenterBoardSelectionMode BoardSelectionMode { get; private set; }

        public static ControlCenterActionIntent QuickSync()
        {
            return new ControlCenterActionIntent(ControlCenterActionKind.QuickSync);
        }

        public static ControlCenterActionIntent ContinuousSync()
        {
            return new ControlCenterActionIntent(ControlCenterActionKind.ContinuousSync);
        }

        public static ControlCenterActionIntent OneTimeSync()
        {
            return new ControlCenterActionIntent(ControlCenterActionKind.OneTimeSync);
        }

        public static ControlCenterActionIntent ToggleAnalysis()
        {
            return new ControlCenterActionIntent(ControlCenterActionKind.ToggleAnalysis);
        }

        public static ControlCenterActionIntent SwapOrder()
        {
            return new ControlCenterActionIntent(ControlCenterActionKind.SwapOrder);
        }

        public static ControlCenterActionIntent ForceRebuild()
        {
            return new ControlCenterActionIntent(ControlCenterActionKind.ForceRebuild);
        }

        public static ControlCenterActionIntent ClearBoard()
        {
            return new ControlCenterActionIntent(ControlCenterActionKind.ClearBoard);
        }

        public static ControlCenterActionIntent SelectBoard(ControlCenterBoardSelectionMode mode)
        {
            return new ControlCenterActionIntent(ControlCenterActionKind.SelectBoard)
            {
                BoardSelectionMode = mode
            };
        }
    }

    internal enum ControlCenterActionEffectKind
    {
        StartQuickSync = 0,
        StopSync = 1,
        StartContinuousSync = 2,
        RunOneTimeSync = 3,
        ResumeAnalysis = 4,
        PauseAnalysis = 5,
        SwapOrder = 6,
        ForceRebuild = 7,
        ClearBoard = 8,
        SelectBoard = 9
    }

    internal sealed class ControlCenterActionEffect
    {
        private ControlCenterActionEffect(ControlCenterActionEffectKind kind)
        {
            Kind = kind;
        }

        public ControlCenterActionEffectKind Kind { get; private set; }
        public ControlCenterBoardSelectionMode BoardSelectionMode { get; private set; }

        public static ControlCenterActionEffect StartQuickSync()
        {
            return new ControlCenterActionEffect(ControlCenterActionEffectKind.StartQuickSync);
        }

        public static ControlCenterActionEffect StopSync()
        {
            return new ControlCenterActionEffect(ControlCenterActionEffectKind.StopSync);
        }

        public static ControlCenterActionEffect StartContinuousSync()
        {
            return new ControlCenterActionEffect(ControlCenterActionEffectKind.StartContinuousSync);
        }

        public static ControlCenterActionEffect RunOneTimeSync()
        {
            return new ControlCenterActionEffect(ControlCenterActionEffectKind.RunOneTimeSync);
        }

        public static ControlCenterActionEffect ResumeAnalysis()
        {
            return new ControlCenterActionEffect(ControlCenterActionEffectKind.ResumeAnalysis);
        }

        public static ControlCenterActionEffect PauseAnalysis()
        {
            return new ControlCenterActionEffect(ControlCenterActionEffectKind.PauseAnalysis);
        }

        public static ControlCenterActionEffect SwapOrder()
        {
            return new ControlCenterActionEffect(ControlCenterActionEffectKind.SwapOrder);
        }

        public static ControlCenterActionEffect ForceRebuild()
        {
            return new ControlCenterActionEffect(ControlCenterActionEffectKind.ForceRebuild);
        }

        public static ControlCenterActionEffect ClearBoard()
        {
            return new ControlCenterActionEffect(ControlCenterActionEffectKind.ClearBoard);
        }

        public static ControlCenterActionEffect SelectBoard(ControlCenterBoardSelectionMode mode)
        {
            return new ControlCenterActionEffect(ControlCenterActionEffectKind.SelectBoard)
            {
                BoardSelectionMode = mode
            };
        }
    }

    internal enum ControlCenterActionExecutionOutcome
    {
        Applied = 0,
        NoOp = 1,
        Rejected = 2
    }

    internal enum ControlCenterActionApplyOutcome
    {
        Accepted = 0,
        NoOp = 1,
        Rejected = 2
    }

    internal sealed class ControlCenterActionApplyResult
    {
        internal ControlCenterActionApplyResult(
            ControlCenterActionApplyOutcome outcome,
            ControlCenterRuntimeSnapshot snapshot)
        {
            Outcome = outcome;
            Snapshot = snapshot;
        }

        public ControlCenterActionApplyOutcome Outcome { get; private set; }
        public ControlCenterRuntimeSnapshot Snapshot { get; private set; }
        public bool ShouldPublishSnapshot
        {
            get { return Outcome != ControlCenterActionApplyOutcome.NoOp; }
        }
    }

    internal interface IControlCenterActionAdapter
    {
        ControlCenterActionExecutionOutcome Execute(ControlCenterActionEffect effect);
    }

    internal sealed class RejectingControlCenterActionAdapter : IControlCenterActionAdapter
    {
        public ControlCenterActionExecutionOutcome Execute(ControlCenterActionEffect effect)
        {
            if (effect == null)
                throw new ArgumentNullException("effect");
            return ControlCenterActionExecutionOutcome.Rejected;
        }
    }

    internal sealed class InMemoryControlCenterActionAdapter : IControlCenterActionAdapter
    {
        private readonly Queue<ControlCenterActionExecutionOutcome> queuedOutcomes =
            new Queue<ControlCenterActionExecutionOutcome>();

        public IList<ControlCenterActionEffect> Effects { get; } =
            new List<ControlCenterActionEffect>();

        public ControlCenterActionExecutionOutcome DefaultOutcome { get; set; } =
            ControlCenterActionExecutionOutcome.Applied;

        public void EnqueueOutcome(ControlCenterActionExecutionOutcome outcome)
        {
            queuedOutcomes.Enqueue(outcome);
        }

        public ControlCenterActionExecutionOutcome Execute(ControlCenterActionEffect effect)
        {
            if (effect == null)
                throw new ArgumentNullException("effect");
            Effects.Add(effect);
            return queuedOutcomes.Count == 0
                ? DefaultOutcome
                : queuedOutcomes.Dequeue();
        }
    }
}
