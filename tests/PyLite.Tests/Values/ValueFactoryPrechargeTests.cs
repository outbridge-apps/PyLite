using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "ResourceLimits")]
    public class ValueFactoryPrechargeTests
    {
        [Fact]
        public void EnsureStrLenOverCapAborts()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<EngineAbort>(() => ctx.Values.EnsureStrLen((long)ctx.Limits.MaxStrChars + 1));
            Assert.Equal(EngineAbortKind.Memory, ex.Kind);
            Assert.Equal("MaxStrChars", ex.LimitName);
        }

        [Fact]
        public void EnsureIntBitsOverCapAborts()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<EngineAbort>(() => ctx.Values.EnsureIntBits((long)ctx.Limits.MaxIntBits + 1));
            Assert.Equal("MaxIntBits", ex.LimitName);
        }

        [Fact]
        public void EnsureCollectionSizeOverCapAborts()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<EngineAbort>(() => ctx.Values.EnsureCollectionSize((long)ctx.Limits.MaxCollectionItems + 1));
            Assert.Equal("MaxCollectionItems", ex.LimitName);
        }

        [Fact]
        public void EnsureStrLenNegativeIsMemoryNotInvalidOp()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<EngineAbort>(() => ctx.Values.EnsureStrLen(-1));
            Assert.Equal(EngineAbortKind.Memory, ex.Kind);
        }

        [Fact]
        public void RentBuilderAndGrowCharge()
        {
            var ctx = Rt.Ctx();
            long before = ctx.Budget.AllocBytesUsed;
            var sb = ctx.Values.RentBuilder(100);
            Assert.Equal(24 + 200, ctx.Budget.AllocBytesUsed - before);
            before = ctx.Budget.AllocBytesUsed;
            ctx.Values.ChargeBuilderGrow(sb, 50);
            Assert.Equal(100, ctx.Budget.AllocBytesUsed - before);
        }

        [Fact]
        public void StrPrechargedDoesNotCharge()
        {
            var ctx = Rt.Ctx();
            long before = ctx.Budget.AllocBytesUsed;
            var s = ctx.Values.StrPrecharged("abc");
            Assert.Equal("abc", s.Value);
            Assert.Equal(0, ctx.Budget.AllocBytesUsed - before);
        }

        [Fact]
        public void ExceptionFactoryCharges()
        {
            var ctx = Rt.Ctx();
            long before = ctx.Budget.AllocBytesUsed;
            ctx.Values.Exception(PyExceptionTypes.ValueError, new ScriptValue[] { ctx.Values.Int(1) });
            Assert.Equal(64 + 8, ctx.Budget.AllocBytesUsed - before);   // Int(1) is cached => 0
        }

        [Fact]
        public void KeyErrorReprIsCappedAt512()
        {
            var ctx = Rt.Ctx();
            var bigKey = ctx.Values.Str(new string('k', 1000));   // repr = 'kkk...' length 1002
            var ke = Raise.KeyError(ctx, bigKey);
            string msg = ke.Value.GetMessageText(ctx);
            Assert.Equal(515, msg.Length);   // 512 chars + "..."
            Assert.EndsWith("...", msg);
        }
    }
}
