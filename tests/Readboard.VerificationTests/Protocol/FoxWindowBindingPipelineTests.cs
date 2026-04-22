using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class FoxWindowBindingPipelineTests
    {
        [Fact]
        public void TryResolve_BindsToFirstParseableAncestorOnCurrentBoardChain()
        {
            Type bindingResolverType = RequireType("readboard.FoxWindowBindingResolver");
            Type bindingType = RequireType("readboard.FoxWindowBinding");
            MethodInfo tryResolve = RequireMethod(
                bindingResolverType,
                "TryResolve",
                typeof(IntPtr),
                typeof(Func<IntPtr, string>),
                typeof(Func<IntPtr, IntPtr>),
                bindingType.MakeByRefType(),
                typeof(FoxWindowContext).MakeByRefType());

            IntPtr boardHandle = new IntPtr(1);
            IntPtr roomPanelHandle = new IntPtr(2);
            IntPtr rootHandle = new IntPtr(3);
            object[] args =
            {
                boardHandle,
                (Func<IntPtr, string>)delegate(IntPtr handle)
                {
                    if (handle == rootHandle)
                        return "> [高级房1] > 43838号对弈房 观战中[第89手] - 升降级";
                    return string.Empty;
                },
                CreateParentResolver(
                    (boardHandle, roomPanelHandle),
                    (roomPanelHandle, rootHandle)),
                null,
                null
            };

            bool resolved = (bool)tryResolve.Invoke(null, args);
            object binding = args[3];
            FoxWindowContext context = (FoxWindowContext)args[4];

            Assert.True(resolved);
            Assert.NotNull(binding);
            Assert.Equal(boardHandle, GetHandle(bindingType, binding, "BoardHandle"));
            Assert.Equal(rootHandle, GetHandle(bindingType, binding, "TitleSourceHandle"));
            Assert.Equal(FoxWindowKind.LiveRoom, GetWindowKind(bindingType, binding));
            Assert.Equal(FoxWindowKind.LiveRoom, context.Kind);
            Assert.Equal("43838号", context.RoomToken);
            Assert.Equal(89, context.LiveTitleMove);
        }

        [Fact]
        public void TryRead_UsesOnlyBoundTitleSourceHandleDuringRefresh()
        {
            Type bindingType = RequireType("readboard.FoxWindowBinding");
            Type titleReaderType = RequireType("readboard.FoxWindowTitleReader");
            MethodInfo tryRead = RequireMethod(
                titleReaderType,
                "TryRead",
                bindingType,
                typeof(IntPtr),
                typeof(Func<IntPtr, bool>),
                typeof(Func<IntPtr, string>),
                typeof(Func<IntPtr, IntPtr>),
                typeof(FoxWindowContext).MakeByRefType());

            IntPtr boardHandle = new IntPtr(10);
            IntPtr titleSourceHandle = new IntPtr(11);
            IntPtr rootHandle = new IntPtr(12);
            Dictionary<IntPtr, int> titleReads = new Dictionary<IntPtr, int>();
            object binding = CreateBinding(bindingType, boardHandle, titleSourceHandle, FoxWindowKind.RecordView);
            object[] args =
            {
                binding,
                boardHandle,
                (Func<IntPtr, bool>)(handle => handle == boardHandle || handle == titleSourceHandle || handle == rootHandle),
                (Func<IntPtr, string>)delegate(IntPtr handle)
                {
                    titleReads[handle] = titleReads.ContainsKey(handle) ? titleReads[handle] + 1 : 1;
                    if (handle == titleSourceHandle)
                        return "棋谱欣赏 - 黑 Ouuu12138 [2段] 对白 已吃2道 [2段] - 数子规则 - 分先 - 黑中盘胜 - [总333手]";
                    return string.Empty;
                },
                CreateParentResolver(
                    (boardHandle, titleSourceHandle),
                    (titleSourceHandle, rootHandle)),
                null
            };

            bool resolved = (bool)tryRead.Invoke(null, args);
            FoxWindowContext context = (FoxWindowContext)args[5];

            Assert.True(resolved);
            Assert.Equal(FoxWindowKind.RecordView, context.Kind);
            Assert.Equal(333, context.RecordCurrentMove);
            Assert.Equal(333, context.RecordTotalMove);
            Assert.True(context.RecordAtEnd);
            Assert.Equal(1, titleReads[titleSourceHandle]);
            Assert.False(titleReads.ContainsKey(boardHandle));
            Assert.False(titleReads.ContainsKey(rootHandle));
        }

        [Fact]
        public void TryRead_RejectsBindingWhenTitleSourceLeavesCurrentBoardChain()
        {
            Type bindingType = RequireType("readboard.FoxWindowBinding");
            Type titleReaderType = RequireType("readboard.FoxWindowTitleReader");
            MethodInfo tryRead = RequireMethod(
                titleReaderType,
                "TryRead",
                bindingType,
                typeof(IntPtr),
                typeof(Func<IntPtr, bool>),
                typeof(Func<IntPtr, string>),
                typeof(Func<IntPtr, IntPtr>),
                typeof(FoxWindowContext).MakeByRefType());

            IntPtr boardHandle = new IntPtr(20);
            IntPtr otherParentHandle = new IntPtr(21);
            IntPtr titleSourceHandle = new IntPtr(22);
            object binding = CreateBinding(bindingType, boardHandle, titleSourceHandle, FoxWindowKind.LiveRoom);
            object[] args =
            {
                binding,
                boardHandle,
                (Func<IntPtr, bool>)(_ => true),
                (Func<IntPtr, string>)(_ => "> [高级房1] > 43838号对弈房 观战中[第89手] - 升降级"),
                CreateParentResolver((boardHandle, otherParentHandle)),
                null
            };

            bool resolved = (bool)tryRead.Invoke(null, args);
            FoxWindowContext context = (FoxWindowContext)args[5];

            Assert.False(resolved);
            Assert.Equal(FoxWindowKind.Unknown, context.Kind);
        }

        private static Type RequireType(string fullName)
        {
            Assembly assembly = typeof(FoxWindowContext).Assembly;
            Type type = assembly.GetType(fullName, false);
            Assert.NotNull(type);
            return type;
        }

        private static MethodInfo RequireMethod(Type type, string name, params Type[] parameterTypes)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                parameterTypes,
                null);
            Assert.NotNull(method);
            return method;
        }

        private static object CreateBinding(Type bindingType, IntPtr boardHandle, IntPtr titleSourceHandle, FoxWindowKind windowKind)
        {
            object binding = Activator.CreateInstance(bindingType);
            bindingType.GetProperty("BoardHandle").SetValue(binding, boardHandle);
            bindingType.GetProperty("TitleSourceHandle").SetValue(binding, titleSourceHandle);
            bindingType.GetProperty("WindowKind").SetValue(binding, windowKind);
            return binding;
        }

        private static IntPtr GetHandle(Type bindingType, object binding, string propertyName)
        {
            return (IntPtr)bindingType.GetProperty(propertyName).GetValue(binding);
        }

        private static FoxWindowKind GetWindowKind(Type bindingType, object binding)
        {
            return (FoxWindowKind)bindingType.GetProperty("WindowKind").GetValue(binding);
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
    }
}
