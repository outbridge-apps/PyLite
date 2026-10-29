using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/08 json module (PyJson: Newtonsoft reader + own writer). Phase 1: loads/dumps + JSONDecodeError.
    [Trait("Category", "Compatibility")]
    public class JsonTests
    {
        // repr of `r`, where r = (expr) evaluated after `import json`.
        private static string J(string expr)
        {
            return Ev.Run("import json\nr = (" + expr + ")\n").Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run("import json\n" + body + "\n"));
        }

        // ---- dumps: default= ----

        [Theory]
        [InlineData("json.dumps({'d': dt(2024, 1, 1, 2, 3)}, default=str)", "'{\"d\": \"2024-01-01 02:03:00\"}'")]
        [InlineData("json.dumps({'d': dt(2024, 1, 1)}, default=lambda o: o.isoformat())", "'{\"d\": \"2024-01-01T00:00:00\"}'")]
        [InlineData("json.dumps({'a': Decimal('1.5')}, default=float)", "'{\"a\": 1.5}'")]
        [InlineData("json.dumps([{1, 2}], default=sorted)", "'[[1, 2]]'")]
        // the hook also sees the datetime inside its own stand-in
        [InlineData("json.dumps(P(1), default=lambda o: {'x': o.x, 'when': dt(2024, 1, 1)} if hasattr(o, 'x') else str(o), sort_keys=True)", "'{\"when\": \"2024-01-01 00:00:00\", \"x\": 1}'")]
        [InlineData("json.dumps([1, 'a', None], default=str)", "'[1, \"a\", null]'")]
        [InlineData("json.dumps({'a': 1}, default=None)", "'{\"a\": 1}'")]
        public void DumpsDefault(string expr, string expected)
        {
            const string pre = "import json\nfrom datetime import datetime as dt\nfrom decimal import Decimal\n"
                + "class P:\n    x: int\n    def __init__(self, x):\n        self.x = x\n";
            Assert.Equal(expected, Ev.Run(pre + "r = (" + expr + ")\n").Repr("r"));
        }

        [Theory]
        [InlineData("json.dumps(P(1), default=lambda o: o)", "ValueError")]                  // the same object again: a cycle
        [InlineData("json.dumps(P(1), default=lambda o: P(o.x))", "RecursionError")]         // fresh objects forever
        [InlineData("json.dumps(P(1), default=lambda o: [o])", "ValueError")]
        [InlineData("json.dumps(P(1), default=1)", "TypeError")]
        [InlineData("json.dumps(P(1))", "TypeError")]
        [InlineData("json.dumps(P(1), default=lambda o: o.nope)", "AttributeError")]         // the hook's own error surfaces
        public void DumpsDefaultErrors(string expr, string exc)
        {
            const string pre = "import json\nclass P:\n    x: int\n    def __init__(self, x):\n        self.x = x\n";
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(pre + "r = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }

        // ---- loads: parse_float / parse_int ----
        // The hook is called with the number's text as the document spells it, which the guard knows
        // from its position bookkeeping; the reader itself never exposes it.

        private const string Dec = "from decimal import Decimal\n";

        [Theory]
        [InlineData("json.loads('{\"p\": 19.99}', parse_float=Decimal)", "{'p': Decimal('19.99')}")]
        [InlineData("json.loads('[1.0, 2.50, 0.1, -0.5]', parse_float=Decimal)", "[Decimal('1.0'), Decimal('2.50'), Decimal('0.1'), Decimal('-0.5')]")]
        [InlineData("json.loads('[1, 2.5, \"3.5\"]', parse_float=Decimal)", "[1, Decimal('2.5'), '3.5']")]      // ints and strings untouched
        [InlineData("json.loads('12345678901234567.89', parse_float=Decimal)", "Decimal('12345678901234567.89')")]
        [InlineData("json.loads('1.0000000000000000000000001', parse_float=Decimal)", "Decimal('1.0000000000000000000000001')")]
        [InlineData("sum(json.loads('[0.1, 0.2]', parse_float=Decimal), Decimal(0))", "Decimal('0.3')")]
        [InlineData("json.loads('1e2', parse_float=Decimal)", "Decimal('1E+2')")]                                   // the document's spelling
        [InlineData("json.loads('[1.5]', parse_float=float)", "[1.5]")]
        [InlineData("json.loads('[1.5]', parse_float=None)", "[1.5]")]
        [InlineData("json.loads('[1.5]')", "[1.5]")]
        // any callable, and it sees the text verbatim
        [InlineData("json.loads('[1.50, -0.0, 2E+3, 1e-7]', parse_float=str)", "['1.50', '-0.0', '2E+3', '1e-7']")]
        [InlineData("json.loads('{\"a\": [1.5, {\"b\": 2.25}], \"c\": 3.0}', parse_float=lambda t: t + '!')", "{'a': ['1.5!', {'b': '2.25!'}], 'c': '3.0!'}")]
        [InlineData("json.loads('[1, -2, 0, 123456789012345678901234567890]', parse_int=str)", "['1', '-2', '0', '123456789012345678901234567890']")]
        [InlineData("json.loads('[1, 2.5]', parse_int=float)", "[1.0, 2.5]")]
        [InlineData("json.loads('[-0]', parse_int=str)", "['-0']")]
        [InlineData("json.loads('[1.5, 2]', parse_float=str, parse_int=str)", "['1.5', '2']")]
        [InlineData("json.loads('[NaN, Infinity, -Infinity, 1.5]', parse_float=str)[3]", "'1.5'")]              // the constants are parse_constant's
        [InlineData("str(json.loads('[NaN, Infinity, -Infinity]', parse_float=str))", "'[nan, inf, -inf]'")]
        // positions stay right across lines, indents and escapes in the strings before a number
        [InlineData("json.loads('[\\n  1.25,\\n  \"a\\\\nb\",\\n  2.50\\n]', parse_float=str)", "['1.25', 'a\\nb', '2.50']")]
        [InlineData("json.loads('{\"k\"\\n:\\n1.10}', parse_float=str)", "{'k': '1.10'}")]                     // a break between key and colon
        [InlineData("json.loads('{\"x\": 1.10}', parse_float=Decimal)", "{'x': Decimal('1.10')}")]
        [InlineData("json.loads('[1.5]', parse_float=int)", "ValueError")]                                    // the hook's own error surfaces
        public void ParseHooks(string expr, string expected)
        {
            if (expected == "ValueError")
            {
                var ex = Assert.Throws<ScriptException>(() => Ev.Run("import json\n" + Dec + "r = " + expr + "\n"));
                Assert.Equal(expected, ex.Value.ExcType.Name);
                return;
            }
            Assert.Equal(expected, Ev.Run("import json\n" + Dec + "r = (" + expr + ")\n").Repr("r"));
        }

        [Theory]
        [InlineData("json.loads('1.5', parse_float=Decimal.from_float)", "TypeError")]                    // its own: from_float wants a float
        [InlineData("json.loads('1.5', parse_float=1)", "TypeError")]                                     // not callable
        [InlineData("json.loads('{}', object_pairs_hook=dict)", "TypeError")]   // still absent, unlike object_hook
        [InlineData("json.loads('1e40', parse_float=Decimal)", "Overflow")]                               // beyond System.Decimal: decimal's own signal
        public void ParseHookErrors(string expr, string exc)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("import json\n" + Dec + "r = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }

        // ---- loads ----

        [Theory]
        [InlineData("json.loads('1')", "1")]                        // int, not float
        [InlineData("json.loads('1.5')", "1.5")]                    // float
        [InlineData("json.loads('1e3')", "1000.0")]                 // exponent -> float
        [InlineData("json.loads('-42')", "-42")]
        [InlineData("json.loads('true')", "True")]
        [InlineData("json.loads('false')", "False")]
        [InlineData("json.loads('null')", "None")]
        [InlineData("json.loads('\"hi\"')", "'hi'")]
        [InlineData("json.loads('[1, 2, 3]')", "[1, 2, 3]")]
        [InlineData("json.loads('{\"a\": 1, \"b\": [2, 3]}')", "{'a': 1, 'b': [2, 3]}")]
        [InlineData("json.loads('{\"a\": 1, \"a\": 2}')", "{'a': 2}")]     // duplicate key -> last wins
        [InlineData("json.loads('123456789012345678901234567890')", "123456789012345678901234567890")]  // big int
        [InlineData("json.loads('  [ 1 , 2 ] ')", "[1, 2]")]        // whitespace tolerant
        public void Loads(string expr, string expected)
        {
            Assert.Equal(expected, J(expr));
        }

        [Fact]
        public void LoadsEmptyIsDecodeError()
        {
            var ex = Raises("json.loads('')");
            Assert.Equal("JSONDecodeError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void LoadsGarbageIsDecodeError()
        {
            Assert.Equal("JSONDecodeError", Raises("json.loads('{bad}')").Value.ExcType.Name);
        }

        [Fact]
        public void LoadsExtraDataIsDecodeError()
        {
            Assert.Equal("JSONDecodeError", Raises("json.loads('1 2')").Value.ExcType.Name);
        }

        // Truncated / unterminated containers: the Newtonsoft reader returns false at EOF instead of
        // throwing, so without the end-of-parse stack check these silently yielded a partial value.
        [Theory]
        [InlineData("json.loads('[1,2,')")]
        [InlineData("json.loads('[1,2')")]
        [InlineData("json.loads('[')")]
        [InlineData("json.loads('[[1]')")]
        [InlineData("json.loads('{')")]
        [InlineData("json.loads('{\"a\":1')")]
        [InlineData("json.loads('{\"a\":')")]
        [InlineData("json.loads('{\"a\"')")]
        public void LoadsTruncatedIsDecodeError(string body)
        {
            Assert.Equal("JSONDecodeError", Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void DecodeErrorIsCaughtAsValueError()
        {
            // JSONDecodeError subclasses ValueError.
            string src = "import json\ncaught = 0\ntry:\n    json.loads('')\nexcept ValueError:\n    caught = 1\n";
            Assert.Equal("1", Ev.Run(src).Repr("caught"));
        }

        [Fact]
        public void LoadsNonStrIsTypeError()
        {
            Assert.Equal("TypeError", Raises("json.loads(5)").Value.ExcType.Name);
        }

        // ---- dumps ----

        [Theory]
        [InlineData("json.dumps(1) == '1'")]
        [InlineData("json.dumps(1.5) == '1.5'")]
        [InlineData("json.dumps(True) == 'true'")]
        [InlineData("json.dumps(None) == 'null'")]
        [InlineData("json.dumps('hi') == '\"hi\"'")]
        [InlineData("json.dumps([1, 2, 3]) == '[1, 2, 3]'")]                       // default sep ", "
        [InlineData("json.dumps({'a': 1, 'b': 2}) == '{\"a\": 1, \"b\": 2}'")]     // insertion order
        [InlineData("json.dumps({'b': 1, 'a': 2}, sort_keys=True) == '{\"a\": 2, \"b\": 1}'")]
        [InlineData("json.dumps([1, 2], separators=(',', ':')) == '[1,2]'")]
        [InlineData("json.dumps('café', ensure_ascii=False) == '\"café\"'")]
        [InlineData("json.dumps({1: 'a'}) == '{\"1\": \"a\"}'")]                   // int key -> string
        [InlineData("json.dumps({True: 'x'}) == '{\"true\": \"x\"}'")]             // bool key -> string
        [InlineData("json.dumps([float('nan')]) == '[NaN]'")]                      // allow_nan default
        [InlineData("json.dumps([]) == '[]'")]
        [InlineData("json.dumps({}) == '{}'")]
        [InlineData("json.dumps([1, 2], indent=2) == '[\\n  1,\\n  2\\n]'")]       // indented
        public void Dumps(string boolExpr)
        {
            Assert.Equal("True", J(boolExpr));
        }

        [Fact]
        public void DumpsEnsureAsciiEscapesNonAscii()
        {
            Assert.Equal("True", J("json.dumps('café') == '\"caf\\\\u00e9\"'"));
        }

        [Fact]
        public void DumpsNanWithoutAllowNanRaises()
        {
            Assert.Equal("ValueError", Raises("json.dumps(float('nan'), allow_nan=False)").Value.ExcType.Name);
        }

        [Fact]
        public void DumpsCircularRaises()
        {
            var ex = Raises("x = []\nx.append(x)\njson.dumps(x)");
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
            Assert.Contains("Circular", ex.Value.GetMessageText(Rt.Ctx()));
        }

        // Data nested past MaxDataDepth is a RecursionError, as CPython's encoder past its recursion limit.
        [Theory]
        [InlineData("x = []\nfor _ in range(200):\n    x = [x]")]
        [InlineData("x = {}\nfor _ in range(200):\n    x = {'a': x}")]
        [InlineData("x = ()\nfor _ in range(200):\n    x = (x,)")]
        public void DumpsTooDeepIsRecursionError(string build)
        {
            var ex = Raises(build + "\njson.dumps(x, indent=1)");
            Assert.Equal("RecursionError", ex.Value.ExcType.Name);
            Assert.Equal("maximum recursion depth exceeded while encoding a JSON object", ex.Value.GetMessageText(Rt.Ctx()));
            Assert.Equal("'ok'", Ev.Run("import json\n" + build + "\ntry:\n    json.dumps(x)\nexcept RecursionError:\n    r = 'ok'\n").Repr("r"));
        }

        [Fact]
        public void DumpsNonSerializableRaisesTypeError()
        {
            Assert.Equal("TypeError", Raises("json.dumps(set())").Value.ExcType.Name);
        }

        [Fact]
        public void RoundTrip()
        {
            Assert.Equal("True",
                J("json.loads(json.dumps({'a': [1, 2], 'b': None, 'c': 'x'})) == {'a': [1, 2], 'b': None, 'c': 'x'}"));
        }
    }
}
