using System;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class ComparisonTests
    {
        private static bool Eq(EvalContext ctx, ScriptValue a, ScriptValue b) => ((BoolValue)PyOps.RichCompare(PyCmpOp.Eq, a, b, ctx)).Value;
        private static bool Cmp(EvalContext ctx, PyCmpOp op, ScriptValue a, ScriptValue b) => ((BoolValue)PyOps.RichCompare(op, a, b, ctx)).Value;

        [Fact]
        public void CrossTypeNumericEquality()
        {
            var ctx = Rt.Ctx();
            Assert.True(Eq(ctx, ctx.Values.Int(1), ctx.Values.Float(1.0)));
            Assert.True(Eq(ctx, ctx.Values.Bool(true), ctx.Values.Int(1)));
            Assert.True(Eq(ctx, ctx.Values.Bool(true), ctx.Values.Float(1.0)));
        }

        [Fact]
        public void PrecisionBoundary()
        {
            var ctx = Rt.Ctx();
            var big = ctx.Values.Int(BigInteger.Pow(2, 53) + 1);
            var f = ctx.Values.Float(9007199254740993.0);   // rounds to 2^53
            Assert.False(Eq(ctx, big, f));
            Assert.True(Cmp(ctx, PyCmpOp.Gt, big, f));
        }

        [Fact]
        public void NanNeverEqualOrOrdered()
        {
            var ctx = Rt.Ctx();
            var nanA = ctx.Values.Float(double.NaN);
            var nanB = ctx.Values.Float(double.NaN);
            Assert.False(Eq(ctx, nanA, nanB));
            Assert.False(Cmp(ctx, PyCmpOp.Lt, nanA, ctx.Values.Int(1)));
            Assert.False(Cmp(ctx, PyCmpOp.Lt, ctx.Values.Int(1), nanA));
            Assert.False(Cmp(ctx, PyCmpOp.Le, nanA, nanB));
        }

        [Fact]
        public void UnorderableTypes()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => PyOps.RichCompare(PyCmpOp.Lt, ctx.Values.Int(3), ctx.Values.Str("a"), ctx));
            Assert.Equal("'<' not supported between instances of 'int' and 'str'", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void StringOrdering()
        {
            var ctx = Rt.Ctx();
            Assert.True(Cmp(ctx, PyCmpOp.Lt, ctx.Values.Str("Z"), ctx.Values.Str("a")));
            Assert.True(Cmp(ctx, PyCmpOp.Lt, ctx.Values.Str("abc"), ctx.Values.Str("abd")));
        }

        [Fact]
        public void SignedZero()
        {
            var ctx = Rt.Ctx();
            Assert.True(Eq(ctx, ctx.Values.Float(-0.0), ctx.Values.Int(0)));
            Assert.False(Cmp(ctx, PyCmpOp.Lt, ctx.Values.Float(-0.0), ctx.Values.Int(0)));
        }

        [Fact]
        public void BigIntVsInfinity()
        {
            var ctx = Rt.Ctx();
            var big = ctx.Values.Int(BigInteger.Pow(10, 400));
            var negBig = ctx.Values.Int(-BigInteger.Pow(10, 400));
            Assert.True(Cmp(ctx, PyCmpOp.Lt, big, ctx.Values.Float(double.PositiveInfinity)));
            Assert.True(Cmp(ctx, PyCmpOp.Gt, negBig, ctx.Values.Float(double.NegativeInfinity)));
            Assert.False(Eq(ctx, big, ctx.Values.Float(double.PositiveInfinity)));
        }

        [Fact]
        public void EqAndNeAreComplementary()
        {
            var ctx = Rt.Ctx();
            Assert.True(((BoolValue)PyOps.RichCompare(PyCmpOp.Ne, ctx.Values.Int(1), ctx.Values.Int(2), ctx)).Value);
            Assert.False(((BoolValue)PyOps.RichCompare(PyCmpOp.Ne, ctx.Values.Int(1), ctx.Values.Int(1), ctx)).Value);
        }

        [Fact]
        public void EqualImpliesEqualHash()
        {
            var ctx = Rt.Ctx();
            var rnd = new Random(20260707);
            for (int i = 0; i < 10_000; i++)
            {
                long n = (long)(rnd.NextDouble() * (1L << 40)) - (1L << 39);
                var iv = ctx.Values.Int(n);
                var fv = ctx.Values.Float(n);
                Assert.True(Eq(ctx, iv, fv));
                Assert.Equal(iv.PyHash(ctx), fv.PyHash(ctx));
            }
        }
    }
}
