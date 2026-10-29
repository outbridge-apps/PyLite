using System.Collections.Generic;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Tests.Runtime;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // The host contract in isolation (impl/07 R-3..R-7, R-17): what a scalar string becomes, which names
    // a request may use, and how declared outputs come back -- without running a script for each row.
    [Trait("Category", "Sandbox")]
    public class RunIOTests
    {
        [Theory]
        [InlineData(null, "None", "NoneType")]
        [InlineData("", "''", "str")]
        [InlineData("42", "42", "int")]
        [InlineData("-7", "-7", "int")]
        [InlineData("+5", "5", "int")]
        [InlineData("007", "7", "int")]
        [InlineData("1e3", "1000.0", "float")]
        [InlineData("2.50", "2.5", "float")]
        [InlineData(".5", "0.5", "float")]
        [InlineData("inf", "'inf'", "str")]
        [InlineData("nan", "'nan'", "str")]
        [InlineData("1_000", "'1_000'", "str")]
        [InlineData("0x10", "'0x10'", "str")]
        [InlineData("true", "True", "bool")]
        [InlineData("FALSE", "False", "bool")]
        [InlineData("None", "'None'", "str")]
        [InlineData(" 1", "' 1'", "str")]
        [InlineData("abc", "'abc'", "str")]
        public void AScalarInputIsTypedByItsText(string raw, string repr, string type)
        {
            var ctx = Rt.Ctx();
            ScriptValue v = RunIO.CoerceScalar(raw, ctx);
            Assert.Equal(repr, v.Repr(ctx));
            Assert.Equal(type, v.PyTypeName);
        }

        [Fact]
        public void ADigitStringPastTheLiteralGuardStaysAString()
        {
            var ctx = Rt.Ctx();
            string atLimit = new string('7', RunIO.MaxNumericLiteralDigits);
            Assert.Equal("int", RunIO.CoerceScalar(atLimit, ctx).PyTypeName);
            Assert.Equal("int", RunIO.CoerceScalar("-" + atLimit, ctx).PyTypeName);
            Assert.Equal("int", RunIO.CoerceScalar(atLimit + "7", ctx).PyTypeName);   // the +1 is a sign slot, not checked as one
            Assert.Equal("str", RunIO.CoerceScalar(atLimit + "77", ctx).PyTypeName);
        }

        [Theory]
        [InlineData("a", true)]
        [InlineData("_x1", true)]
        [InlineData("print", true)]     // a builtin name may be shadowed by a global
        [InlineData("Result", true)]
        [InlineData("", false)]
        [InlineData("1a", false)]
        [InlineData("a-b", false)]
        [InlineData("a b", false)]
        [InlineData("é", false)]   // ASCII only
        [InlineData("if", false)]       // reserved words
        [InlineData("None", false)]
        [InlineData("match", true)]     // soft keyword: a legal name
        public void AVariableNameIsAnAsciiIdentifierThatIsNotReserved(string name, bool ok)
        {
            Assert.Equal(ok, RunIO.IsIdentifier(name));
        }

        [Fact]
        public void ValidateNamesReportsTheFirstOffender()
        {
            string error;
            var req = new RunRequest
            {
                ScalarsIn = new Dictionary<string, string> { { "a", "1" } },
                JsonIn = new Dictionary<string, string> { { "b", "[]" } },
                ScalarOutputNames = new[] { "ok", "1bad" },
            };
            Assert.False(RunIO.ValidateNames(req, out error));
            Assert.Equal("invalid variable name '1bad'", error);

            req.ScalarOutputNames = null;
            req.JsonIn = new Dictionary<string, string> { { "a", "[]" } };
            Assert.False(RunIO.ValidateNames(req, out error));
            Assert.Equal("input variable 'a' is defined in both ScalarsIn and JsonIn", error);

            req.JsonIn = null;
            Assert.True(RunIO.ValidateNames(req, out error));
            Assert.Null(error);
        }

        [Fact]
        public void AJsonInputThatDoesNotParseIsAHostMarshalFailure()
        {
            var ctx = Rt.Ctx();
            var env = Outbridge.PyLite.Runtime.Environment.CreateModule(ctx, ctx.Values.Dict(4), HostFunctionTable.CreateDefault(), null);
            var req = new RunRequest { JsonIn = new Dictionary<string, string> { { "j", "{\"a\": [1, 2" } } };
            var ex = Assert.Throws<HostMarshalFailure>(() => RunIO.PopulateJsonInputs(env, req, ctx));
            Assert.StartsWith("invalid JSON in input variable 'j': ", ex.Message);
        }

        [Fact]
        public void OutputsAreStrOfTheValueDedupedAndAbsentWhenUnassigned()
        {
            var ctx = Rt.Ctx();
            DictValue globals = ctx.Values.Dict(4);
            globals.SetItem(ctx.Values.Str("n"), ctx.Values.Int(7), ctx);
            globals.SetItem(ctx.Values.Str("s"), ctx.Values.Str("x y"), ctx);
            globals.SetItem(ctx.Values.Str("e"), ctx.Values.Str(""), ctx);
            IReadOnlyDictionary<string, string> r = RunIO.CollectScalarOutputs(globals, new[] { "n", "s", "n", "e", "missing" }, ctx);
            Assert.Equal(3, r.Count);
            Assert.Equal("7", r["n"]);
            Assert.Equal("x y", r["s"]);
            Assert.Equal("", r["e"]);   // "" stays "" -- the adapter's ""->None->"None" bug lived above this line
            Assert.False(r.ContainsKey("missing"));
            Assert.Same(RunIO.EmptyStringMap, RunIO.CollectScalarOutputs(globals, null, ctx));
        }

        [Fact]
        public void AScalarOutputThatIsNotDataIsRefusedByName()
        {
            var ctx = Rt.Ctx();
            DictValue globals = ctx.Values.Dict(4);
            globals.SetItem(ctx.Values.Str("f"), ctx.Values.Slice(ctx.Values.None, ctx.Values.None, ctx.Values.None), ctx);
            var ex = Assert.Throws<HostMarshalFailure>(() => RunIO.CollectScalarOutputs(globals, new[] { "f" }, ctx));
            Assert.Equal("output variable 'f' is not serializable", ex.Message);
        }

        [Fact]
        public void AJsonOutputIsCompactAndRefusesWhatJsonCannotCarry()
        {
            var ctx = Rt.Ctx();
            DictValue globals = ctx.Values.Dict(4);
            DictValue d = ctx.Values.Dict(2);
            d.SetItem(ctx.Values.Str("k"), ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(1), ctx.Values.Str("é") }), ctx);
            globals.SetItem(ctx.Values.Str("d"), d, ctx);
            globals.SetItem(ctx.Values.Str("bad"), ctx.Values.Float(double.NaN), ctx);
            Assert.Equal("{\"k\":[1,\"é\"]}", RunIO.CollectJsonOutputs(globals, new[] { "d" }, ctx)["d"]);
            var ex = Assert.Throws<HostMarshalFailure>(() => RunIO.CollectJsonOutputs(globals, new[] { "bad" }, ctx));
            Assert.StartsWith("output variable 'bad' is not serializable: ", ex.Message);
        }
    }
}
