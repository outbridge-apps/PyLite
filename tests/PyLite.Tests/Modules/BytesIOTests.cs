using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // io.BytesIO and the 1.1.0 StringIO additions: what only the engine guarantees — the buffer cap,
    // the allocation budget, clean errors for absurd positions — plus the consumers that read from it.
    [Trait("Category", "Compatibility")]
    public class BytesIOTests
    {
        private static string R(string body, ResourceLimits limits = null)
        {
            return Ev.Run("import io\n" + body, limits).Repr("r");
        }

        private static EngineAbort Abort(string body, ResourceLimits limits)
        {
            return Assert.Throws<EngineAbort>(() => Ev.Run("import io\n" + body + "\n", limits));
        }

        private static string RaisesType(string body)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("import io\n" + body + "\n"));
            return ex.Value.ExcType.Name;
        }

        [Fact]
        public void GrowthPastMaxStrCharsEndsTheRunAsMemory()
        {
            var lim = ResourceLimits.Default.With(maxStrChars: 1000);
            EngineAbort a = Abort("f = io.BytesIO()\nfor i in range(20):\n    f.write(b'x' * 100)", lim);
            Assert.Equal(EngineAbortKind.Memory, a.Kind);
            Assert.Equal("MaxStrChars", a.LimitName);
        }

        [Fact]
        public void StringIOGrowthPastMaxStrCharsEndsTheRunAsMemory()
        {
            var lim = ResourceLimits.Default.With(maxStrChars: 1000);
            EngineAbort a = Abort("f = io.StringIO()\nfor i in range(20):\n    f.write('x' * 100)", lim);
            Assert.Equal(EngineAbortKind.Memory, a.Kind);
        }

        [Theory]
        [InlineData("f = io.BytesIO()\nf.seek(2 ** 40)\nf.write(b'x')")]
        [InlineData("f = io.BytesIO()\nf.seek(2 ** 62)\nf.write(b'x')")]
        [InlineData("f = io.StringIO()\nf.seek(2 ** 40)\nf.write('x')")]
        public void AFarSeekThenWriteIsAMemoryAbortNotAHostFault(string body)
        {
            EngineAbort a = Abort(body, ResourceLimits.Default);
            Assert.Equal(EngineAbortKind.Memory, a.Kind);
        }

        [Fact]
        public void RepeatedGetvalueIsCharged()
        {
            // 1 MB buffer, 300 copies = 300 MB of bytes objects against a 64 MB budget.
            var lim = ResourceLimits.Default.With(maxAllocBytes: 64 * 1024 * 1024);
            EngineAbort a = Abort("f = io.BytesIO(b'x' * 1000000)\nkeep = []\nfor i in range(300):\n    keep.append(f.getvalue())", lim);
            Assert.Equal(EngineAbortKind.Memory, a.Kind);
        }

        [Fact]
        public void ReallocationIsChargedSoAWriteLoopHitsTheBudget()
        {
            var lim = ResourceLimits.Default.With(maxAllocBytes: 8 * 1024 * 1024);
            EngineAbort a = Abort("f = io.BytesIO()\nchunk = b'y' * 65536\nfor i in range(400):\n    f.write(chunk)", lim);
            Assert.Equal(EngineAbortKind.Memory, a.Kind);
        }

        [Theory]
        [InlineData("io.BytesIO().seek(2 ** 70)", "OverflowError")]
        [InlineData("io.BytesIO().read(2 ** 70)", "OverflowError")]
        [InlineData("io.BytesIO().seek(0, 2 ** 40)", "OverflowError")]
        [InlineData("io.BytesIO().seek('0')", "TypeError")]
        [InlineData("io.BytesIO().read(1.0)", "TypeError")]
        [InlineData("io.BytesIO().write('text')", "TypeError")]
        [InlineData("io.BytesIO('text')", "TypeError")]
        [InlineData("io.BytesIO(b'', b'')", "TypeError")]
        [InlineData("io.BytesIO(b'x', initial_bytes=b'y')", "TypeError")]
        [InlineData("io.StringIO().seek(2 ** 70)", "OverflowError")]
        [InlineData("io.StringIO().read('1')", "TypeError")]
        public void AbsurdArgumentsAreScriptErrors(string body, string type)
        {
            Assert.Equal(type, RaisesType(body));
        }

        [Fact]
        public void NegativeSeekRelativeClampsToZero()
        {
            Assert.Equal("(0, b'abc')", R("f = io.BytesIO(b'abc')\nr = (f.seek(-100, 1), f.read())"));
        }

        [Fact]
        public void TruncateNeverExtendsAndOverseekPadsWithZeros()
        {
            Assert.Equal("(b'abc', b'ab\\x00\\x00z')", R("f = io.BytesIO(b'abc')\nf.truncate(10)\na = f.getvalue()\nf.truncate(2)\nf.seek(4)\nf.write(b'z')\nr = (a, f.getvalue())"));
        }

        [Fact]
        public void JsonLoadReadsBytesIO()
        {
            Assert.Equal("{'a': [1, 2], 'é': 'ü'}", R("import json\nr = json.load(io.BytesIO('{\"a\": [1, 2], \"é\": \"ü\"}'.encode('utf-8')))"));
        }

        [Fact]
        public void ElementTreeParseReadsBytesIO()
        {
            Assert.Equal("('root', 'ü')", R("import xml.etree.ElementTree as ET\nt = ET.parse(io.BytesIO('<root><a>ü</a></root>'.encode('utf-8')))\nr = (t.getroot().tag, t.getroot().find('a').text)"));
        }

        [Theory]
        [InlineData("t.write(f, encoding='utf-8', xml_declaration=True)", "b\"<?xml version='1.0' encoding='utf-8'?>\\n<a>\\xc3\\xbc</a>\"")]
        [InlineData("t.write(f)", "b'<a>&#252;</a>'")]
        [InlineData("t.write(f, 'cp1251')", "b\"<?xml version='1.0' encoding='cp1251'?>\\n<a>&#252;</a>\"")]
        public void ElementTreeWritesBytesIntoBytesIO(string call, string expected)
        {
            Assert.Equal(expected, R("import xml.etree.ElementTree as ET\nt = ET.ElementTree(ET.fromstring('<a>ü</a>'))\nf = io.BytesIO()\n"
                + call + "\nr = f.getvalue()"));
        }

        [Fact]
        public void ElementTreeBytesIntoStringIOIsCPythonsTypeError()
        {
            Assert.Equal("TypeError", RaisesType("import xml.etree.ElementTree as ET\nET.ElementTree(ET.Element('a')).write(io.StringIO())"));
        }

        [Fact]
        public void GzipRoundTripThroughBytesIO()
        {
            Assert.Equal("b'payloadpayload'", R("import gzip\nf = io.BytesIO()\nf.write(gzip.compress(b'payload' * 2))\nf.seek(0)\nr = gzip.decompress(f.read())"));
        }

        [Fact]
        public void NextSkipsAHeaderLine()
        {
            Assert.Equal("(b'h\\n', [b'1\\n', b'2'])", R("f = io.BytesIO(b'h\\n1\\n2')\nh = next(f)\nr = (h, list(f))"));
        }

        [Fact]
        public void HostileCallsNeverFaultAtTheEngineBoundary()
        {
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = ResourceLimits.Batch });
            foreach (string src in new[]
            {
                "import io\nio.BytesIO().seek(2 ** 63)\n",
                "import io\nf = io.BytesIO(b'abc')\nf.seek(2 ** 62)\nf.read()\nf.readline()\nf.readlines()\nf.truncate()\nf.write(b'x')\n",
                "import io\nio.BytesIO().truncate(-2 ** 63)\n",
                "import io\nio.StringIO('abc').readline(2 ** 62)\n",
            })
            {
                RunResult r = engine.Run(src, new RunRequest());
                Assert.True(r.Succeeded || r.Error.Kind != ScriptErrorKind.EngineFault, src);
            }
        }
    }
}
