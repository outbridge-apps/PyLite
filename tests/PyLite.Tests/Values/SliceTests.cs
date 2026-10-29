using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class SliceTests
    {
        [Fact]
        public void NegativeStepFull()
        {
            var ctx = Rt.Ctx();
            var sl = ctx.Values.Slice(ctx.Values.None, ctx.Values.None, ctx.Values.Int(-1));
            var ind = sl.Indices(10, ctx);
            Assert.Equal((BigInteger)9, ind.Start);
            Assert.Equal((BigInteger)(-1), ind.Stop);
            Assert.Equal((BigInteger)(-1), ind.Step);
            Assert.Equal((BigInteger)10, ind.Length);
        }

        [Fact]
        public void ClampedBounds()
        {
            var ctx = Rt.Ctx();
            var sl = ctx.Values.Slice(ctx.Values.Int(100), ctx.Values.Int(-100), ctx.Values.Int(-1));
            var ind = sl.Indices(10, ctx);
            Assert.Equal((BigInteger)9, ind.Start);
            Assert.Equal((BigInteger)(-1), ind.Stop);
            Assert.Equal((BigInteger)10, ind.Length);
        }

        [Fact]
        public void HugeStepClampsInsteadOfOverflowing()
        {
            var ctx = Rt.Ctx();
            var big = ctx.Values.Int(BigInteger.Pow(10, 100));
            var sl = ctx.Values.Slice(ctx.Values.None, ctx.Values.None, big);
            var ind = sl.Indices(3, ctx);
            Assert.Equal((BigInteger)1, ind.Length);      // a stride past the sequence takes just the first item
            Assert.Equal(int.MaxValue, ind.StepInt);      // clamped, never (int)10**100 which would overflow
        }

        [Fact]
        public void HugeNegativeStepClampsInsteadOfOverflowing()
        {
            var ctx = Rt.Ctx();
            var big = ctx.Values.Int(-BigInteger.Pow(10, 100));
            var sl = ctx.Values.Slice(ctx.Values.None, ctx.Values.None, big);
            var ind = sl.Indices(3, ctx);
            Assert.Equal((BigInteger)1, ind.Length);
            Assert.Equal(int.MinValue, ind.StepInt);
        }

        [Fact]
        public void ZeroStepRejected()
        {
            var ctx = Rt.Ctx();
            var sl = ctx.Values.Slice(ctx.Values.Int(1), ctx.Values.Int(2), ctx.Values.Int(0));
            var ex = Assert.Throws<ScriptException>(() => sl.Indices(10, ctx));
            Assert.Equal("slice step cannot be zero", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void ReprForm()
        {
            var ctx = Rt.Ctx();
            var sl = ctx.Values.Slice(ctx.Values.Int(1), ctx.Values.Int(5), ctx.Values.None);
            Assert.Equal("slice(1, 5, None)", sl.Repr(ctx));
        }

        // A bool component is a valid index (True==1, False==0) and must take the same fast path as an int.
        [Fact]
        public void BoolIndicesNormalizeLikeInts()
        {
            var ctx = Rt.Ctx();
            var sl = ctx.Values.Slice(BoolValue.True, BoolValue.True, ctx.Values.None);
            var ind = sl.Indices(10, ctx);
            Assert.Equal((BigInteger)1, ind.Start);
            Assert.Equal((BigInteger)1, ind.Stop);
            Assert.Equal((BigInteger)0, ind.Length);   // s[1:1] is empty
        }

        // long.MinValue can't be negated without overflow, so it is excluded from the long fast path and
        // handled by the exact BigInteger path — a stride that huge still just takes the first (last) item.
        [Fact]
        public void LongMinStepFallsToBigPath()
        {
            var ctx = Rt.Ctx();
            var sl = ctx.Values.Slice(ctx.Values.None, ctx.Values.None, ctx.Values.Int(long.MinValue));
            var ind = sl.Indices(3, ctx);
            Assert.Equal((BigInteger)1, ind.Length);
            Assert.Equal((BigInteger)2, ind.Start);   // negative step starts at the end
            Assert.Equal(int.MinValue, ind.StepInt);
        }

        // Cross-check the fast path's Length (computed by ceil-division) against an independent oracle that
        // COUNTS selected indices, over a grid of signs / bounds / steps / lengths. Catches any off-by-one.
        [Fact]
        public void FastPathLengthMatchesCountingOracle()
        {
            var ctx = Rt.Ctx();
            int[] lens = { 0, 1, 2, 5, 10 };
            int?[] bounds = { null, -12, -5, -1, 0, 1, 3, 5, 9, 12 };
            int[] steps = { -3, -2, -1, 1, 2, 3 };
            foreach (int len in lens)
                foreach (int? start in bounds)
                    foreach (int? stop in bounds)
                        foreach (int step in steps)
                        {
                            var sl = ctx.Values.Slice(
                                start.HasValue ? (ScriptValue)ctx.Values.Int(start.Value) : ctx.Values.None,
                                stop.HasValue ? (ScriptValue)ctx.Values.Int(stop.Value) : ctx.Values.None,
                                ctx.Values.Int(step));
                            var ind = sl.Indices(len, ctx);
                            long expLen = CountSelected(len, start, stop, step, out long expStart);
                            Assert.Equal((BigInteger)expLen, ind.Length);
                            if (expLen > 0)
                                Assert.Equal((BigInteger)expStart, ind.Start);
                        }
        }

        // Independent reference: normalise bounds per CPython, then COUNT the range (does not reuse the
        // ceil-division length formula under test).
        private static long CountSelected(long len, int? start, int? stop, long step, out long normStart)
        {
            bool neg = step < 0;
            long s = start.HasValue ? Clamp(start.Value, len, neg) : (neg ? len - 1 : 0);
            long e = stop.HasValue ? Clamp(stop.Value, len, neg) : (neg ? -1 : len);
            normStart = s;
            long n = 0;
            if (neg)
                for (long i = s; i > e; i += step) n++;
            else
                for (long i = s; i < e; i += step) n++;
            return n;
        }

        private static long Clamp(long x, long len, bool neg)
        {
            if (x < 0)
            {
                x += len;
                if (x < 0)
                    x = neg ? -1 : 0;
            }
            else if (x >= len)
                x = neg ? len - 1 : len;
            return x;
        }
    }
}
