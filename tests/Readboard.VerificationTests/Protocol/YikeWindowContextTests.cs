using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class YikeWindowContextTests
    {
        [Fact]
        public void unknown_has_null_room_and_move()
        {
            YikeWindowContext ctx = YikeWindowContext.Unknown();

            Assert.Null(ctx.RoomToken);
            Assert.Null(ctx.MoveNumber);
        }

        [Fact]
        public void copy_of_null_returns_unknown()
        {
            YikeWindowContext ctx = YikeWindowContext.CopyOf(null);

            Assert.NotNull(ctx);
            Assert.Null(ctx.RoomToken);
            Assert.Null(ctx.MoveNumber);
        }

        [Fact]
        public void signature_changes_when_room_or_move_changes()
        {
            var a = new YikeWindowContext { RoomToken = "65191829", MoveNumber = 16 };
            var b = new YikeWindowContext { RoomToken = "65191829", MoveNumber = 17 };
            var c = new YikeWindowContext { RoomToken = "65191830", MoveNumber = 16 };
            var aDup = new YikeWindowContext { RoomToken = "65191829", MoveNumber = 16 };

            Assert.NotEqual(a.ContextSignature, b.ContextSignature);
            Assert.NotEqual(a.ContextSignature, c.ContextSignature);
            Assert.Equal(a.ContextSignature, aDup.ContextSignature);
        }

        [Fact]
        public void unknown_signature_is_stable()
        {
            Assert.Equal(
                YikeWindowContext.Unknown().ContextSignature,
                YikeWindowContext.Unknown().ContextSignature);
        }
    }
}
