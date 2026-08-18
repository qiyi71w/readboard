using System;

namespace readboard
{
    internal interface IYikeContextAdapter
    {
        long CaptureObservationGeneration();
        void StoreContext(YikeWindowContext context);
        void SetCoordinatorContext(YikeWindowContext context);
        void ApplyTitle();
        ControlCenterSessionObservationApplyResult ApplyObservation(
            ControlCenterSessionObservation observation);
    }

    internal sealed class YikeContextRuntime
    {
        private readonly IYikeContextAdapter adapter;

        public YikeContextRuntime(IYikeContextAdapter adapter)
        {
            this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public ControlCenterSessionObservationApplyResult Apply(YikeWindowContext context)
        {
            YikeWindowContext copy = YikeWindowContext.CopyOf(context);
            adapter.StoreContext(copy);
            adapter.SetCoordinatorContext(copy);
            adapter.ApplyTitle();
            return adapter.ApplyObservation(
                new ControlCenterSessionObservation(adapter.CaptureObservationGeneration())
                    .WithYikeWindowContext(copy));
        }
    }
}
