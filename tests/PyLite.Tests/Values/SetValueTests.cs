using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class SetValueTests
    {
        private static SetValue Set(EvalContext ctx, params ScriptValue[] xs)
        {
            var s = ctx.Values.Set(xs.Length);
            foreach (var x in xs)
                s.AddItem(x, ctx);
            return s;
        }
        private static FrozenSetValue FSet(EvalContext ctx, params ScriptValue[] xs) => ctx.Values.FrozenSet(Set(ctx, xs).Table);
        private static ScriptValue Bin(EvalContext ctx, PyBinOp op, ScriptValue a, ScriptValue b) => PyOps.BinaryOp(op, a, b, ctx);
        private static bool Cmp(EvalContext ctx, PyCmpOp op, ScriptValue a, ScriptValue b) => ((BoolValue)PyOps.RichCompare(op, a, b, ctx)).Value;

        private static List<BigInteger> Members(EvalContext ctx, SetValue s)
        {
            var it = s.GetIteratorCore(ctx);
            var r = new List<BigInteger>();
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
                r.Add(((IntValue)v).Value);
            return r;
        }

        [Fact]
        public void UnionOrder()
        {
            var ctx = Rt.Ctx();
            var r = (SetValue)Bin(ctx, PyBinOp.BitOr, Set(ctx, ctx.Values.Int(1), ctx.Values.Int(2)), Set(ctx, ctx.Values.Int(2), ctx.Values.Int(3)));
            Assert.Equal("set", r.PyTypeName);
            Assert.Equal(new[] { (BigInteger)1, 2, 3 }, Members(ctx, r));
        }

        [Fact]
        public void IntersectDiffSymDiff()
        {
            var ctx = Rt.Ctx();
            var a = Set(ctx, ctx.Values.Int(1), ctx.Values.Int(2));
            Assert.Equal(new[] { (BigInteger)2 }, Members(ctx, (SetValue)Bin(ctx, PyBinOp.BitAnd, a, Set(ctx, ctx.Values.Int(2), ctx.Values.Int(3)))));
            Assert.Equal(new[] { (BigInteger)1 }, Members(ctx, (SetValue)Bin(ctx, PyBinOp.Sub, a, Set(ctx, ctx.Values.Int(2)))));
            Assert.Equal(new[] { (BigInteger)1, 3 }, Members(ctx, (SetValue)Bin(ctx, PyBinOp.BitXor, a, Set(ctx, ctx.Values.Int(2), ctx.Values.Int(3)))));
        }

        [Fact]
        public void OperatorRequiresSet()
        {
            var ctx = Rt.Ctx();
            var lst = ctx.Values.List(1); lst.Add(ctx.Values.Int(1), ctx);
            var ex = Assert.Throws<ScriptException>(() => Bin(ctx, PyBinOp.BitOr, Set(ctx, ctx.Values.Int(1)), lst));
            Assert.Equal("unsupported operand type(s) for |: 'set' and 'list'", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void FrozensetOperandTypeResult()
        {
            var ctx = Rt.Ctx();
            var r = (SetValue)Bin(ctx, PyBinOp.BitOr, FSet(ctx, ctx.Values.Int(1)), Set(ctx, ctx.Values.Int(2)));
            Assert.Equal("frozenset", r.PyTypeName);
            Assert.Equal(new[] { (BigInteger)1, 2 }, Members(ctx, r));
        }

        [Fact]
        public void NumericTowerCollapse()
        {
            var ctx = Rt.Ctx();
            var s = Set(ctx, ctx.Values.Int(1), ctx.Values.Float(1.0), ctx.Values.Bool(true));
            Assert.Equal(1, s.Count);
            Assert.Equal(new[] { (BigInteger)1 }, Members(ctx, s));
        }

        [Fact]
        public void SubsetComparisons()
        {
            var ctx = Rt.Ctx();
            var a = Set(ctx, ctx.Values.Int(1), ctx.Values.Int(2));
            var b = Set(ctx, ctx.Values.Int(1), ctx.Values.Int(2), ctx.Values.Int(3));
            Assert.True(Cmp(ctx, PyCmpOp.Le, a, b));
            Assert.False(Cmp(ctx, PyCmpOp.Lt, a, a));
            Assert.False(Cmp(ctx, PyCmpOp.Le, Set(ctx, ctx.Values.Int(1), ctx.Values.Int(4)), b));
        }

        [Fact]
        public void EmptySetEqualsEmptyFrozenset()
        {
            var ctx = Rt.Ctx();
            Assert.True(((BoolValue)PyOps.RichCompare(PyCmpOp.Eq, ctx.Values.Set(0), ctx.Values.FrozenSet(ctx.Values.Set(0).Table), ctx)).Value);
        }

        [Fact]
        public void AddDuringIterationAborts()
        {
            var ctx = Rt.Ctx();
            var s = Set(ctx, ctx.Values.Int(1), ctx.Values.Int(2));
            var it = s.GetIteratorCore(ctx);
            ScriptValue v;
            it.MoveNext(ctx, out v);
            s.AddItem(ctx.Values.Int(3), ctx);
            var ex = Assert.Throws<ScriptException>(() => it.MoveNext(ctx, out v));
            Assert.Equal("Set changed size during iteration", ex.Value.GetMessageText(ctx));
        }

        // CPython's set iterator checks the size only: a remove+add that keeps it goes on (in an
        // unspecified order), and the walk must survive the table's rebuild underneath it.
        [Fact]
        public void SameSizeReplaceDuringIterationContinues()
        {
            var ctx = Rt.Ctx();
            var s = Set(ctx, ctx.Values.Int(1), ctx.Values.Int(2), ctx.Values.Int(3));
            var it = s.GetIteratorCore(ctx);
            ScriptValue v;
            Assert.True(it.MoveNext(ctx, out v));
            Assert.True(s.RemoveItem(v, ctx));
            s.AddItem(ctx.Values.Int(99), ctx);
            int seen = 0;
            while (it.MoveNext(ctx, out v))
                seen++;
            Assert.True(seen >= 1 && seen <= 3);
        }

        [Fact]
        public void UnhashableMembershipProbe()
        {
            var ctx = Rt.Ctx();
            var lst = ctx.Values.List(1); lst.Add(ctx.Values.Int(1), ctx);
            var ex = Assert.Throws<ScriptException>(() => PyOps.Contains(lst, Set(ctx, ctx.Values.Int(1)), ctx));
            Assert.Equal("unhashable type: 'list'", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void TupleMembershipByValue()
        {
            var ctx = Rt.Ctx();
            var s = Set(ctx, ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(1), ctx.Values.Int(2) }));
            Assert.True(PyOps.Contains(ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(1), ctx.Values.Int(2) }), s, ctx));
        }

        [Fact]
        public void FrozensetHashOrderIndependent()
        {
            var ctx = Rt.Ctx();
            var a = FSet(ctx, ctx.Values.Int(1), ctx.Values.Int(2));
            var b = FSet(ctx, ctx.Values.Int(2), ctx.Values.Int(1));
            Assert.Equal(a.PyHash(ctx), b.PyHash(ctx));
        }

        [Fact]
        public void LargeInsertionOrder()
        {
            var ctx = Rt.Ctx();
            var s = ctx.Values.Set(0);
            for (int i = 0; i < 1000; i++)
                s.AddItem(ctx.Values.Int(i), ctx);
            var m = Members(ctx, s);
            for (int i = 0; i < 1000; i++)
                Assert.Equal((BigInteger)i, m[i]);
        }

        [Fact]
        public void PopFirstInsertionOrder()
        {
            var ctx = Rt.Ctx();
            var s = Set(ctx, ctx.Values.Int(3), ctx.Values.Int(1), ctx.Values.Int(2));
            Assert.Equal((BigInteger)3, ((IntValue)s.PopFirst(ctx)).Value);
            Assert.Equal((BigInteger)1, ((IntValue)s.PopFirst(ctx)).Value);
        }
    }
}
