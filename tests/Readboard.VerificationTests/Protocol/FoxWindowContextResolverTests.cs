using System;
using System.Collections.Generic;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class FoxWindowContextResolverTests
    {
        [Fact]
        public void Resolve_UsesAncestorLiveRoomTitleWhenSelectedBoardPanelTitleIsNotParseable()
        {
            IntPtr boardHandle = new IntPtr(1);
            IntPtr roomPanelHandle = new IntPtr(2);
            IntPtr rootHandle = new IntPtr(3);
            StubWindowDescriptorFactory descriptorFactory = new StubWindowDescriptorFactory(
                new Dictionary<IntPtr, string>
                {
                    [boardHandle] = "CChessboardPanel",
                    [roomPanelHandle] = "CRoomPanel",
                    [rootHandle] = "> [高级房1] > 43838号对弈房 观战中[第89手] - 升降级"
                });

            FoxWindowContext context = FoxWindowContextResolver.Resolve(
                boardHandle,
                descriptorFactory,
                CreateParentResolver(
                    (boardHandle, roomPanelHandle),
                    (roomPanelHandle, rootHandle)));

            Assert.Equal(FoxWindowKind.LiveRoom, context.Kind);
            Assert.Equal("43838号", context.RoomToken);
            Assert.Equal(89, context.LiveTitleMove);
        }

        [Fact]
        public void Resolve_UsesAncestorRecordTitleWhenSelectedBoardPanelTitleIsNotParseable()
        {
            IntPtr boardHandle = new IntPtr(10);
            IntPtr rootHandle = new IntPtr(11);
            StubWindowDescriptorFactory descriptorFactory = new StubWindowDescriptorFactory(
                new Dictionary<IntPtr, string>
                {
                    [boardHandle] = "CChessboardPanel",
                    [rootHandle] = "棋谱欣赏 - 黑 Ouuu12138 [2段] 对白 已吃2道 [2段] - 数子规则 - 分先 - 黑中盘胜 - [总333手]"
                });

            FoxWindowContext context = FoxWindowContextResolver.Resolve(
                boardHandle,
                descriptorFactory,
                CreateParentResolver((boardHandle, rootHandle)));

            Assert.Equal(FoxWindowKind.RecordView, context.Kind);
            Assert.Equal(333, context.RecordCurrentMove);
            Assert.Equal(333, context.RecordTotalMove);
            Assert.True(context.RecordAtEnd);
        }

        [Fact]
        public void Resolve_ReturnsUnknownWhenNeitherSelectedHandleNorAncestorsHaveParseableTitles()
        {
            IntPtr boardHandle = new IntPtr(20);
            IntPtr rootHandle = new IntPtr(21);
            StubWindowDescriptorFactory descriptorFactory = new StubWindowDescriptorFactory(
                new Dictionary<IntPtr, string>
                {
                    [boardHandle] = "CChessboardPanel",
                    [rootHandle] = "Fox"
                });

            FoxWindowContext context = FoxWindowContextResolver.Resolve(
                boardHandle,
                descriptorFactory,
                CreateParentResolver((boardHandle, rootHandle)));

            Assert.Equal(FoxWindowKind.Unknown, context.Kind);
        }

        private static Func<IntPtr, IntPtr> CreateParentResolver(params (IntPtr Child, IntPtr Parent)[] pairs)
        {
            Dictionary<IntPtr, IntPtr> parents = new Dictionary<IntPtr, IntPtr>();
            for (int i = 0; i < pairs.Length; i++)
                parents[pairs[i].Child] = pairs[i].Parent;

            return delegate(IntPtr handle)
            {
                IntPtr parent;
                return parents.TryGetValue(handle, out parent) ? parent : IntPtr.Zero;
            };
        }

        private sealed class StubWindowDescriptorFactory : IWindowDescriptorFactory
        {
            private readonly Dictionary<IntPtr, string> titles;

            public StubWindowDescriptorFactory(Dictionary<IntPtr, string> titles)
            {
                this.titles = titles ?? throw new ArgumentNullException("titles");
            }

            public bool TryCreate(IntPtr handle, out WindowDescriptor descriptor)
            {
                string title;
                if (!titles.TryGetValue(handle, out title))
                {
                    descriptor = null;
                    return false;
                }

                descriptor = new WindowDescriptor
                {
                    Handle = handle,
                    Title = title
                };
                return true;
            }
        }
    }
}
