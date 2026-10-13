using System;

namespace readboard
{
    internal interface IProtocolCommandHost
    {
        void DispatchProtocolCommand(Action command);
        void HandlePlaceRequest(MoveRequest request);
        void HandleYikeContext(YikeWindowContext context);
        void HandleYikeGeometry(YikeBoardGeometry geometry);
        void HandleLossFocus();
        void HandleStopInBoardRequest();
        void HandleVersionRequest();
        void HandleQuitRequest();
    }

    internal interface IAnalysisStateProtocolHost
    {
        void HandleAnalysisState(bool running);
    }
}
