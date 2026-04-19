namespace readboard
{
    internal sealed class PlaceRequestExecutionResult
    {
        private static readonly PlaceRequestExecutionResult noResponse =
            new PlaceRequestExecutionResult(false, false);

        private readonly bool shouldSendResponse;
        private readonly bool success;

        private PlaceRequestExecutionResult(bool shouldSendResponse, bool success)
        {
            this.shouldSendResponse = shouldSendResponse;
            this.success = success;
        }

        public bool ShouldSendResponse
        {
            get { return shouldSendResponse; }
        }

        public bool Success
        {
            get { return success; }
        }

        public static PlaceRequestExecutionResult NoResponse
        {
            get { return noResponse; }
        }

        public static PlaceRequestExecutionResult CreateResponse(bool success)
        {
            return new PlaceRequestExecutionResult(true, success);
        }
    }
}
