using System.Numerics;
using System.Threading.Tasks;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class ValueFactoryTests
    {
        [Fact]
        public void SmallIntSameInstance()
        {
            var ctx = Rt.Ctx();
            Assert.Same(ctx.Values.Int(5), ctx.Values.Int(5));
        }

        [Fact]
        public void CacheBoundaries()
        {
            var ctx = Rt.Ctx();
            Assert.Same(ctx.Values.Int(-5), ctx.Values.Int(-5));
            Assert.Same(ctx.Values.Int(256), ctx.Values.Int(256));
            Assert.Same(ctx.Values.Int(4096), ctx.Values.Int(4096));
            Assert.NotSame(ctx.Values.Int(-6), ctx.Values.Int(-6));
            Assert.NotSame(ctx.Values.Int(4097), ctx.Values.Int(4097));
        }

        [Fact]
        public void CachedIntChargesNothing()
        {
            var ctx = Rt.Ctx();
            long before = ctx.Budget.AllocRemaining;
            ctx.Values.Int(5);
            Assert.Equal(before, ctx.Budget.AllocRemaining);
        }

        [Fact]
        public void BigIntChargeByBits()
        {
            var ctx = Rt.Ctx();
            long before = ctx.Budget.AllocRemaining;
            ctx.Values.Int(BigInteger.Pow(2, 1000));
            long spent = before - ctx.Budget.AllocRemaining;
            Assert.True(spent >= 32 + 125, "spent=" + spent);
        }

        [Fact]
        public void OversizeStringAborts()
        {
            var ctx = Rt.Ctx();
            Assert.Throws<EngineAbort>(() => ctx.Values.Str(new string('a', 16_000_001)));
        }

        [Fact]
        public void StringChargeExact()
        {
            var ctx = Rt.Ctx();
            long before = ctx.Budget.AllocRemaining;
            ctx.Values.Str("abc");
            Assert.Equal(30, before - ctx.Budget.AllocRemaining);
        }

        [Fact]
        public void StringAbortsBeforeAllocationUnderTightLimit()
        {
            var ctx = Rt.Ctx(ResourceLimits.Default.With(maxAllocBytes: 1000));
            Assert.Throws<EngineAbort>(() => ctx.Values.Str(new string('a', 600)));
        }

        [Fact]
        public void SingletonsStable()
        {
            var ctx = Rt.Ctx();
            Assert.Same(ctx.Values.Bool(true), ctx.Values.Bool(true));
            Assert.Same(ctx.Values.None, ctx.Values.None);
        }

        [Fact]
        public void StrFromCharCacheAndBeyond()
        {
            var ctx = Rt.Ctx();
            Assert.Same(ctx.Values.StrFromChar('a'), ctx.Values.StrFromChar('a'));
            Assert.NotSame(ctx.Values.StrFromChar('é'), ctx.Values.StrFromChar('é'));
        }

        [Fact]
        public void BudgetStringBuilderAbortsBeforeGrowth()
        {
            var ctx = Rt.Ctx(ResourceLimits.Default.With(maxStrChars: 100_000));
            var sb = new BudgetStringBuilder(ctx);
            Assert.Throws<EngineAbort>(() => sb.Append(new string('a', 1_000_000)));
            Assert.Equal(0, sb.Length);
        }

        [Fact]
        public void FloatTruthiness()
        {
            var ctx = Rt.Ctx();
            Assert.True(ctx.Values.Float(double.NaN).IsTruthy(ctx));
            Assert.False(ctx.Values.Float(-0.0).IsTruthy(ctx));
        }

        [Fact]
        public void SmallCacheThreadSafe()
        {
            var baseline = Rt.Ctx().Values.Int(5);
            Parallel.For(0, 8, _ =>
            {
                var ctx = Rt.Ctx();
                for (int i = 0; i < 50_000; i++)
                    Assert.Same(baseline, ctx.Values.Int(5));
            });
        }
    }
}
