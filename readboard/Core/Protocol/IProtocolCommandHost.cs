using System;

namespace readboard
{
    internal interface IProtocolCommandHost
    {
        void DispatchProtocolCommand(Action command);
        void HandlePlaceRequest(MoveRequest request);
        void HandleLossFocus();
        void HandleStopInBoardRequest();
        void HandleVersionRequest();
        void HandleQuitRequest();
    }
}
