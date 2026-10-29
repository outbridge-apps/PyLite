using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class ContainerHashCompareTests
    {
        private static TupleValue Tup(EvalContext ctx, params ScriptValue[] xs) => ctx.Values.Tuple(xs);
        private static ListValue Lst(EvalContext ctx, params ScriptValue[] xs)
        {
            var l = ctx.Values.List(xs.Length);
            foreach (var x in xs)
                l.Add(x, ctx);
            return l;
        }
        private static bool Eq(EvalContext ctx, ScriptValue a, ScriptValue b) => ((BoolValue)PyOps.RichCompare(PyCmpOp.Eq, a, b, ctx)).Value;
        private static bool Cmp(EvalContext ctx, PyCmpOp op, ScriptValue a, ScriptValue b) => ((BoolValue)PyOps.RichCompare(op, a, b, ctx)).Value;

        [Fact] public void EmptyTupleHash() { var ctx = Rt.Ctx(); Assert.Equal(3527539L, Tup(ctx).PyHash(ctx)); }
        [Fact] public void SingletonTupleHash() { var ctx = Rt.Ctx(); Assert.Equal(3430019387558L, Tup(ctx, ctx.Values.Int(1)).PyHash(ctx)); }

        [Fact]
        public void TripleTupleHash()
        {
            var ctx = Rt.Ctx();
            Assert.Equal(2528502973977326415L, Tup(ctx, ctx.Values.Int(1), ctx.Values.Int(2), ctx.Values.Int(3)).PyHash(ctx));
        }

        [Fact]
        public void NestedTupleHashDeterministic()
        {
            var ctx = Rt.Ctx();
            var t = Tup(ctx, Tup(ctx, ctx.Values.Int(1), ctx.Values.Int(2)), Tup(ctx, ctx.Values.Int(3), ctx.Values.Int(4)));
            Assert.Equal(t.PyHash(ctx), t.PyHash(ctx));
        }

        [Fact]
        public void TupleWithListUnhashable()
        {
            var ctx = Rt.Ctx();
            var t = Tup(ctx, ctx.Values.Int(1), Lst(ctx, ctx.Values.Int(2)));
            var ex = Assert.Throws<ScriptException>(() => t.PyHash(ctx));
            Assert.Equal("unhashable type: 'list'", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void TupleMatryoshkaHitsDepthLimit()
        {
            var ctx = Rt.Ctx();
            ScriptValue t = Tup(ctx, ctx.Values.Int(0));
            for (int i = 0; i < 200; i++)
                t = Tup(ctx, t);
            var ex = Assert.Throws<ScriptException>(() => t.PyHash(ctx));
            Assert.Equal("maximum recursion depth exceeded in comparison", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void NestedTupleEquality()
        {
            var ctx = Rt.Ctx();
            var a = Tup(ctx, ctx.Values.Int(1), Tup(ctx, ctx.Values.Int(2), ctx.Values.Int(3)));
            var b = Tup(ctx, ctx.Values.Int(1), Tup(ctx, ctx.Values.Int(2), ctx.Values.Int(3)));
            Assert.True(Eq(ctx, a, b));
        }

        [Fact]
        public void ListVsTupleNeverEqual()
        {
            var ctx = Rt.Ctx();
            Assert.False(Eq(ctx, Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(2)), Tup(ctx, ctx.Values.Int(1), ctx.Values.Int(2))));
        }

        [Fact]
        public void LexicographicOrder()
        {
            var ctx = Rt.Ctx();
            Assert.True(Cmp(ctx, PyCmpOp.Lt, Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(2)), Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(2), ctx.Values.Int(3))));
            Assert.True(Cmp(ctx, PyCmpOp.Lt, Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(2), ctx.Values.Int(4)), Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(3))));
        }

        [Fact]
        public void ListTupleUnorderable()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => PyOps.RichCompare(PyCmpOp.Lt, Lst(ctx, ctx.Values.Int(1)), Tup(ctx, ctx.Values.Int(1)), ctx));
            Assert.Equal("'<' not supported between instances of 'list' and 'tuple'", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void NanInsideList()
        {
            var ctx = Rt.Ctx();
            Assert.False(Cmp(ctx, PyCmpOp.Lt, Lst(ctx, ctx.Values.Float(double.NaN)), Lst(ctx, ctx.Values.Int(1))));
            Assert.False(Cmp(ctx, PyCmpOp.Lt, Lst(ctx, ctx.Values.Int(1)), Lst(ctx, ctx.Values.Float(double.NaN))));
        }

        [Fact]
        public void DeepNestingHitsDepthLimit()
        {
            var ctx = Rt.Ctx();
            ScriptValue a = Lst(ctx, ctx.Values.Int(0));
            ScriptValue b = Lst(ctx, ctx.Values.Int(0));
            for (int i = 0; i < 130; i++)
            {
                a = Lst(ctx, a);
                b = Lst(ctx, b);
            }
            var ex = Assert.Throws<ScriptException>(() => PyOps.Equals(a, b, ctx, 0));
            Assert.Equal("maximum recursion depth exceeded in comparison", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void CyclicComparisonTerminates()
        {
            var ctx = Rt.Ctx();
            var a = ctx.Values.List(0); a.Add(a, ctx);
            var b = ctx.Values.List(0); b.Add(b, ctx);
            Assert.Throws<ScriptException>(() => PyOps.Equals(a, b, ctx, 0));
        }
    }
}
