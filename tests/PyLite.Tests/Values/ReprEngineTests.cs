using System.Numerics;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class ReprEngineTests
    {
        private static ListValue Lst(EvalContext ctx, params ScriptValue[] xs)
        {
            var l = ctx.Values.List(xs.Length);
            foreach (var x in xs)
                l.Add(x, ctx);
            return l;
        }
        private static string Repr(EvalContext ctx, ScriptValue v) => v.Repr(ctx);

        [Fact]
        public void ListRepr()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("[1, 'a', 2.0]", Repr(ctx, Lst(ctx, ctx.Values.Int(1), ctx.Values.Str("a"), ctx.Values.Float(2.0))));
        }

        [Fact]
        public void TupleRepr()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("(1,)", Repr(ctx, ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(1) })));
            Assert.Equal("()", Repr(ctx, ctx.Values.Tuple(new ScriptValue[0])));
            Assert.Equal("(1, (2, 3))", Repr(ctx, ctx.Values.Tuple(new ScriptValue[]
            {
                ctx.Values.Int(1),
                ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(2), ctx.Values.Int(3) })
            })));
        }

        [Fact]
        public void DictRepr()
        {
            var ctx = Rt.Ctx();
            var d = ctx.Values.Dict(0);
            d.SetItem(ctx.Values.Str("a"), ctx.Values.Int(1), ctx);
            d.SetItem(ctx.Values.Str("b"), Lst(ctx, ctx.Values.Int(2)), ctx);
            Assert.Equal("{'a': 1, 'b': [2]}", Repr(ctx, d));
        }

        [Fact]
        public void SetAndFrozensetRepr()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("set()", Repr(ctx, ctx.Values.Set(0)));
            var s = ctx.Values.Set(0); s.AddItem(ctx.Values.Int(1), ctx);
            Assert.Equal("{1}", Repr(ctx, s));
            var fs = ctx.Values.FrozenSet(s.Table);
            Assert.Equal("frozenset({1})", Repr(ctx, fs));
        }

        [Fact]
        public void SelfReference()
        {
            var ctx = Rt.Ctx();
            var l = ctx.Values.List(0); l.Add(l, ctx);
            Assert.Equal("[[...]]", Repr(ctx, l));

            var d = ctx.Values.Dict(0); d.SetItem(ctx.Values.Str("k"), d, ctx);
            Assert.Equal("{'k': {...}}", Repr(ctx, d));
        }

        [Fact]
        public void StrVsRepr()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("ab", ctx.Values.Str("ab").Str(ctx));
            Assert.Equal("'ab'", ctx.Values.Str("ab").Repr(ctx));
            Assert.Equal("['a']", Lst(ctx, ctx.Values.Str("a")).Str(ctx));   // container str = repr
        }

        [Fact]
        public void QuoteSelection()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("\"a'b\"", ctx.Values.Str("a'b").Repr(ctx));
            Assert.Equal("'a\\'b\"c'", ctx.Values.Str("a'b\"c").Repr(ctx));
        }

        [Fact]
        public void EscapeSequences()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("'\\n\\t\\\\'", ctx.Values.Str("\n\t\\").Repr(ctx));
            Assert.Equal("'\\x7f'", ctx.Values.Str("\x7f").Repr(ctx));
            Assert.Equal("'\\x1b'", ctx.Values.Str("\x1b").Repr(ctx));
        }

        [Fact]
        public void PrintableUnicode()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("'é𝄞'", ctx.Values.Str("é𝄞").Repr(ctx));
            Assert.Equal("' '", ctx.Values.Str(" ").Repr(ctx));
            Assert.Equal("'\\ud834'", ctx.Values.Str("\uD834").Repr(ctx));
        }

        [Fact]
        public void DictKeysAndItemsRepr()
        {
            var ctx = Rt.Ctx();
            var d = ctx.Values.Dict(0);
            d.SetItem(ctx.Values.Str("a"), ctx.Values.Int(1), ctx);
            d.SetItem(ctx.Values.Str("b"), ctx.Values.Int(2), ctx);
            Assert.Equal("dict_keys(['a', 'b'])", Repr(ctx, d.KeysView(ctx)));
            var d1 = ctx.Values.Dict(0);
            d1.SetItem(ctx.Values.Str("a"), ctx.Values.Int(1), ctx);
            Assert.Equal("dict_items([('a', 1)])", Repr(ctx, d1.ItemsView(ctx)));
        }

        [Fact]
        public void KeyErrorTupleRepr()
        {
            var ctx = Rt.Ctx();
            var d = ctx.Values.Dict(0);
            var ex = Assert.Throws<ScriptException>(() =>
                d.GetItemOrThrow(ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(1), ctx.Values.Int(2) }), ctx));
            Assert.Equal("(1, 2)", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void DagBombAborts()
        {
            var ctx = Rt.Ctx(ResourceLimits.Default.With(maxAllocBytes: 4_000_000));
            ScriptValue x = Lst(ctx, ctx.Values.Int(0));
            for (int i = 0; i < 60; i++)
                x = Lst(ctx, x, x);
            Assert.Throws<EngineAbort>(() => x.Repr(ctx));
        }

        [Fact]
        public void DeepNestingNoStackOverflow()
        {
            var ctx = Rt.Ctx();
            ScriptValue x = Lst(ctx, ctx.Values.Int(0));
            for (int i = 0; i < 100_000; i++)
            {
                var o = ctx.Values.List(1);
                o.Add(x, ctx);
                x = o;
            }
            string s = x.Repr(ctx);
            Assert.StartsWith("[[", s);
        }

        [Fact]
        public void BigIntReprPreCharge()
        {
            var ctx = Rt.Ctx();
            var big = ctx.Values.Int(BigInteger.Pow(10, 300000));
            Assert.Throws<EngineAbort>(() => big.Repr(ctx));
        }
    }
}
