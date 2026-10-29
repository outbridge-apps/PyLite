using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class StrValueTests
    {
        private static long Len(EvalContext ctx, string s)
        {
            long n;
            ctx.Values.Str(s).TryLengthCore(out n);
            return n;
        }

        private static string Item(EvalContext ctx, string s, ScriptValue idx) => ((StrValue)ctx.Values.Str(s).GetItem(idx, ctx)).Value;
        private static string ErrText(EvalContext ctx, System.Func<ScriptValue> f, string expectedType)
        {
            var ex = Assert.Throws<ScriptException>(() => f());
            Assert.Equal(expectedType, ex.Value.ExcType.Name);
            return ex.Value.GetMessageText(ctx);
        }

        [Fact] public void LenCountsCodeUnits() { var ctx = Rt.Ctx(); Assert.Equal(2, Len(ctx, "𝄞")); }

        [Fact]
        public void Indexing()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("c", Item(ctx, "abc", ctx.Values.Int(-1)));
            Assert.Equal("string index out of range", ErrText(ctx, () => ctx.Values.Str("abc").GetItem(ctx.Values.Int(3), ctx), "IndexError"));
        }

        [Fact]
        public void NonIntIndex()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("string indices must be integers", ErrText(ctx, () => ctx.Values.Str("abc").GetItem(ctx.Values.Float(1.0), ctx), "TypeError"));
        }

        [Fact]
        public void Slicing()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("bd", Item(ctx, "abcdef", ctx.Values.Slice(ctx.Values.Int(1), ctx.Values.Int(5), ctx.Values.Int(2))));
            Assert.Equal("cba", Item(ctx, "abc", ctx.Values.Slice(ctx.Values.None, ctx.Values.None, ctx.Values.Int(-1))));
        }

        [Fact]
        public void ConcatAndCharge()
        {
            var ctx = Rt.Ctx();
            var a = ctx.Values.Str("ab");
            var b = ctx.Values.Str("cd");
            long before = ctx.Budget.AllocRemaining;
            var r = PyOps.BinaryOp(PyBinOp.Add, a, b, ctx);
            Assert.Equal(32, before - ctx.Budget.AllocRemaining);
            Assert.Equal("abcd", ((StrValue)r).Value);
        }

        [Fact]
        public void AsymmetricConcatError()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("can only concatenate str (not \"int\") to str",
                ErrText(ctx, () => PyOps.BinaryOp(PyBinOp.Add, ctx.Values.Str("a"), ctx.Values.Int(1), ctx), "TypeError"));
            Assert.Equal("unsupported operand type(s) for +: 'int' and 'str'",
                ErrText(ctx, () => PyOps.BinaryOp(PyBinOp.Add, ctx.Values.Int(1), ctx.Values.Str("a"), ctx), "TypeError"));
        }

        [Fact]
        public void Repeat()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("ababab", ((StrValue)PyOps.BinaryOp(PyBinOp.Mul, ctx.Values.Str("ab"), ctx.Values.Int(3), ctx)).Value);
            Assert.Equal("", ((StrValue)PyOps.BinaryOp(PyBinOp.Mul, ctx.Values.Str("ab"), ctx.Values.Int(-2), ctx)).Value);
            Assert.Equal("ab", ((StrValue)PyOps.BinaryOp(PyBinOp.Mul, ctx.Values.Str("ab"), ctx.Values.Bool(true), ctx)).Value);
        }

        [Fact]
        public void RepeatFloatIsError()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("can't multiply sequence by non-int of type 'float'",
                ErrText(ctx, () => PyOps.BinaryOp(PyBinOp.Mul, ctx.Values.Str("a"), ctx.Values.Float(1.0), ctx), "TypeError"));
        }

        [Fact]
        public void RepeatOverCapAborts()
        {
            var ctx = Rt.Ctx();
            Assert.Throws<EngineAbort>(() => PyOps.BinaryOp(PyBinOp.Mul, ctx.Values.Str("ab"), ctx.Values.Int(BigInteger.Pow(10, 7)), ctx));
        }

        [Fact]
        public void Contains()
        {
            var ctx = Rt.Ctx();
            Assert.True(PyOps.Contains(ctx.Values.Str("b"), ctx.Values.Str("abc"), ctx));
            Assert.True(PyOps.Contains(ctx.Values.Str(""), ctx.Values.Str("abc"), ctx));
            Assert.Equal("'in <string>' requires string as left operand, not int",
                ErrText(ctx, () => { PyOps.Contains(ctx.Values.Int(1), ctx.Values.Str("abc"), ctx); return null; }, "TypeError"));
        }

        [Fact]
        public void Iteration()
        {
            var ctx = Rt.Ctx();
            var it = ctx.Values.Str("ab").GetIteratorCore(ctx);
            var got = new List<string>();
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
                got.Add(((StrValue)v).Value);
            Assert.Equal(new[] { "a", "b" }, got);
        }

        [Fact]
        public void UnpairedSurrogateIndex()
        {
            var ctx = Rt.Ctx();
            var ch = (StrValue)ctx.Values.Str("\uD834").GetItem(ctx.Values.Int(0), ctx);
            Assert.Equal(1, ch.Value.Length);
        }

        [Fact]
        public void EqualDistinctInstances()
        {
            var ctx = Rt.Ctx();
            Assert.True(((BoolValue)PyOps.RichCompare(PyCmpOp.Eq, ctx.Values.Str("abc"), ctx.Values.Str("abc"), ctx)).Value);
        }
    }
}
