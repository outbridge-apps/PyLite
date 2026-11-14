using System;
using System.Linq;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // The second sandbox review (2026-10): work that grew with a script-chosen size but was charged one step -
    // a compiled expression tree of 65,000 nodes, 99,000 comprehension clauses, a frame of 45,000 parameters,
    // a class statement re-walking its __init__, a tail shift per `del L[0]`, a compare per prefix, a dataclass
    // DAG - so the step limit never saw it and the deadline saw it late, and a loop of it leaked the runner
    // thread. Each case runs at size n and 2n: the steps charged must grow with the work (the old engine's
    // stayed flat or grew with the iterations only). Steps are deterministic, so this holds on any machine.
    [Trait("Category", "ResourceLimits")]
    public class WorkChargedAsItScalesTests
    {
        private static long Steps(string src)
        {
            var limits = TestEngines.Unhurried.With(maxSteps: 1_000_000_000);
            RunResult r = TestEngines.WithCeiling(limits).Run(src, new RunRequest { Limits = limits });
            Assert.True(r.Succeeded, r.Succeeded ? null : r.Error.Message);
            return r.Stats.Steps;
        }

        private static string Join(string sep, string item, int n) { return string.Join(sep, Enumerable.Repeat(item, n)); }

        // label, a script for size n, the minimum growth of the steps when n doubles
        public static TheoryData<string, Func<int, string>, int, double> Cases()
        {
            return new TheoryData<string, Func<int, string>, int, double>
            {
                // the per-element work doubles, the element count does not: steps ~2x
                { "fused-or", n => "r = [0 for x in range(300) if " + Join(" or ", "x<0", n) + "]\n", 4000, 1.7 },
                { "compiled-sum", n => "g = 0\nr = [0 for x in range(300) if " + Join("+", "(" + Join("+", "g", 500) + ")", n / 500) + "]\n", 4000, 1.7 },
                { "if-clauses", n => "r = [0 for x in range(300) " + Join(" ", "if 1", n) + "]\n", 4000, 1.7 },
                { "kwonly-frame", n => "def f(*, " + string.Join(", ", Enumerable.Range(0, n).Select(i => "a" + i + "=0")) + "):\n    pass\nfor i in range(300):\n    f()\n", 4000, 1.7 },
                { "startswith-tuple", n => "s = 'a' * " + n + "\nt = (s[:-1] + 'b',) * 300\ns.startswith(t)\n", 200000, 1.7 },
                { "bytes-equal", n => "a = b'a' * " + n + "\nb = b'a' * " + (n - 1) + " + b'a'\nfor i in range(300):\n    a == b\n", 400000, 1.7 },
                { "attrgetter-path", n => "import operator\nclass C:\n    def __init__(self):\n        self.a = self\nc = C()\nf = operator.attrgetter('a.' * " + (n - 1) + " + 'a')\nfor i in range(30):\n    f(c)\n", 40000, 1.7 },
                { "deque-repeat", n => "from collections import deque\nd = deque([0] * " + n + ", maxlen=" + n + ")\nfor i in range(30):\n    d *= 2\n", 200000, 1.7 },
                // a native loop per call that took one step: matches of an empty pattern, a document scanned
                // for a line number, base64 and hex over the input
                { "re-sub-empty", n => "import re\nfor i in range(30):\n    re.sub('', '', 'a' * " + n + ")\n", 20000, 1.7 },
                { "re-split-empty", n => "import re\nfor i in range(30):\n    re.split('', 'a' * " + n + ")\n", 20000, 1.7 },
                { "decode-error-lines", n => "import json\nd = 'x' * " + n + "\nfor i in range(300):\n    json.JSONDecodeError('m', d, len(d))\n", 400000, 1.7 },
                { "b32encode", n => "import base64\ns = b'a' * " + n + "\nfor i in range(30):\n    base64.b32encode(s)\n", 400000, 1.7 },
                { "unhexlify", n => "import binascii\ns = '61' * " + n + "\nfor i in range(30):\n    binascii.unhexlify(s)\n", 200000, 1.7 },
                // each product is linear in the result so far, so the steps grow faster than n (the old count grew 2x)
                { "factorial", n => "import math\nmath.factorial(" + n + ")\n", 20000, 2.5 },
                // quadratic work: the steps grow faster than the iterations when n doubles (the old count grew 2x with them)
                { "del-front", n => "L = [0] * " + n + "\nwhile L:\n    del L[0]\n", 20000, 2.2 },
                { "remove-front", n => "L = list(range(" + n + "))\nfor i in range(" + n + "):\n    L.remove(i)\n", 20000, 2.2 },
                { "xml-del-front", n => "import xml.etree.ElementTree as ET\nr = ET.Element('r')\nfor i in range(" + n + "):\n    ET.SubElement(r, 'c')\nwhile len(r):\n    del r[0]\n", 20000, 2.2 },
                // a dataclass DAG: one more level (n = 1000 is 10 levels, 2n is 11) doubles the nodes along its paths
                { "asdict-dag", n => "from dataclasses import dataclass, asdict\n@dataclass\nclass N:\n    a: object\n    b: object\nx = N(None, None)\nfor i in range(" + (9 + n / 1000) + "):\n    x = N(x, x)\nasdict(x)\n", 1000, 1.7 },
            };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void TheStepsGrowWithTheWork(string label, Func<int, string> script, int n, double growth)
        {
            long small = Steps(script(n));
            long big = Steps(script(2 * n));
            Assert.True(big >= growth * small, label + ": " + small + " steps at n, " + big + " at 2n");
        }

        // `case [a, *rest]` copied 2**40 items into rest, charged as memory only: capped and charged now.
        [Fact]
        public void AStarPatternOverAHugeRangeStopsAtTheItemCap()
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run("match range(2**40):\n    case [a, *rest]:\n        pass\n"));
            Assert.Equal("MaxCollectionItems", ex.LimitName);
        }

        // randbytes made its buffer before the str cap refused the result; it is checked first now, and the
        // block-by-block draw gives the bytes one draw gave.
        [Fact]
        public void RandbytesChecksTheCapFirstAndDrawsTheSameStream()
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run("import random\nrandom.randbytes(250000000)\n",
                ResourceLimits.Default.With(maxStrChars: 1_000_000)));
            Assert.Equal("MaxStrChars", ex.LimitName);
            const string src = "import random\nrandom.seed(5)\na = random.randbytes(70000)\nrandom.seed(5)\n"
                + "b = random.randbytes(65536) + random.randbytes(4464)\nr = (a == b, len(a), random.random() == random.random())\n";
            Assert.Equal("(True, 70000, False)", Ev.Run(src).Repr("r"));
        }

        // The rewritten paths answer as before.
        [Fact]
        public void TheRewrittenPathsAnswerAsBefore()
        {
            const string src = "import html, string, operator\nfrom collections import deque\n"
                + "class C:\n    def __init__(self):\n        self.a = self\n        self.v = 5\n"
                + "d = deque([1, 2, 3], maxlen=5)\nd *= 3\nL = [1, 2, 3, 2]\nL.remove(2)\ndel L[0]\n"
                + "r = (html.unescape('&notit; &amp &ampx &frac34x &amp; &#65;'), string.capwords(' hello  world\\t x '),\n"
                + "     string.capwords('a-b--c', '-'), operator.attrgetter('a.a.v')(C()), list(d), L,\n"
                + "     b'ab' < b'b', b'a' < b'ab', b'abc' == b'abc', sorted([b'b', b'ab', b'', b'a']),\n"
                + "     'abc'.startswith(('x', 'ab')), b'abc'.endswith((b'x', b'bc')), slice(None, None, 3**40).indices(2**70)[2] == 3**40)\n";
            Assert.Equal("('¬it; & &x ¾x & A', 'Hello World X', 'A-B--C', 5, [2, 3, 1, 2, 3], [3, 2], True, True, True, "
                + "[b'', b'a', b'ab', b'b'], True, True, True)", Ev.Run(src).Repr("r"));
        }
    }
}
