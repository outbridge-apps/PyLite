using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Tests;
using Xunit;

namespace Outbridge.PyLite.Tests.Builtins
{
    // impl/06 T-BLT-02: ArgSpec binder + PyUnicode tables + Coerce helpers.
    [Trait("Category", "Compatibility")]
    public class ArgSpecTests
    {
        private static KwArgs Kw(string name, ScriptValue value)
        {
            return KwArgs.FromArrays(new[] { name }, new[] { value });
        }

        private static string Bind(EvalContext ctx, ArgSpec spec, ScriptValue[] args, KwArgs kw,
            out ScriptValue[] slots, out ScriptValue[] star)
        {
            slots = new ScriptValue[spec.SlotCount];
            ScriptValue[] s = slots;
            ScriptValue[] st = null;
            var ex = Assert.Throws<ScriptException>(() => spec.Bind(ctx, args, kw, s, out st));
            star = st;
            return ex.Value.GetMessageText(ctx);
        }

        // Every builtin's arity text comes from Args, in CPython's C-API families: METH_NOARGS / METH_O
        // ("takes no arguments", "takes exactly one argument"), _PyArg_CheckPositional for any other
        // positional-only range ("f expected at least N arguments, got M", naming the bound crossed), and
        // PyArg_ParseTuple for the ArgSpec binders ("takes at least N argument(s) (M given)"), plus
        // "takes no keyword arguments".
        [Theory]
        [InlineData("[].append()", "append() takes exactly one argument (0 given)")]
        [InlineData("[].insert(1)", "insert expected 2 arguments, got 1")]
        [InlineData("next()", "next expected at least 1 argument, got 0")]           // said "at most 2" (D9)
        [InlineData("next(iter([]), 1, 2)", "next expected at most 2 arguments, got 3")]
        [InlineData("min()", "min expected at least 1 argument, got 0")]
        [InlineData("{}.get()", "get expected at least 1 argument, got 0")]          // said "at most 2" (D9)
        [InlineData("from collections import deque\ndeque().index(1, 2, 3, 4)", "index expected at most 3 arguments, got 4")]   // said "at least 1" (D9)
        [InlineData("filter(None)", "filter expected 2 arguments, got 1")]
        [InlineData("hex()", "hex() takes exactly one argument (0 given)")]
        [InlineData("import math\nmath.comb(1)", "comb expected 2 arguments, got 1")]
        [InlineData("from collections import deque\ndeque().copy(1)", "copy() takes no arguments (1 given)")]
        [InlineData("import html\nhtml.escape()", "escape() takes at least 1 argument (0 given)")]
        [InlineData("import html\nhtml.escape(1, 2, 3)", "escape() takes at most 2 arguments (3 given)")]
        [InlineData("import itertools\nitertools.islice(1)", "islice() takes at least 2 arguments (1 given)")]
        [InlineData("import unicodedata\nunicodedata.name('a', x=1)", "name() takes no keyword arguments")]
        [InlineData("set().issubset()", "issubset() takes exactly one argument (0 given)")]
        [InlineData("sorted([], foo=1)", "'foo' is an invalid keyword argument for sorted()")]
        [InlineData("[].sort(foo=1)", "'foo' is an invalid keyword argument for sort()")]
        [InlineData("print(foo=1)", "'foo' is an invalid keyword argument for print()")]
        [InlineData("zip(strict=True, foo=1)", "zip() got an unexpected keyword argument 'foo'")]
        [InlineData("import json\njson.dumps(1, foo=1)", "'foo' is an invalid keyword argument for dumps()")]
        [InlineData("import csv, io\ncsv.reader(io.StringIO(''), foo=1)", "'foo' is an invalid keyword argument for this function")]
        [InlineData("from dataclasses import field\nfield(foo=1)", "field() got an unexpected keyword argument 'foo'")]
        [InlineData("import bisect\nbisect.bisect([], 1, foo=1)", "bisect() got an unexpected keyword argument 'foo'")]
        public void ArityTextsComeFromOnePlace(string call, string expected)
        {
            string[] lines = call.Split('\n');
            string src = string.Join("\n", lines, 0, lines.Length - 1) + "\ntry:\n    " + lines[lines.Length - 1]
                + "\n    r = 'no error'\nexcept TypeError as e:\n    r = str(e)\n";
            string quote = expected.Contains("'") ? "\"" : "'";   // repr picks the quote the text does not use
            Assert.Equal(quote + expected + quote, Outbridge.PyLite.Tests.Evaluator.Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void AbsTooFew()
        {
            var ctx = Rt.Ctx();
            var spec = ArgSpec.Create("abs").Req("x").Seal();
            ScriptValue[] slots, star;
            Assert.Equal("abs() takes exactly one argument (0 given)",
                Bind(ctx, spec, new ScriptValue[0], KwArgs.Empty, out slots, out star));
        }

        [Fact]
        public void AbsTooMany()
        {
            var ctx = Rt.Ctx();
            var spec = ArgSpec.Create("abs").Req("x").Seal();
            ScriptValue[] slots, star;
            Assert.Equal("abs() takes exactly one argument (2 given)",
                Bind(ctx, spec, new ScriptValue[] { ctx.Values.Int(1), ctx.Values.Int(2) }, KwArgs.Empty, out slots, out star));
        }

        [Fact]
        public void RoundOptionalDefaultsMissing()
        {
            var ctx = Rt.Ctx();
            var spec = ArgSpec.Create("round").Req("number").Opt("ndigits", ArgSpec.MISSING).Seal();
            var slots = new ScriptValue[spec.SlotCount];
            ScriptValue[] star;
            var x = ctx.Values.Int(5);
            spec.Bind(ctx, new ScriptValue[] { x }, KwArgs.Empty, slots, out star);
            Assert.Same(x, slots[0]);
            Assert.Same(ArgSpec.MISSING, slots[1]);
            Assert.Empty(star);
        }

        [Fact]
        public void PrintStarAndKwOnly()
        {
            var ctx = Rt.Ctx();
            var spec = ArgSpec.Create("print").Star("args")
                .KwOnly("sep", ArgSpec.MISSING).KwOnly("end", ArgSpec.MISSING)
                .KwOnly("file", ArgSpec.MISSING).KwOnly("flush", ArgSpec.MISSING).Seal();
            var a = ctx.Values.Int(1); var b = ctx.Values.Int(2); var comma = ctx.Values.Str(",");
            var slots = new ScriptValue[spec.SlotCount];
            ScriptValue[] star;
            spec.Bind(ctx, new ScriptValue[] { a, b }, Kw("sep", comma), slots, out star);
            Assert.Equal(new ScriptValue[] { a, b }, star);
            Assert.Same(comma, slots[0]);   // sep
            Assert.Same(ArgSpec.MISSING, slots[1]);   // end
        }

        [Fact]
        public void InvalidKeyword()
        {
            var ctx = Rt.Ctx();
            var spec = ArgSpec.Create("f").Req("x").Seal();
            ScriptValue[] slots, star;
            Assert.Equal("'bogus' is an invalid keyword argument for this function",
                Bind(ctx, spec, new ScriptValue[] { ctx.Values.Int(1) }, Kw("bogus", ctx.Values.Int(1)), out slots, out star));
        }

        [Fact]
        public void NoKwargsRejected()
        {
            var ctx = Rt.Ctx();
            var spec = ArgSpec.Create("len").Req("obj").NoKwargs().Seal();
            ScriptValue[] slots, star;
            Assert.Equal("len() takes no keyword arguments",
                Bind(ctx, spec, new ScriptValue[] { ctx.Values.Int(1) }, Kw("x", ctx.Values.Int(1)), out slots, out star));
        }

        [Fact]
        public void MultipleValues()
        {
            var ctx = Rt.Ctx();
            var spec = ArgSpec.Create("f").Req("x").Seal();
            ScriptValue[] slots, star;
            Assert.Equal("f() got multiple values for argument 'x'",
                Bind(ctx, spec, new ScriptValue[] { ctx.Values.Int(1) }, Kw("x", ctx.Values.Int(2)), out slots, out star));
        }

        // ---- PyUnicode: code points as hex ints so the source stays ASCII ----

        [Theory]
        [InlineData(0x20, true)]      // space
        [InlineData(0x09, true)]      // tab
        [InlineData(0x1c, true)]      // FS: a Python space
        [InlineData(0x1f, true)]      // US: a Python space
        [InlineData(0x3000, true)]    // ideographic space
        [InlineData(0x00a0, true)]    // NBSP
        [InlineData(0x180e, false)]   // Mongolian vowel separator: NOT a Python space (unlike char.IsWhiteSpace)
        [InlineData(0x61, false)]     // 'a'
        public void IsPythonSpace(int cp, bool expected)
        {
            Assert.Equal(expected, PyUnicode.IsPythonSpace((char)cp));
        }

        [Theory]
        [InlineData(0x0a, true)]      // LF
        [InlineData(0x0d, true)]      // CR
        [InlineData(0x1c, true)]      // FS is a line break
        [InlineData(0x1f, false)]     // US is a space but NOT a line break
        [InlineData(0x2028, true)]    // LS line separator
        [InlineData(0x00a0, false)]   // NBSP: a space but NOT a line break
        public void IsLineBreak(int cp, bool expected)
        {
            Assert.Equal(expected, PyUnicode.IsLineBreak((char)cp));
        }

        // ---- Coerce ----

        [Fact]
        public void AdjustIndicesNoneNone()
        {
            var ctx = Rt.Ctx();
            int s, e;
            Coerce.AdjustIndices(ctx, ctx.Values.None, ctx.Values.None, 5, out s, out e);
            Assert.Equal(0, s);
            Assert.Equal(5, e);
        }

        [Fact]
        public void AdjustIndicesNegative()
        {
            var ctx = Rt.Ctx();
            int s, e;
            Coerce.AdjustIndices(ctx, ctx.Values.Int(-1), ctx.Values.Int(-10), 5, out s, out e);
            Assert.Equal(4, s);
            Assert.Equal(0, e);
        }

        [Fact]
        public void ToIndexRejectsFloat()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => Coerce.ToIndex(ctx, ctx.Values.Float(1.0), "x"));
            Assert.Equal("'float' object cannot be interpreted as an integer", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void ToIndexAcceptsBool()
        {
            var ctx = Rt.Ctx();
            Assert.Equal(BigInteger.One, Coerce.ToIndex(ctx, ctx.Values.Bool(true), "x"));
        }
    }
}
