using readboard;

namespace Readboard.VerificationTests.Host
{
    internal sealed class RejectingControlCenterActionAdapter : IControlCenterActionAdapter
    {
        public ControlCenterActionExecutionOutcome Execute(ControlCenterActionEffect effect)
        {
            return ControlCenterActionExecutionOutcome.Rejected;
        }
    }
}
