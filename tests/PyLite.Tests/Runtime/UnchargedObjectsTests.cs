using System.Text;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // The second sandbox review (2026-10) found memory a script could take with nothing charged and no cap:
    // objects a module call returns that keep script values alive (g = itemgetter(g) chained to GBs), copies
    // and per-level texts made inside one call, class tables, and script iterators drained into a plain list.
    // Each now ends on the limit it would cross. Small limits keep every case fast.
    [Trait("Category", "ResourceLimits")]
    public class UnchargedObjectsTests
    {
        private static readonly ResourceLimits Small = TestEngines.Unhurried.With(   // memory decides, never the clock
            maxStrChars: 1_000_000, maxCollectionItems: 10_000, maxAllocBytes: 32L << 20);

        [Theory]
        [InlineData("import operator\ng = None\nfor _ in range(500000):\n    g = operator.itemgetter(g)\n")]
        [InlineData("import operator\ng = None\nfor _ in range(500000):\n    g = operator.methodcaller('x', g)\n")]
        [InlineData("import functools\nd = functools.lru_cache(maxsize=0)\nf = len\nfor _ in range(600000):\n    f = d(f)\n")]
        [InlineData("import functools\nK = functools.cmp_to_key(lambda a, b: 0)\nk = None\nfor _ in range(600000):\n    k = K(k)\n")]
        [InlineData("import functools\nf = len\nfor _ in range(200000):\n    f = functools.wraps(f)\n")]
        [InlineData("import dataclasses\nf = None\nfor _ in range(500000):\n    f = dataclasses.field(default=f)\n")]
        [InlineData("from collections import namedtuple\nP = namedtuple('P', ['f%d' % i for i in range(1000)])\np = P._make(range(1000))\nL = []\nfor i in range(5000):\n    L.append(p._replace(f0=i))\n")]
        [InlineData("s = 'a' * 900000\nL = []\nfor i in range(40):\n    L.append(format(s, '.899999'))\n")]
        [InlineData("import json\nx = 1\nfor i in range(100):\n    x = [x]\ns = json.dumps(x, separators=('y' * 900000, ':'), indent=0)\n")]
        [InlineData("import json\nn = [0]\ndef f(o):\n    n[0] += 1\n    if n[0] < 120:\n        return iter([])\n    return [1]\ns = json.dumps(iter([]), indent='x' * 5000, default=f)\n")]
        public void AnObjectOrCopyACallMakesIsCharged(string src)
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src, Small));
            Assert.Equal("MaxAllocBytes", ex.LimitName);
        }

        // 150 classes of 35,000 fields took 968 MB with 0 charged: a class's slot and default tables are its own.
        [Fact]
        public void ClassTablesAreCharged()
        {
            var src = new StringBuilder("L = []\nfor i in range(150):\n    class C:\n");
            for (int i = 0; i < 3000; i++)
                src.Append("        a").Append(i).Append(": int\n");
            src.Append("    L.append(C)\n");
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src.ToString(), Small));
            Assert.Equal("MaxAllocBytes", ex.LimitName);
        }

        [Theory]
        [InlineData("from collections import namedtuple\nimport itertools\nP = namedtuple('P', 'a', defaults=itertools.repeat(0))\n")]
        [InlineData("from collections import namedtuple\nimport itertools\nP = namedtuple('P', itertools.repeat('a'))\n")]
        [InlineData("import csv, io, itertools\nw = csv.DictWriter(io.StringIO(), itertools.repeat('a'))\n")]
        [InlineData("import csv, io, itertools\nr = csv.DictReader(io.StringIO('a\\n'), fieldnames=itertools.repeat('a'))\nnext(r)\n")]
        [InlineData("import itertools\nx = ''.join(itertools.repeat('', 20000))\n")]
        [InlineData("import itertools\nx = b''.join(itertools.repeat(b'', 20000))\n")]
        [InlineData("from urllib.parse import urlencode\nimport itertools\nx = urlencode(itertools.repeat(('a', 'b')))\n")]
        public void AScriptIteratorDrainedByAModuleStopsAtTheItemCap(string src)
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src, Small));
            Assert.Equal("MaxCollectionItems", ex.LimitName);
        }

        [Fact]
        public void ACsvRowOfEndlessFieldsStopsAtTheStrCap()
        {
            const string src = "import csv, io, itertools\ncsv.writer(io.StringIO()).writerow(itertools.repeat(None))\n";
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src, Small));
            Assert.Equal("MaxStrChars", ex.LimitName);
        }

        [Fact]
        public void UrlunsplitOfAnEndlessIteratorIsAValueError()
        {
            const string src = "from urllib.parse import urlunsplit\nimport itertools\n"
                + "try:\n    urlunsplit(itertools.repeat('a'))\nexcept ValueError as e:\n    r = str(e)\n";
            Assert.Equal("'urlunsplit() takes a 5-item sequence (more given)'", Ev.Run(src).Repr("r"));
        }

        // The pool was filled by iterating and indexed by len(): a __len__ that says more than the iteration
        // gives read past the pool (an engine fault).
        [Fact]
        public void SampleOfALyingLenIsATypeError()
        {
            const string src = "import random\nclass S:\n    def __len__(self):\n        return 10\n"
                + "    def __iter__(self):\n        return iter([1, 2])\n"
                + "try:\n    random.sample(S(), 3)\nexcept TypeError as e:\n    r = 'TypeError'\n";
            Assert.Equal("'TypeError'", Ev.Run(src).Repr("r"));
        }

        // The same paths answer as before when nothing is near a limit.
        [Fact]
        public void TheChargedPathsAnswerAsBefore()
        {
            const string src = "import operator, functools, json, re, csv, io, itertools, dataclasses, random\n"
                + "from collections import namedtuple\nfrom urllib.parse import urlencode, urlunsplit\n"
                + "P = namedtuple('P', 'a b c', defaults=[2, 3])\n"
                + "w = io.StringIO()\ncsv.writer(w).writerow([1, None, 'x'])\n"
                + "r = (operator.itemgetter(1, 0)('ab'), operator.methodcaller('upper')('a'),\n"
                + "     sorted([3, 1, 2], key=functools.cmp_to_key(lambda a, b: b - a)), functools.lru_cache(maxsize=0)(len)('abc'),\n"
                + "     format('abcdef', '.3'), [m.group() for m in re.compile('a').finditer('aaxa', 0, 3)],\n"
                + "     json.dumps([[1, 2]], indent=1, separators=(';', '=')), P(1), P(1)._replace(b=5), P._fields,\n"
                + "     w.getvalue(), '-'.join(['a', '', 'b']), b'.'.join([b'x', b'y']), urlencode([('a', 1), ('b', 2)]),\n"
                + "     urlunsplit(('http', 'h', '/p', 'q', 'f')),\n"
                + "     len(random.sample([1, 2, 3, 4], 2)))\n";
            Assert.Equal("(('b', 'a'), 'A', [3, 2, 1], 3, 'abc', ['a', 'a'], '[\\n [\\n  1;\\n  2\\n ]\\n]', "
                + "P(a=1, b=2, c=3), P(a=1, b=5, c=3), ('a', 'b', 'c'), '1,,x\\r\\n', 'a--b', b'x.y', 'a=1&b=2', "
                + "'http://h/p?q#f', 2)", Ev.Run(src).Repr("r"));
        }
    }
}
