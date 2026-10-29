using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // The document grammar is RFC 8259. Newtonsoft is lenient by design and has no strict mode, so the
    // token stream is checked against the raw text (JsonGuard) and anything doubtful goes to the full
    // scan (JsonScan) — every expectation below is a leniency the reader would have let through. The
    // message and the position are CPython's own.
    [Trait("Category", "Compatibility")]
    public class JsonStrictTests
    {
        private const string P = "import json\n";

        private static string Err(string src)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(P + "r = json.loads(" + src + ")\n"));
            Assert.Equal("JSONDecodeError", ex.Value.ExcType.Name);
            return ex.Value.GetMessageText(Rt.Ctx());
        }

        [Theory]
        // unquoted and single-quoted names and strings
        [InlineData("'{a: 1}'", "Expecting property name enclosed in double quotes: line 1 column 2 (char 1)")]
        [InlineData("'{\\'a\\': 1}'", "Expecting property name enclosed in double quotes: line 1 column 2 (char 1)")]
        [InlineData("\"['a', 'b']\"", "Expecting value: line 1 column 2 (char 1)")]
        // trailing and empty commas
        [InlineData("'[1,2,]'", "Expecting value: line 1 column 6 (char 5)")]
        [InlineData("'{\"a\":1,}'", "Expecting property name enclosed in double quotes: line 1 column 8 (char 7)")]
        [InlineData("'[1,,2]'", "Expecting value: line 1 column 4 (char 3)")]
        [InlineData("'[,1]'", "Expecting value: line 1 column 2 (char 1)")]
        // comments
        [InlineData("'[1] // c'", "Extra data: line 1 column 5 (char 4)")]
        [InlineData("'[/*x*/1]'", "Expecting value: line 1 column 2 (char 1)")]
        // number shapes JavaScript allows and JSON does not
        [InlineData("'[01]'", "Expecting ',' delimiter: line 1 column 3 (char 2)")]
        [InlineData("'[0x10]'", "Expecting ',' delimiter: line 1 column 3 (char 2)")]
        [InlineData("'[1.]'", "Expecting ',' delimiter: line 1 column 3 (char 2)")]
        [InlineData("'[.5]'", "Expecting value: line 1 column 2 (char 1)")]
        [InlineData("'[+1]'", "Expecting value: line 1 column 2 (char 1)")]
        // JavaScript values
        [InlineData("'[undefined]'", "Expecting value: line 1 column 2 (char 1)")]
        [InlineData("'new Date(1)'", "Expecting value: line 1 column 1 (char 0)")]
        [InlineData("'[True]'", "Expecting value: line 1 column 2 (char 1)")]
        // a raw line break and bad escapes inside strings
        [InlineData("'[\"a\\nb\"]'", "Invalid control character '\\n' at: line 2 column 1 (char 4)")]
        [InlineData("'[\"a\\rb\"]'", "Invalid control character '\\r' at: line 1 column 5 (char 4)")]   // CPython counts only \n
        [InlineData("'{\"k\": \"a\\nb\"}'", "Invalid control character '\\n' at: line 2 column 1 (char 9)")]
        [InlineData("'[\"\\\\x41\"]'", "Invalid \\escape: 'x': line 1 column 4 (char 3)")]
        [InlineData("'[\"\\\\u00G1\"]'", "Invalid \\uXXXX escape: line 1 column 4 (char 3)")]
        public void LenientInputIsRefused(string src, string message)
        {
            Assert.Equal(message, Err(src));
        }

        [Theory]
        [InlineData("'[1] [2]'", "Extra data: line 1 column 5 (char 4)")]
        [InlineData("'{\"a\":1}{\"b\":2}'", "Extra data: line 1 column 8 (char 7)")]
        [InlineData("'{\"a\"}'", "Expecting ':' delimiter: line 1 column 5 (char 4)")]
        [InlineData("'{\"a\":}'", "Expecting value: line 1 column 6 (char 5)")]
        [InlineData("'[1'", "Expecting ',' delimiter: line 1 column 3 (char 2)")]
        [InlineData("'{\"a\":1'", "Expecting ',' delimiter: line 1 column 7 (char 6)")]
        [InlineData("'[\"abc'", "Unterminated string starting at: line 1 column 2 (char 1)")]
        [InlineData("''", "Expecting value: line 1 column 1 (char 0)")]
        [InlineData("'   '", "Expecting value: line 1 column 4 (char 3)")]
        // the line and column arithmetic on a document that really has lines
        [InlineData("'{\\n  \"a\": 1,\\n  \"b\": x\\n}'", "Expecting value: line 3 column 8 (char 19)")]
        public void StructuralErrorsCarryCPythonsWording(string src, string message)
        {
            Assert.Equal(message, Err(src));
        }

        [Theory]
        // NaN and the infinities are CPython's own default, not a leniency to close
        [InlineData("json.loads('[NaN]')[0] != json.loads('[NaN]')[0]", "True")]
        [InlineData("str(json.loads('[Infinity]')[0])", "'inf'")]
        [InlineData("str(json.loads('[-Infinity]')[0])", "'-inf'")]
        [InlineData("json.loads('null')", "None")]
        [InlineData("json.loads('\\\"bare\\\"')", "'bare'")]
        [InlineData("json.loads('[1e2]')", "[100.0]")]
        [InlineData("json.loads('[-0.5e-3]')", "[-0.0005]")]
        [InlineData("json.loads('[0]')", "[0]")]
        [InlineData("json.loads(' [ 1 , 2 ] ')", "[1, 2]")]
        [InlineData("json.loads('[]')", "[]")]
        [InlineData("json.loads('{}')", "{}")]
        [InlineData("json.loads('[[[[[1]]]]]')", "[[[[[1]]]]]")]
        [InlineData("json.loads('{\\\"a\\\":1,\\\"a\\\":2}')", "{'a': 2}")]
        [InlineData("json.loads('[123456789012345678901234567890]')", "[123456789012345678901234567890]")]
        [InlineData("json.loads('[\\\"\\\\u0041\\\\n\\\\t\\\\/\\\\\\\\\\\"]')", "['A\\n\\t/\\\\']")]
        [InlineData("json.loads('{\\\"a\\\": [1, {\\\"b\\\": null}], \\\"c\\\": true}')", "{'a': [1, {'b': None}], 'c': True}")]
        public void ValidDocumentsStillParse(string expr, string expected)
        {
            Assert.Equal(expected, Ev.ReprOf(P, expr));
        }

        // strict= is CPython's own parameter and covers exactly one axis: a raw control character inside
        // a string. It is not a way to get the rest of the leniency back.
        [Fact]
        public void StrictFalseAllowsControlCharactersOnly()
        {
            Assert.Equal("['a\\nb']", Ev.Run(P + "r = json.loads('[\"a\\nb\"]', strict=False)\n").Repr("r"));
            Assert.Equal("['a\\nb']", Ev.Run(P + "r = json.loads('[\"a\\nb\"]', strict=0)\n").Repr("r"));
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(P + "r = json.loads('[1,2,]', strict=False)\n"));
            Assert.Equal("JSONDecodeError", ex.Value.ExcType.Name);
        }

        // JSONDecodeError(msg, doc, pos), as CPython builds it: args is the one formatted message, and the
        // five attributes are there — the same for an error the engine raises and one the script makes.
        [Theory]
        [InlineData("e = err('[1,,2]'); r = (e.msg, e.pos, e.lineno, e.colno, e.doc, e.args)",
            "('Expecting value', 3, 1, 4, '[1,,2]', ('Expecting value: line 1 column 4 (char 3)',))")]
        [InlineData("e = err('{\\n  \"a\": 1,\\n  \"b\": x\\n}'); r = (e.msg, e.pos, e.lineno, e.colno)", "('Expecting value', 19, 3, 8)")]
        [InlineData("e = err('[1e400]'); r = (e.msg, e.pos, e.doc)", "('Expecting value', 1, '[1e400]')")]         // the reader's own limit
        [InlineData("e = err('[1'); r = (e.msg, e.pos, repr(e))", "(\"Expecting ',' delimiter\", 2, 'JSONDecodeError(\"Expecting \\',\\' delimiter: line 1 column 3 (char 2)\")')")]
        [InlineData("e = err(''); r = (e.msg, e.pos, e.lineno, e.colno, isinstance(e, ValueError))", "('Expecting value', 0, 1, 1, True)")]
        [InlineData("e = json.JSONDecodeError('boom', 'ab\\ncd', 4); r = (str(e), e.lineno, e.colno, e.args)",
            "('boom: line 2 column 2 (char 4)', 2, 2, ('boom: line 2 column 2 (char 4)',))")]
        [InlineData("e = err('[1,,2]'); r = e.doc is doc", "True")]                                                  // the script's own str, not a copy
        public void DecodeErrorCarriesItsAttributes(string src, string expected)
        {
            const string pre = "import json\ndoc = None\ndef err(s):\n    global doc\n    doc = s\n    try:\n        json.loads(s)\n    except json.JSONDecodeError as e:\n        return e\n";
            Assert.Equal(expected, Ev.Run(P + pre + src + "\n").Repr("r"));
        }

        [Theory]
        [InlineData("json.JSONDecodeError('m')", "TypeError")]
        [InlineData("json.JSONDecodeError('m', 'd', 'p')", "TypeError")]
        public void DecodeErrorConstructorIsStrict(string expr, string exc)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(P + "r = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }

        // The reader keeps a raw control character other than CR and LF without a trace, and walking
        // every string again to find one costs more than the parse of a long one. Documented deviation.
        [Fact]
        public void RawTabInsideAStringIsTheOpenEdge()
        {
            Assert.Equal("['a\\tb']", Ev.Run(P + "r = json.loads('[\"a\\tb\"]')\n").Repr("r"));
        }

        // The guard trusts the reader's line position only where the text confirms it; on a pretty-printed
        // document every token sits on its own line and the arithmetic has to stay in step throughout.
        [Theory]
        [InlineData("'{\\n  \"a\": [\\n    1,\\n    2.5,\\n    \"x\"\\n  ],\\n  \"b\": null\\n}'", "{'a': [1, 2.5, 'x'], 'b': None}")]
        [InlineData("'{\\r\\n  \"a\": 1,\\r\\n  \"b\": [true]\\r\\n}'", "{'a': 1, 'b': [True]}")]
        [InlineData("'\\n\\n  [ 1 ,\\n 2 ]  \\n'", "[1, 2]")]
        [InlineData("'{\"a\"\\n:\\n1}'", "{'a': 1}")]
        [InlineData("'[\"a\\\\nb\", \"\\\\u00e9\", -0.5e-3, 1E2, NaN]'", "['a\\nb', 'é', -0.0005, 100.0, nan]")]
        public void MultiLineDocumentsStayInStep(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(P + "r = json.loads(" + src + ")\n").Repr("r"));
        }
    }
}
