using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // The sandbox audit of the day's additions (2026-09-11): every pass over script-sized data charges
    // steps (so the deadline sees it), every recursion over script-built data is counted against
    // MaxDataDepth (so it is a RecursionError, not a walk down the C# stack), and the one runaway found
    // (the XPath parent axis on a deep chain) is pinned as finishing.
    [Trait("Category", "ResourceLimits")]
    public class SandboxCoverageTests
    {
        private static long Steps(string src)
        {
            return Ev.Run(src).Ctx.Budget.StepsUsed;
        }

        // A one-megabyte pass charges at least LinearChunk per LinearChunkSize (4096) of it; before the
        // audit each of these ran to completion with a dozen steps on the counter.
        [Theory]
        [InlineData("s = chr(1046) * 1000000\nr = s.encode('cp866')\n")]
        [InlineData("b = bytes([134]) * 1000000\nr = b.decode('cp866')\n")]
        [InlineData("s = 'x' * 1000000\nr = s.encode('utf-8')\n")]
        [InlineData("b = b'abc ' * 250000\nr = b.upper()\n")]
        [InlineData("b = b'abc ' * 250000\nr = b.split()\n")]
        [InlineData("b = b'abc ' * 250000\nr = b.replace(b'a', b'x')\n")]
        [InlineData("b = b'abc ' * 250000\nr = b.title()\n")]
        [InlineData("s = chr(1078) * 1000000\nr = s.upper()\n")]
        [InlineData("s = chr(223) * 1000000\nr = s.casefold()\n")]
        [InlineData("import urllib.parse as up\nr = up.quote('a b/' * 250000)\n")]
        [InlineData("import urllib.parse as up\nr = up.quote(b'a b/' * 250000)\n")]
        [InlineData("import urllib.parse as up\nr = up.unquote('%20a' * 250000)\n")]
        [InlineData("import urllib.parse as up\nr = up.urlsplit('http://h/' + 'p/' * 500000)\n")]
        [InlineData("import html\nr = html.unescape('&amp;' * 200000)\n")]
        [InlineData("import html\nr = html.escape('<' * 1000000)\n")]
        [InlineData("import hashlib\nr = hashlib.sha224(b'x' * 1000000).hexdigest()\n")]
        [InlineData("r = bytes.fromhex('ab' * 500000)\n")]
        public void ALinearPassIsCharged(string src)
        {
            Assert.True(Steps(src) >= 200, "steps: " + Steps(src));
        }

        // A whole-receiver method is charged at the dispatcher (Slots.Linear), whatever its body does:
        // the receiver here is a million items, so the dispatcher alone is 244 steps on top of the
        // setup. issubset and set.copy walked self with no charge of their own before this.
        [Theory]
        [InlineData("s = set(range(1000000))\n", "r = s.issubset({1})\n", 240)]
        [InlineData("s = set(range(1000000))\n", "r = s.copy()\n", 240)]
        [InlineData("s = set(range(1000000))\n", "r = s.isdisjoint({-1})\n", 240)]
        [InlineData("d = dict.fromkeys(range(500000))\n", "r = d.copy()\n", 120)]   // two 1M dicts exceed the default alloc cap
        [InlineData("l = list(range(1000000))\n", "for i in range(10):\n    l.insert(0, i)\n", 2400)]
        [InlineData("l = list(range(1000000))\n", "for i in range(10):\n    l.pop(0)\n", 2400)]
        [InlineData("l = list(range(1000000))\n", "l.reverse()\n", 240)]
        [InlineData("from collections import deque\nd = deque(range(1000000))\n", "d.rotate(1)\n", 240)]
        [InlineData("from collections import Counter\nc = Counter(range(1000000))\n", "r = c.total()\n", 240)]
        [InlineData("import xml.etree.ElementTree as ET\nr = ET.Element('r')\nfor i in range(200000):\n    ET.SubElement(r, 'd')\n",
            "for i in range(10):\n    r.remove(r[-1])\n", 480)]
        public void AWholeReceiverMethodIsChargedAtTheDispatcher(string setup, string op, long atLeast)
        {
            long delta = Steps(setup + op) - Steps(setup);
            Assert.True(delta >= atLeast, "steps for the operation: " + delta);
        }

        // The same rule for every leaf that re-enters an engine with its children: the hook is called
        // with the depth it sits at and continues the count from there, so a chain of deques, slices,
        // ChainMaps, partial arguments or exception arguments is a RecursionError too, and never a
        // C# frame per level. The engines' own work stacks stay unbounded: a deep plain list prints in full.
        [Theory]
        [InlineData("from collections import deque\na = deque(); b = deque()\nfor _ in range(100000):\n    a = deque([a]); b = deque([b])\ntry:\n    r = a == b\nexcept RecursionError:\n    r = 'caught'\n", "'caught'")]
        [InlineData("from collections import deque\na = deque()\nfor _ in range(100000):\n    a = deque([a])\ntry:\n    r = repr(a)\nexcept RecursionError:\n    r = 'caught'\n", "'caught'")]
        [InlineData("from collections import deque\na = deque([0]); b = deque([1])\nfor _ in range(100000):\n    a = deque([a]); b = deque([b])\ntry:\n    r = a < b\nexcept RecursionError:\n    r = 'caught'\n", "'caught'")]
        [InlineData("a = slice(0); b = slice(0)\nfor _ in range(100000):\n    a = slice(a); b = slice(b)\ntry:\n    r = a == b\nexcept RecursionError:\n    r = 'caught'\n", "'caught'")]
        [InlineData("a = slice(0)\nfor _ in range(100000):\n    a = slice(a)\ntry:\n    r = repr(a)\nexcept RecursionError:\n    r = 'caught'\n", "'caught'")]
        [InlineData("from functools import partial\ndef f(*a): pass\np = partial(f)\nfor _ in range(100000):\n    p = partial(f, p)\ntry:\n    r = repr(p)\nexcept RecursionError:\n    r = 'caught'\n", "'caught'")]
        [InlineData("from collections import ChainMap\na = ChainMap({}); b = ChainMap({})\nfor _ in range(100000):\n    a = ChainMap({'k': a}); b = ChainMap({'k': b})\ntry:\n    r = a == b\nexcept RecursionError:\n    r = 'caught'\n", "'caught'")]
        [InlineData("e = ValueError(1)\nfor _ in range(100000):\n    e = ValueError(e)\ntry:\n    r = repr(e)\nexcept RecursionError:\n    r = 'caught'\n", "'caught'")]
        [InlineData("l = []\nfor _ in range(5000):\n    l = [l]\nr = len(repr(l))\n", "10002")]
        [InlineData("from collections import deque\na = deque([1, [2, deque([3])]])\nr = repr(a) + ' ' + str(a == deque([1, [2, deque([3])]]))\n", "'deque([1, [2, deque([3])]]) True'")]
        public void ALeafThatReentersTheEngineCountsItsDepth(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void InsortChargesTheShift()
        {
            // 100 inserts at the front of a 100k list move 10M slots: at 4096 per step that is ~2400
            long steps = Steps("import bisect\nl = list(range(100000))\nfor i in range(100):\n    bisect.insort(l, -i)\n");
            Assert.True(steps >= 2400, "steps: " + steps);
        }

        // .//d/.. on a chain: each parent is looked up in a set, not scanned for in a list, and every
        // node is a step. Before, a 200k chain did not finish inside a 20 s deadline.
        [Fact]
        public void TheParentAxisOnADeepChainFinishes()
        {
            var r = Ev.Run(
                "import xml.etree.ElementTree as ET\n"
                + "r = ET.Element('r'); e = r\n"
                + "for i in range(50000):\n"
                + "    e = ET.SubElement(e, 'd')\n"
                + "n = len(r.findall('.//d/..'))\n",
                ResourceLimits.Default.With(deadlineMs: 10_000));
            Assert.Equal("50000", r.Repr("n"));
        }

        // Record equality and hashing count nesting against MaxDataDepth like a tuple in a tuple, so a
        // deep chain is a RecursionError the script can catch — not the C# stack guard's abort.
        private const string Chain =
            "from dataclasses import dataclass\n"
            + "@dataclass(frozen=True)\n"
            + "class N:\n"
            + "    child: object\n"
            + "a = None; b = None\n"
            + "for _ in range(1000):\n"
            + "    a = N(a); b = N(b)\n";

        [Theory]
        [InlineData("r = a == b\n")]
        [InlineData("r = hash(a)\n")]
        [InlineData("r = a != b\n")]
        public void DeepRecordNestingIsARecursionError(string op)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Chain + op));
            Assert.Equal("RecursionError", ex.Value.ExcType.Name);
            Assert.Equal("maximum recursion depth exceeded in comparison", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void AShallowRecordChainStillCompares()
        {
            Assert.Equal("True", Ev.Run(Chain.Replace("range(1000)", "range(100)") + "r = a == b\n").Repr("r"));
            Assert.Equal("True", Ev.Run(Chain.Replace("range(1000)", "range(100)") + "r = hash(a) == hash(b)\n").Repr("r"));
        }

        [Theory]
        [InlineData("l = []; m = []\nfor _ in range(1000):\n    l = [l]; m = [m]\ntry:\n    r = l == m\nexcept RecursionError:\n    r = 'caught'\n", "'caught'")]
        [InlineData("t = ()\nfor _ in range(1000):\n    t = (t,)\ntry:\n    r = hash(t)\nexcept RuntimeError:\n    r = 'caught as RuntimeError'\n", "'caught as RuntimeError'")]
        [InlineData("r = issubclass(RecursionError, RuntimeError)\n", "True")]
        [InlineData("r = RecursionError.__name__\n", "'RecursionError'")]
        public void RecursionErrorIsCatchableAndARuntimeError(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        // The call-depth cap is the other kind of recursion, and it stays an abort a script cannot
        // swallow: a __eq__ that compares self to other again dies at MaxCallDepth.
        [Fact]
        public void CallDepthStaysAnAbort()
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(
                "class A:\n    def __eq__(self, o):\n        return self == o\ntry:\n    r = A() == A()\nexcept BaseException:\n    r = 'swallowed'\n"));
            Assert.Equal(EngineAbortKind.Recursion, ex.Kind);
        }

        // A record instance as a csv dialect is probed for eight attributes without an exception per
        // miss. With exceptions, a reader built from one cost ~50x a reader built from a dialect NAME;
        // the ratio is what is pinned, best of three each way, because an absolute time is not a fact
        // on a loaded box (the suite runs classes in parallel and this one drifts +-30% on its own).
        [Fact]
        public void ADuckDialectCostsAboutWhatANamedOneDoes()
        {
            const string Loop =
                "import csv\n"
                + "class D:\n"
                + "    def __init__(self):\n"
                + "        self.delimiter = ','\n"
                + "n = 0\n"
                + "for i in range(20000):\n"
                + "    n += len(next(csv.reader(['a,b'], DIALECT)))\n";
            long duck = BestOfThreeMs(Loop.Replace("DIALECT", "D()"));
            long named = BestOfThreeMs(Loop.Replace("DIALECT", "'excel'"));
            Assert.True(duck < 8 * System.Math.Max(named, 5), "duck " + duck + " ms vs named " + named + " ms");
        }

        private static long BestOfThreeMs(string src)
        {
            long best = long.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Assert.Equal("40000", Ev.Run(src, ResourceLimits.Default.With(deadlineMs: 60_000)).Repr("n"));
                sw.Stop();
                if (sw.ElapsedMilliseconds < best)
                    best = sw.ElapsedMilliseconds;
            }
            return best;
        }
    }
}
