namespace readboard
{
    internal sealed class YikeWindowContext
    {
        public string RoomToken { get; set; }
        public int? MoveNumber { get; set; }

        public string ContextSignature
        {
            get
            {
                string room = string.IsNullOrWhiteSpace(RoomToken) ? "_" : RoomToken.Trim();
                string move = MoveNumber.HasValue ? MoveNumber.Value.ToString() : "_";
                return "room=" + room + ";move=" + move;
            }
        }

        public static YikeWindowContext Unknown()
        {
            return new YikeWindowContext();
        }

        public static YikeWindowContext CopyOf(YikeWindowContext ctx)
        {
            if (ctx == null)
                return Unknown();

            return new YikeWindowContext
            {
                RoomToken = ctx.RoomToken,
                MoveNumber = ctx.MoveNumber
            };
        }
    }
}
