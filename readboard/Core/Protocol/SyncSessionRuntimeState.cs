using System;

namespace readboard
{
    internal sealed class SyncSessionRuntimeState
    {
        public SyncSessionRuntimeState()
        {
            ResetProbeState();
        }

        public IntPtr SelectedWindowHandle { get; set; }
        public int CurrentBoardPixelWidth { get; set; }
        public int CurrentBoardPixelHeight { get; set; }
        public BoardFrame CurrentBoardFrame { get; set; }
        public YikeWindowContext LastCapturedYikeContext { get; set; }
        public string LastSentYikeContextSignature { get; set; }
        public bool PreferPrintWindow { get; set; }
        public bool InitialProbePending { get; set; }
        public bool RetriedAfterBlackFrame { get; set; }

        public void ResetProbeState()
        {
            PreferPrintWindow = true;
            InitialProbePending = true;
            RetriedAfterBlackFrame = false;
        }
    }
}
