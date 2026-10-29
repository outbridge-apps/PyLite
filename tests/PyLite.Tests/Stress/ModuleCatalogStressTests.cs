using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Outbridge.PyLite.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Outbridge.PyLite.Tests.Stress
{
    // The A8 catalog for the stdlib modules: hostile inputs to the module surface rather than to the
    // evaluator. The invariant is the same one the engine sells to its host — a run ends in a LEGAL
    // outcome inside deadline+grace, so the runner thread is never leaked. A HostError here means the
    // engine gave up waiting: some native loop ran without charging the budget.
    [Collection("TimeSensitive")]   // asserts wall-clock bounds; must not race the rest of the suite
    [Trait("Category", "ResourceLimits")]
    [Trait("Category", "MachineSensitive")]
    public class ModuleCatalogStressTests
    {
        private readonly ITestOutputHelper _out;
        public ModuleCatalogStressTests(ITestOutputHelper output) { _out = output; }

        private static ScriptEngine Engine(ResourceLimits lim)
        {
            return new ScriptEngine(new EngineOptions
            {
                DefaultLimits = lim ?? ResourceLimits.Default.With(deadlineMs: 3000),
                Clock = new SystemClock(TimeSpan.Zero, "UTC"),
                SleepPolicy = new NoOpSleepPolicy(),
            });
        }

        // The external cap is a HANG DETECTOR only; the guarantee under test is the outcome kind.
        private RunResult RunGuarded(string source, ResourceLimits lim = null, int hardCapMs = 40000)
        {
            var engine = Engine(lim);
            RunResult result = null;
            var t = Task.Run(() => result = engine.Run(source, new RunRequest()));
            Assert.True(t.Wait(hardCapMs), "engine hung past the external cap (" + hardCapMs + "ms)");
            Assert.NotNull(result);
            _out.WriteLine(result.Error != null ? result.Error.Kind + ": " + result.Error.Message : "SUCCEEDED");
            return result;
        }

        private void AssertKind(string source, ScriptErrorKind kind, ResourceLimits lim = null)
        {
            RunResult r = RunGuarded(source, lim);
            Assert.False(r.Succeeded, "expected " + kind + " but the script succeeded");
            Assert.Equal(kind, r.Error.Kind);
        }

        // A run that is allowed to succeed still has to come back on time and never as a leak.
        private void AssertNoLeak(string source, ResourceLimits lim = null)
        {
            RunResult r = RunGuarded(source, lim);
            if (!r.Succeeded)
                Assert.NotEqual(ScriptErrorKind.HostError, r.Error.Kind);
        }

        // ---- textwrap ----

        // Breaking one long word re-slices its tail per emitted piece, so the work between two outer
        // steps grows without bound: the deadline has to be checked inside the split, not every 1024 steps.
        [Fact]
        public void Textwrap_LongWordBreak()
        {
            var sw = Stopwatch.StartNew();
            AssertKind("import textwrap\nr = textwrap.wrap('x' * 8000000, width=1)\n", ScriptErrorKind.BudgetExceeded);
            Assert.True(sw.ElapsedMilliseconds < 20000, "deadline overrun: " + sw.ElapsedMilliseconds + "ms");
        }

        // The word-separator regex over megabytes is one native call; enumerating its matches lazily is
        // what keeps it interruptible.
        [Fact]
        public void Textwrap_WrapMegabytes()
        {
            AssertKind("import textwrap\nr = textwrap.wrap('word ' * 800000, width=70)\n", ScriptErrorKind.BudgetExceeded);
        }

        [Fact]
        public void Textwrap_DedentMegabytes()
        {
            AssertNoLeak("import textwrap\nr = textwrap.dedent(('    line of text' + chr(10)) * 250000)\n");
        }

        // ---- unicodedata ----

        // NormalizeString is quadratic in the length of one call: 256K expanding characters cost 11s.
        // Chunking keeps it linear, and the result still has to fit MaxStrChars.
        [Fact]
        public void Unicodedata_NormalizeExpansionBomb()
        {
            var sw = Stopwatch.StartNew();
            AssertKind("import unicodedata\nr = unicodedata.normalize('NFKC', chr(0xFDFA) * 1000000)\n",
                ScriptErrorKind.BudgetExceeded);
            Assert.True(sw.ElapsedMilliseconds < 20000, "deadline overrun: " + sw.ElapsedMilliseconds + "ms");
        }

        // A base character followed by thousands of marks has no safe break point, and reordering it is
        // quadratic in the BCL, so it is refused rather than run (a documented divergence).
        [Fact]
        public void Unicodedata_CombiningRunRefused()
        {
            var sw = Stopwatch.StartNew();
            AssertKind("import unicodedata\nr = unicodedata.normalize('NFC', 'a' + (chr(0x334) + chr(0x301)) * 50000)\n",
                ScriptErrorKind.RuntimeError);
            Assert.True(sw.ElapsedMilliseconds < 10000, "refusal took " + sw.ElapsedMilliseconds + "ms");
        }

        // Ordinary text of the same size must still normalize well inside the deadline.
        [Fact]
        public void Unicodedata_NormalizeLargeTextSucceeds()
        {
            RunResult r = RunGuarded("import unicodedata\n"
                + "s = (chr(0xC5) + 'abcdefgh') * 400000\n"
                + "r = unicodedata.normalize('NFD', s)\n"
                + "assert len(r) > len(s)\n");
            Assert.True(r.Succeeded, r.Error != null ? r.Error.Message : "");
        }

        // ---- xml.etree ----

        private const string DeepTree =
            "import xml.etree.ElementTree as ET\n"
            + "root = ET.Element('r')\n"
            + "cur = root\n"
            + "for i in range(20000):\n"
            + "    cur = ET.SubElement(cur, 'e')\n";

        [Fact]
        public void Xml_DeepTreeSerialize()
        {
            AssertNoLeak(DeepTree + "s = ET.tostring(root, encoding='unicode')\n");
        }

        // indent() writes depth characters per level, so a deep tree is quadratic in output size: it must
        // stop on the allocation budget instead of building the string.
        [Fact]
        public void Xml_DeepTreeIndent()
        {
            AssertKind(DeepTree + "ET.indent(root)\n", ScriptErrorKind.BudgetExceeded);
        }

        [Fact]
        public void Xml_DeepTreeIterFind()
        {
            AssertNoLeak(DeepTree + "n = len(list(root.iterfind('.//e')))\n");
        }

        [Fact]
        public void Xml_DeepDocumentParse()
        {
            AssertNoLeak("import xml.etree.ElementTree as ET\n"
                + "root = ET.fromstring('<a>' * 10000 + '</a>' * 10000)\n");
        }

        // ---- json ----

        [Fact]
        public void Json_DeepNestingDump()
        {
            AssertKind("import json\n"
                + "x = []\n"
                + "cur = x\n"
                + "for i in range(5000):\n"
                + "    nxt = []\n"
                + "    cur.append(nxt)\n"
                + "    cur = nxt\n"
                + "s = json.dumps(x)\n", ScriptErrorKind.RuntimeError);
        }

        // A default= hook that keeps handing back a new object must hit the data-depth cap, not the stack.
        [Fact]
        public void Json_DefaultHookChain()
        {
            AssertKind("import json\n"
                + "class W:\n"
                + "    def __init__(self, n):\n"
                + "        self.n = n\n"
                + "def d(o):\n"
                + "    return W(o.n + 1)\n"
                + "s = json.dumps(W(0), default=d)\n", ScriptErrorKind.RuntimeError);
        }

        [Fact]
        public void Json_DeepNestingLoad()
        {
            AssertKind("import json\nx = json.loads('[' * 5000 + ']' * 5000)\n", ScriptErrorKind.RuntimeError);
        }

        // ---- urllib.parse ----

        [Fact]
        public void Urllib_QuoteExpansionBomb()
        {
            AssertKind("from urllib.parse import quote\nr = quote(chr(0x1F600) * 4000000)\n",
                ScriptErrorKind.BudgetExceeded);
        }

        [Fact]
        public void Urllib_UrlencodeBigMapping()
        {
            // Fast enough to finish on a quiet box and to be cut off on a busy one; both are legal, a
            // leaked thread is not.
            AssertNoLeak("from urllib.parse import urlencode\n"
                + "d = {}\n"
                + "for i in range(200000):\n"
                + "    d['key' + str(i)] = 'value' + str(i)\n"
                + "r = urlencode(d)\n");
        }

        // ---- secrets / traceback / collections ----

        [Fact]
        public void Secrets_TokenSizeRefused()
        {
            AssertKind("import secrets\nb = secrets.token_bytes(2000000000)\n", ScriptErrorKind.RuntimeError);
        }

        // A long __context__ chain is capped when the traceback is rendered, not walked recursively.
        [Fact]
        public void Traceback_LongContextChain()
        {
            AssertNoLeak("import traceback\n"
                + "e = ValueError('root')\n"
                + "for i in range(500):\n"
                + "    try:\n"
                + "        raise RuntimeError('lvl') from e\n"
                + "    except RuntimeError as x:\n"
                + "        e = x\n"
                + "try:\n"
                + "    raise e\n"
                + "except RuntimeError:\n"
                + "    t = traceback.format_exc()\n");
        }

        // ---- bytes / re: size bombs in what a template or a tab expands to ----

        // A tab expands to tabsize spaces, so a big tabsize multiplies the input by it. The result length
        // is computed and capped before any buffer is allocated.
        [Fact]
        public void Bytes_ExpandTabsBomb()
        {
            var sw = Stopwatch.StartNew();
            AssertKind("data = bytes([9]) * 4000000\nr = data.expandtabs(1000000)\n", ScriptErrorKind.BudgetExceeded);
            Assert.True(sw.ElapsedMilliseconds < 20000, "deadline overrun: " + sw.ElapsedMilliseconds + "ms");
        }

        // Outputs are capped as they grow, never built first and checked after: each of these used to make
        // the host allocate gigabytes (an OutOfMemory engine fault) or loop unstepped before a limit fired (B6).
        [Theory]
        [InlineData("r = ('a' * 100000).replace('', 'b' * 100000)\n")]
        [InlineData("r = '%.2000000000d' % 1\n")]
        [InlineData("r = '%2000000000d' % 1\n")]
        [InlineData("r = ('%s' * 100) % (('x' * 1000000,) * 100)\n")]
        [InlineData("import json\nr = json.dumps(['x' * 1000000] * 100)\n")]
        [InlineData("import re\nr = re.sub('a', lambda m: 'x' * 1000000, 'a' * 100)\n")]
        [InlineData("import xml.etree.ElementTree as ET\ne = ET.Element('r')\nt = 'x' * 1000000\n"
            + "for i in range(100):\n    ET.SubElement(e, 'c').text = t\nr = ET.tostring(e)\n")]
        [InlineData("import xml.etree.ElementTree as ET\ne = ET.Element('r')\nt = 'x' * 1000000\n"
            + "for i in range(100):\n    ET.SubElement(e, 'c').text = t\nr = ET.tostring(e, method='text')\n")]
        [InlineData("import textwrap\nr = textwrap.indent('a\\n' * 100, 'p' * 1000000)\n")]
        [InlineData("import itertools\nr = itertools.tee([1], 10 ** 9)\n")]
        [InlineData("import calendar\nr = calendar.month(2024, 1, 10 ** 8)\n")]
        [InlineData("import calendar\nr = calendar.month(2024, 1, 2, 10 ** 9)\n")]
        [InlineData("r = b'-'.join([b'x' * 1000000] * 100)\n")]
        [InlineData("r = (b'a' * 100000).replace(b'', b'b' * 100000)\n")]
        public void OutputBombsStopAtTheCap(string source)
        {
            AssertKind(source, ScriptErrorKind.BudgetExceeded);
        }

        [Fact]
        public void LazyReversedAndBatchedNeedNoUpfrontArray()
        {
            RunResult r = RunGuarded("import itertools\ns = 'ab' * 4000000\nr = (next(reversed(s)), next(reversed(s.encode())), "
                + "next(itertools.batched([1, 2], 10 ** 9)))\n");
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Message);
        }

        // A template repeating a group multiplies the subject by the template length. Unchecked, the
        // StringBuilder raised OutOfMemory and the run ended as an engine fault instead of a budget stop.
        [Fact]
        public void Re_ExpandTemplateBomb()
        {
            AssertKind("import re\n"
                + "m = re.match('(a+)', 'a' * 1000000)\n"
                + "r = m.expand((chr(92) + '1') * 100000)\n", ScriptErrorKind.BudgetExceeded);
        }

        [Fact]
        public void Re_SubTemplateBomb()
        {
            AssertKind("import re\n"
                + "r = re.sub('(a+)', (chr(92) + '1') * 100000, 'a' * 1000000)\n", ScriptErrorKind.BudgetExceeded);
        }

        // itertext over a wide tree yields one string per text and tail: bounded by the budget, not by
        // a native call, and a deep tree walks an explicit stack.
        [Fact]
        public void Xml_IterTextFlood()
        {
            AssertNoLeak("import xml.etree.ElementTree as ET\n"
                + "root = ET.Element('r')\n"
                + "for i in range(3000000):\n"
                + "    ET.SubElement(root, 'c').text = 'v'\n"
                + "n = len(list(root.itertext()))\n");
        }

        [Fact]
        public void Xml_IterTextDeepTree()
        {
            RunResult r = RunGuarded(DeepTree + "for e in root.iter():\n    e.text = 't'\n"
                + "n = len(list(root.itertext()))\n");
            Assert.True(r.Succeeded || r.Error.Kind == ScriptErrorKind.BudgetExceeded);
        }

        // pbkdf2 is meant to be slow, so a runaway iteration count must stop on the deadline rather than
        // sit inside one native call, and a huge dklen must be refused before the buffer is allocated.
        [Fact]
        public void Hashlib_Pbkdf2IterationBomb()
        {
            var sw = Stopwatch.StartNew();
            AssertKind("import hashlib\nr = hashlib.pbkdf2_hmac('sha512', b'p', b's', 200000000)\n",
                ScriptErrorKind.BudgetExceeded);
            Assert.True(sw.ElapsedMilliseconds < 20000, "deadline overrun: " + sw.ElapsedMilliseconds + "ms");
        }

        [Fact]
        public void Hashlib_Pbkdf2DkLenBomb()
        {
            AssertKind("import hashlib\nr = hashlib.pbkdf2_hmac('sha256', b'p', b's', 1, 1000000000)\n",
                ScriptErrorKind.BudgetExceeded);
        }

        // ---- struct ----

        // A format is bounded before anything is read: the repeat count has its own cap and the total
        // size goes through the collection limit, so neither a wide format nor a huge one allocates.
        [Theory]
        [InlineData("import struct\nr = struct.calcsize('>1000000000q')\n")]
        [InlineData("import struct\nr = struct.calcsize('>' + 'q' * 4000000)\n")]
        [InlineData("import struct\nr = struct.pack('>8000000s', b'x')\n")]
        [InlineData("import struct\nr = struct.unpack('>' + 'b' * 6000000, bytes(6000000))\n")]
        public void Struct_FormatBombs(string src)
        {
            RunResult r = RunGuarded(src);
            Assert.False(r.Succeeded);
            Assert.True(r.Error.Kind == ScriptErrorKind.BudgetExceeded || r.Error.Kind == ScriptErrorKind.RuntimeError,
                "unexpected " + r.Error.Kind + ": " + r.Error.Message);
        }

        // Materialising every record of a large buffer is legal but budgeted; pulling a few is not.
        [Fact]
        public void Struct_IterUnpackFullBufferIsBudgeted()
        {
            AssertKind("import struct\nr = list(struct.iter_unpack('>b', bytes(15000000)))\n",
                ScriptErrorKind.BudgetExceeded);
        }

        [Fact]
        public void Struct_IterUnpackHeadIsCheap()
        {
            var sw = Stopwatch.StartNew();
            RunResult r = RunGuarded("import struct\n"
                + "it = struct.iter_unpack('>b', bytes(15000000))\n"
                + "n = 0\n"
                + "for t in it:\n"
                + "    n += 1\n"
                + "    if n > 10:\n"
                + "        break\n"
                + "assert n == 11\n");
            Assert.True(r.Succeeded, r.Error != null ? r.Error.Message : "");
            Assert.True(sw.ElapsedMilliseconds < 10000, "lazy read took " + sw.ElapsedMilliseconds + "ms");
        }

        // The format cache is capped, so a script cycling through thousands of distinct formats keeps
        // re-parsing rather than growing the cache without bound.
        [Fact]
        public void Struct_FormatCacheFlood()
        {
            AssertNoLeak("import struct\n"
                + "n = 0\n"
                + "for i in range(200000):\n"
                + "    n += struct.calcsize('>' + 'h' * (i % 4000 + 1))\n");
        }

        // ---- csv ----

        // Sniffer runs four regex passes for the quote guess and then counts every character of every
        // line. Neither could be interrupted, so a multi-megabyte sample outlived deadline and grace.
        // The deadline here is 500 ms: since the 1.1.0 sort the whole pass can finish inside 3 s on a warm
        // JIT, and what is under test is that the pass is interruptible.
        // The deadline is 500 ms: since the 1.1.0 sort the whole pass can finish inside 3 s on a warm JIT,
        // and what is under test is that the pass is interruptible.
        [Fact]
        public void Csv_SnifferBigSample()
        {
            var sw = Stopwatch.StartNew();
            AssertKind("import csv\n"
                + "sample = ('aaa,bbb,ccc' + chr(10)) * 1200000\n"
                + "d = csv.Sniffer().sniff(sample)\n", ScriptErrorKind.BudgetExceeded, ResourceLimits.Default.With(deadlineMs: 500));
            Assert.True(sw.ElapsedMilliseconds < 20000, "deadline overrun: " + sw.ElapsedMilliseconds + "ms");
        }

        [Fact]
        public void Csv_SnifferHasHeaderBigSample()
        {
            AssertKind("import csv\n"
                + "sample = ('aaa,bbb,ccc' + chr(10)) * 1200000\n"
                + "h = csv.Sniffer().has_header(sample)\n", ScriptErrorKind.BudgetExceeded, ResourceLimits.Default.With(deadlineMs: 500));
        }

        // A quoted sample is answered from the head of the sample, so it stays fast at any size.
        [Fact]
        public void Csv_SnifferQuotedSampleStaysFast()
        {
            var sw = Stopwatch.StartNew();
            RunResult r = RunGuarded("import csv\n"
                + "sample = ('\"aaa\",\"bbb\",\"ccc\"' + chr(10)) * 400000\n"
                + "d = csv.Sniffer().sniff(sample)\n"
                + "assert d.delimiter == ','\n");
            Assert.True(r.Succeeded, r.Error != null ? r.Error.Message : "");
            Assert.True(sw.ElapsedMilliseconds < 10000, "quote guess took " + sw.ElapsedMilliseconds + "ms");
        }

        [Fact]
        public void Csv_HugeQuotedField()
        {
            AssertNoLeak("import csv, io\n"
                + "row = '\"' + 'x' * 8000000 + '\"'\n"
                + "r = list(csv.reader(io.StringIO(row)))\n");
        }

        [Fact]
        public void Collections_CounterTotalBig()
        {
            AssertNoLeak("from collections import Counter\n"
                + "c = Counter()\n"
                + "for i in range(500000):\n"
                + "    c[i] = i\n"
                + "t = c.total()\n");
        }
    }
}
