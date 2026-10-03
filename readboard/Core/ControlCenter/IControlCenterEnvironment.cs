using System;

namespace readboard
{
    internal sealed class ControlCenterWindowFacts
    {
        public IntPtr Handle { get; set; }
        public FoxWindowContext Context { get; set; } = FoxWindowContext.Unknown();
        public bool BindingInvalidated { get; set; }
    }

    internal interface IControlCenterEnvironment
    {
        bool HasActiveSyncOperation { get; }
        bool? TargetWindowValid { get; }
        bool ShowInBoardHint { get; }
        DateTime UtcNow { get; }
        ControlCenterWindowFacts ReadWindow();
        FoxMatchBarReading ReadPlayers(IntPtr windowHandle, FoxWindowContext context);
        FoxMatchBarReading DiscoverIdentityCandidates(IntPtr windowHandle);
        void ProjectState(ControlCenterPreferences preferences, ControlCenterSessionState sessionState, bool platformChanged);
        void ShowOnBoardHint();
    }
}
