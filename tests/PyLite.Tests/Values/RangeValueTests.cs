using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class RangeValueTests
    {
        private static RangeValue R(EvalContext ctx, BigInteger start, BigInteger stop, BigInteger step) => ctx.Values.Range(start, stop, step);
        private static List<BigInteger> ToList(EvalContext ctx, RangeValue r)
        {
            var it = r.GetIteratorCore(ctx);
            var res = new List<BigInteger>();
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
                res.Add(((IntValue)v).Value);
            return res;
        }
        private static bool In(EvalContext ctx, ScriptValue x, RangeValue r) => PyOps.Contains(x, r, ctx);

        [Fact] public void LengthStep3() { var ctx = Rt.Ctx(); Assert.Equal(4, PyOps.Length(R(ctx, 0, 10, 3), ctx)); }

        [Fact]
        public void NegativeStepListing()
        {
            var ctx = Rt.Ctx();
            Assert.Equal(3, PyOps.Length(R(ctx, 5, 0, -2), ctx));
            Assert.Equal(new[] { (BigInteger)5, 3, 1 }, ToList(ctx, R(ctx, 5, 0, -2)));
        }

        [Fact]
        public void Indexing()
        {
            var ctx = Rt.Ctx();
            Assert.Equal((BigInteger)9, ((IntValue)R(ctx, 0, 10, 1).GetItem(ctx.Values.Int(-1), ctx)).Value);
            var ex = Assert.Throws<ScriptException>(() => R(ctx, 0, 10, 1).GetItem(ctx.Values.Int(10), ctx));
            Assert.Equal("range object index out of range", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void EqualityAndHash()
        {
            var ctx = Rt.Ctx();
            Assert.True(((BoolValue)PyOps.RichCompare(PyCmpOp.Eq, R(ctx, 0, 5, 2), R(ctx, 0, 6, 2), ctx)).Value);
            Assert.True(((BoolValue)PyOps.RichCompare(PyCmpOp.Eq, R(ctx, 0, 0, 1), R(ctx, 2, 2, 1), ctx)).Value);
            Assert.Equal(R(ctx, 0, 0, 1).PyHash(ctx), R(ctx, 2, 2, 1).PyHash(ctx));
        }

        [Fact]
        public void MembershipInt()
        {
            var ctx = Rt.Ctx();
            Assert.True(In(ctx, ctx.Values.Int(5), R(ctx, 0, 10, 1)));
            Assert.False(In(ctx, ctx.Values.Int(10), R(ctx, 0, 10, 1)));
            Assert.False(In(ctx, ctx.Values.Int(-1), R(ctx, 0, 10, 1)));
            Assert.True(In(ctx, ctx.Values.Int(4), R(ctx, 0, 10, 2)));
            Assert.False(In(ctx, ctx.Values.Int(3), R(ctx, 0, 10, 2)));
        }

        [Fact]
        public void MembershipFloatFallback()
        {
            var ctx = Rt.Ctx();
            Assert.True(In(ctx, ctx.Values.Float(2.0), R(ctx, 0, 3, 1)));
            Assert.False(In(ctx, ctx.Values.Float(2.5), R(ctx, 0, 3, 1)));
        }

        [Fact]
        public void MembershipBigIntO1()
        {
            var ctx = Rt.Ctx();
            Assert.True(In(ctx, ctx.Values.Int(BigInteger.Pow(10, 20)), R(ctx, 0, BigInteger.Pow(10, 21), 1)));
        }

        [Fact]
        public void HugeLengthOverflow()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => PyOps.Length(R(ctx, 0, BigInteger.Pow(10, 30), 1), ctx));
            Assert.Equal("Python int too large to convert to C ssize_t", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void ReverseSlice()
        {
            var ctx = Rt.Ctx();
            var r = (RangeValue)R(ctx, 0, 10, 1).GetItem(ctx.Values.Slice(ctx.Values.None, ctx.Values.None, ctx.Values.Int(-1)), ctx);
            Assert.Equal((BigInteger)9, r.Start);
            Assert.Equal((BigInteger)(-1), r.Stop);
            Assert.Equal((BigInteger)(-1), r.Step);
        }

        [Fact]
        public void Repr()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("range(0, 5)", R(ctx, 0, 5, 1).Repr(ctx));
            Assert.Equal("range(0, 10, 2)", R(ctx, 0, 10, 2).Repr(ctx));
        }

        [Fact]
        public void Unorderable()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => PyOps.RichCompare(PyCmpOp.Lt, R(ctx, 0, 2, 1), R(ctx, 0, 3, 1), ctx));
            Assert.Equal("'<' not supported between instances of 'range' and 'range'", ex.Value.GetMessageText(ctx));
        }
    }
}
