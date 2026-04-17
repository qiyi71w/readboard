using System;

namespace readboard
{
    internal sealed class SyncSessionRuntimeDependencies
    {
        public ISyncCoordinatorHost Host { get; set; }
        public IBoardCaptureService CaptureService { get; set; }
        public IBoardRecognitionService RecognitionService { get; set; }
        public IMovePlacementService PlacementService { get; set; }
        public IOverlayService OverlayService { get; set; }
        public ISyncWindowLocator WindowLocator { get; set; }
        public IWindowDescriptorFactory WindowDescriptorFactory { get; set; }
    }
}
