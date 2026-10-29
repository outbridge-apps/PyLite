using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class ListTupleTests
    {
        private static ListValue Lst(EvalContext ctx, params ScriptValue[] xs)
        {
            var l = ctx.Values.List(xs.Length);
            foreach (var x in xs)
                l.Add(x, ctx);
            return l;
        }
        private static ScriptValue Bin(EvalContext ctx, PyBinOp op, ScriptValue a, ScriptValue b) => PyOps.BinaryOp(op, a, b, ctx);
        private static List<BigInteger> Ints(ListValue l)
        {
            var r = new List<BigInteger>();
            foreach (var v in l.Items)
                r.Add(((IntValue)v).Value);
            return r;
        }
        private static string Err(EvalContext ctx, System.Func<ScriptValue> f, string type)
        {
            var ex = Assert.Throws<ScriptException>(() => f());
            Assert.Equal(type, ex.Value.ExcType.Name);
            return ex.Value.GetMessageText(ctx);
        }

        [Fact]
        public void ListConcat()
        {
            var ctx = Rt.Ctx();
            Assert.Equal(new[] { (BigInteger)1, 2, 3 }, Ints((ListValue)Bin(ctx, PyBinOp.Add, Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(2)), Lst(ctx, ctx.Values.Int(3)))));
        }

        [Fact]
        public void MixedConcatErrors()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("can only concatenate list (not \"tuple\") to list",
                Err(ctx, () => Bin(ctx, PyBinOp.Add, Lst(ctx, ctx.Values.Int(1)), ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(2) })), "TypeError"));
            Assert.Equal("can only concatenate tuple (not \"list\") to tuple",
                Err(ctx, () => Bin(ctx, PyBinOp.Add, ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(1) }), Lst(ctx, ctx.Values.Int(2))), "TypeError"));
        }

        [Fact]
        public void ListRepeat()
        {
            var ctx = Rt.Ctx();
            Assert.Equal(new[] { (BigInteger)1, 2, 1, 2 }, Ints((ListValue)Bin(ctx, PyBinOp.Mul, Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(2)), ctx.Values.Int(2))));
            Assert.Empty(((ListValue)Bin(ctx, PyBinOp.Mul, Lst(ctx, ctx.Values.Int(1)), ctx.Values.Int(0))).Items);
            Assert.Equal(new[] { (BigInteger)1 }, Ints((ListValue)Bin(ctx, PyBinOp.Mul, Lst(ctx, ctx.Values.Int(1)), ctx.Values.Bool(true))));
        }

        [Fact]
        public void RepeatByFloatIsError()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("can't multiply sequence by non-int of type 'float'",
                Err(ctx, () => Bin(ctx, PyBinOp.Mul, Lst(ctx, ctx.Values.Int(1)), ctx.Values.Float(1.0)), "TypeError"));
        }

        [Fact]
        public void SpliceStep1()
        {
            var ctx = Rt.Ctx();
            var l = Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(2), ctx.Values.Int(3), ctx.Values.Int(4));
            l.SetItem(ctx.Values.Slice(ctx.Values.Int(1), ctx.Values.Int(3), ctx.Values.None), Lst(ctx, ctx.Values.Int(9)), ctx);
            Assert.Equal(new[] { (BigInteger)1, 9, 4 }, Ints(l));
        }

        [Fact]
        public void ExtendedSliceAssign()
        {
            var ctx = Rt.Ctx();
            var l = Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(2), ctx.Values.Int(3), ctx.Values.Int(4));
            l.SetItem(ctx.Values.Slice(ctx.Values.None, ctx.Values.None, ctx.Values.Int(2)), Lst(ctx, ctx.Values.Int(7), ctx.Values.Int(8)), ctx);
            Assert.Equal(new[] { (BigInteger)7, 2, 8, 4 }, Ints(l));
        }

        [Fact]
        public void ExtendedSliceSizeMismatch()
        {
            var ctx = Rt.Ctx();
            var l = Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(2), ctx.Values.Int(3), ctx.Values.Int(4));
            Assert.Equal("attempt to assign sequence of size 1 to extended slice of size 2",
                Err(ctx, () => { l.SetItem(ctx.Values.Slice(ctx.Values.None, ctx.Values.None, ctx.Values.Int(2)), Lst(ctx, ctx.Values.Int(7)), ctx); return null; }, "ValueError"));
        }

        [Fact]
        public void DeleteExtendedSlice()
        {
            var ctx = Rt.Ctx();
            var l = Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(2), ctx.Values.Int(3), ctx.Values.Int(4), ctx.Values.Int(5));
            l.DelItem(ctx.Values.Slice(ctx.Values.None, ctx.Values.None, ctx.Values.Int(2)), ctx);
            Assert.Equal(new[] { (BigInteger)2, 4 }, Ints(l));
        }

        [Fact]
        public void InPlaceConcatIterable()
        {
            var ctx = Rt.Ctx();
            var l = Lst(ctx, ctx.Values.Int(1));
            l.InPlaceConcat(ctx.Values.Str("ab"), ctx);
            Assert.Equal(3, l.Items.Count);
            Assert.Equal("a", ((StrValue)l.Items[1]).Value);
            Assert.Equal("b", ((StrValue)l.Items[2]).Value);
        }

        [Fact]
        public void SelfInPlaceConcat()
        {
            var ctx = Rt.Ctx();
            var l = Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(2));
            l.InPlaceConcat(l, ctx);
            Assert.Equal(new[] { (BigInteger)1, 2, 1, 2 }, Ints(l));
        }

        [Fact]
        public void Indexing()
        {
            var ctx = Rt.Ctx();
            var l = Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(2), ctx.Values.Int(3));
            Assert.Equal((BigInteger)3, ((IntValue)l.GetItem(ctx.Values.Int(-1), ctx)).Value);
            Assert.Equal("list index out of range", Err(ctx, () => l.GetItem(ctx.Values.Int(3), ctx), "IndexError"));
            Assert.Equal("list indices must be integers, not str", Err(ctx, () => l.GetItem(ctx.Values.Str("a"), ctx), "TypeError"));
        }

        [Fact]
        public void CollectionCapAborts()
        {
            var ctx = Rt.Ctx();
            Assert.Throws<EngineAbort>(() => Bin(ctx, PyBinOp.Mul, Lst(ctx, ctx.Values.Int(0)), ctx.Values.Int(5_000_001)));
        }

        [Fact]
        public void Membership()
        {
            var ctx = Rt.Ctx();
            Assert.True(PyOps.Contains(ctx.Values.Int(2), Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(2)), ctx));
            Assert.True(PyOps.Contains(ctx.Values.Float(2.0), Lst(ctx, ctx.Values.Int(1), ctx.Values.Int(2)), ctx));
            Assert.True(PyOps.Contains(Lst(ctx), Lst(ctx, Lst(ctx)), ctx));
        }

        // list, tuple and struct_time read through one SequenceOps: the index texts, the small-int fast
        // path that still unifies 1/True/1.0, slicing, and the repeat/concat arithmetic are the same code.
        [Theory]
        [InlineData("[1, 2][5]", "IndexError('list index out of range')")]
        [InlineData("(1, 2)[-3]", "IndexError('tuple index out of range')")]
        [InlineData("time.gmtime(0)[9]", "IndexError('tuple index out of range')")]
        [InlineData("[1]['a']", "TypeError('list indices must be integers, not str')")]
        [InlineData("time.gmtime(0)[1.5]", "TypeError('tuple indices must be integers, not float')")]
        [InlineData("(1.0 in [1, 2], True in (1, 2), 1 in [1.5, 1.0], 3 in time.gmtime(0))", "(True, True, True, True)")]
        [InlineData("([1, 2, 1].count(1.0), (1, True, 1.0).count(1), [1, 2].index(True))", "(2, 3, 0)")]
        [InlineData("(lambda l: (l.remove(True), l)[1])([1, 2])", "[2]")]
        [InlineData("(time.gmtime(0)[:2], time.gmtime(0)[::4], [0, 1, 2, 3][::-2], (0, 1, 2)[3:])", "((1970, 1), (1970, 0, 0), [3, 1], ())")]
        [InlineData("([1] * -1, (1,) * 0, [1, 2] * True, (1, 2) * 2)", "([], (), [1, 2], (1, 2, 1, 2))")]
        [InlineData("[1] * 2.0", "TypeError(\"can't multiply sequence by non-int of type 'float'\")")]
        [InlineData("(1,) + [1]", "TypeError('can only concatenate tuple (not \"list\") to tuple')")]
        [InlineData("[1] + (1,)", "TypeError('can only concatenate list (not \"tuple\") to list')")]
        public void SequenceProtocolIsShared(string expr, string expected)
        {
            string src = "import time\ntry:\n    r = " + expr + "\nexcept Exception as e:\n    r = e\n";
            Assert.Equal(expected, Outbridge.PyLite.Tests.Evaluator.Ev.Run(src).Repr("r"));
        }

        // An extended-slice delete is one compaction pass (a RemoveAt per element was quadratic and
        // uncharged, B15), checked against the slice's own indices for every start/stop/step shape.
        [Fact]
        public void ExtendedSliceDeleteMatchesTheIndices()
        {
            string src = "ok = True\nfor n in (0, 1, 2, 7, 23):\n"
                + "    for st in (None, 0, 3, -1, -3, n, -n - 2):\n        for sp in (None, 0, n // 2, n + 5, -2):\n"
                + "            for step in (1, 2, 3, -1, -2, -4, 7):\n"
                + "                a = list(range(n))\n                gone = set(range(n)[st:sp:step])\n"
                + "                del a[st:sp:step]\n                ok = ok and a == [x for x in range(n) if x not in gone]\n"
                + "big = list(range(200000))\ndel big[::2]\nr = (ok, len(big), big[:3])\n";
            var r = Outbridge.PyLite.Tests.Evaluator.Ev.Run(src, Outbridge.PyLite.Hosting.ResourceLimits.Default.With(deadlineMs: 10000));
            Assert.Equal("(True, 100000, [1, 3, 5])", r.Repr("r"));
        }
    }
}
