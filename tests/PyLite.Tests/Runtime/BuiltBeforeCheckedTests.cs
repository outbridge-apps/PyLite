using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // The 2026-10 sandbox review found outputs built in full before their size was checked, lists grown from
    // iterators with no charge or cap, and memory kept by matches, caches and work stacks with nothing
    // charged: the process took hundreds of MB to GB past what the budget counted. Each now ends on the limit
    // it would cross, before the memory is taken. Small limits keep every case fast.
    [Trait("Category", "ResourceLimits")]
    public class BuiltBeforeCheckedTests
    {
        private static readonly ResourceLimits Small = ResourceLimits.Default.With(
            maxStrChars: 1_000_000, maxCollectionItems: 10_000, maxAllocBytes: 32L << 20);

        [Theory]
        [InlineData("x = ascii(chr(0x100) * 200000)\n", "MaxStrChars")]
        [InlineData("x = '%a' % (chr(0x100) * 200000)\n", "MaxStrChars")]
        [InlineData("s = chr(0x100) * 200000\nx = f'{s!a}'\n", "MaxStrChars")]
        [InlineData("x = format(1.5, '.250000000g')\n", "MaxStrChars")]
        [InlineData("x = format(1.5, '.2000000000g')\n", "MaxStrChars")]
        [InlineData("print('', 'x', 'y', sep='z' * 600000)\n", "MaxStrChars")]
        [InlineData("import json\nx = json.dumps('\\u00e9' * 200000)\n", "MaxStrChars")]
        [InlineData("import json\nx = json.dumps(chr(1) * 200000)\n", "MaxStrChars")]
        [InlineData("import html\nx = html.escape(\"'\" * 200000)\n", "MaxStrChars")]
        [InlineData("from urllib.parse import urlencode\nx = urlencode({'a' * 200000: range(60)}, doseq=True)\n", "MaxStrChars")]
        [InlineData("import textwrap\nx = textwrap.wrap('a ' * 300, width=100002, initial_indent='x' * 100000, subsequent_indent='x' * 100000)\n", "MaxStrChars")]
        [InlineData("import textwrap\nx = textwrap.wrap('a\\tb', tabsize=2000000000)\n", "MaxStrChars")]
        [InlineData("s = 'a' * 100000\nx = ('{0:s}' * 40).format(s)\n", "MaxStrChars")]
        [InlineData("s = 'a' * 600000\nx = f'{s}{s}'\n", "MaxStrChars")]
        [InlineData("import calendar\nx = calendar.weekheader(2 ** 31 - 1)\n", "MaxStrChars")]
        [InlineData("from datetime import datetime, timezone, timedelta\ntz = timezone(timedelta(0), 'x' * 100000)\nx = datetime(2020, 1, 1, tzinfo=tz).strftime('%Z' * 30)\n", "MaxStrChars")]
        [InlineData("import xml.etree.ElementTree as ET\ne = ET.Element('a')\ne.text = '\\u4e00' * 200000\nx = ET.tostring(e)\n", "MaxStrChars")]
        public void AnOutputPastTheCapIsRefusedBeforeItIsBuilt(string src, string limit)
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src, Small));
            Assert.Equal(limit, ex.LimitName);
        }

        [Theory]
        [InlineData("import itertools\ndef f(*a):\n    pass\nf(*itertools.repeat(None))\n")]
        [InlineData("import itertools\ndef f(*a):\n    pass\nf(*itertools.repeat(None, 20000))\n")]
        [InlineData("import math, itertools\nx = math.dist(itertools.repeat(1.0), [1.0])\n")]
        [InlineData("import random, itertools\nx = random.choices([1, 2], weights=itertools.repeat(1.0))\n")]
        [InlineData("import itertools\nx = next(itertools.batched(itertools.repeat(0), 20000))\n")]
        [InlineData("from collections import namedtuple\nimport itertools\nT = namedtuple('T', 'a')\nx = T._make(itertools.repeat(0))\n")]
        [InlineData("import xml.etree.ElementTree as ET\nr = ET.Element('r')\nr.extend([ET.Element('a')] * 6000)\nr.extend([ET.Element('a')] * 6000)\n")]
        public void AListGrownFromAnIteratorStopsAtTheItemCap(string src)
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src, Small));
            Assert.Equal("MaxCollectionItems", ex.LimitName);
        }

        // P4: a tuple or list sized by one argument, not grown item by item.
        [Theory]
        [InlineData("import struct\nx = struct.unpack('0s' * 20000, b'')\n")]
        [InlineData("import itertools\nx = next(itertools.combinations_with_replacement([1], 20000))\n")]
        [InlineData("import itertools\nx = next(itertools.product([1], repeat=20000))\n")]
        [InlineData("x = []\nx[0:0] = range(6000)\nx[0:0] = range(6000)\n")]
        [InlineData("x = list(range(10000))\nx.insert(0, 1)\n")]
        public void ACollectionSizedInOneStepStopsAtTheItemCap(string src)
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src, Small));
            Assert.Equal("MaxCollectionItems", ex.LimitName);
        }

        [Fact]
        public void TheCappedCollectionsAnswerBelowTheCap()
        {
            string src = "import struct, itertools\n"
                + "a = struct.unpack('0s2s0s', b'ab')\n"
                + "b = list(itertools.combinations_with_replacement([], 20000))\n"
                + "c = next(itertools.product([1], repeat=3))\n"
                + "x = [1, 2]\nx[1:1] = [5, 6]\nx.insert(0, 0)\n"
                + "r = (a, b, c, x)\n";
            Assert.Equal("((b'', b'ab', b''), [], (1, 1, 1), [0, 1, 5, 6, 2])", Ev.Run(src, Small).Repr("r"));
        }

        [Theory]
        [InlineData("import re\np = re.compile('(' + '()' * 2000 + ')')\nkept = [m for m in p.finditer('a' * 4000)]\n")]
        [InlineData("import operator\np = '.' * 600000\ng = operator.attrgetter(p, p, p, p)\n")]   // 4.8 MB a path: the split array
        [InlineData("from urllib.parse import urljoin\nx = urljoin('http://h/a/b', '../' * 300000)\n")]
        [InlineData("import struct\nfor i in range(20):\n    struct.calcsize('b' * (200000 + i))\n")]
        public void MemoryAMatchCacheOrCopyKeepsIsCharged(string src)
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src, Small.With(maxAllocBytes: 16L << 20, maxCollectionItems: 5_000_000, regexPerOpTimeoutMs: 10000)));
            Assert.Equal("MaxAllocBytes", ex.LimitName);
        }

        [Fact]
        public void AQuotedCsvFieldIsLimitedAsItSpansLines()
        {
            string src = "import csv\ntry:\n    next(csv.reader(['\"'] + ['x' * 100000] * 5))\nexcept csv.Error as e:\n    r = str(e)\n";
            Assert.Equal("'field larger than field limit (131072)'", Ev.Run(src).Repr("r"));
        }

        // The same paths at ordinary sizes answer as before.
        [Fact]
        public void TheCheckedPathsAnswerAsBefore()
        {
            string src = "import json, html, textwrap, calendar, itertools, math, random, re, struct, operator\n"
                + "from collections import namedtuple\nfrom urllib.parse import urlencode, urljoin\n"
                + "T = namedtuple('T', 'a b')\nQ = namedtuple('Q', 'y')\nO = Q(T(5, 6))\n"
                + "def f(*a):\n    return len(a)\n"
                + "r = (ascii('é\\U0001F600x'), json.dumps(['é', {'k': [1, (2, 3)]}], sort_keys=True), html.escape('<\\'&\">'),"
                + " textwrap.wrap('a b c', width=3, initial_indent='>', subsequent_indent='-'), format(1.5, '.30g'),"
                + " calendar.weekheader(3)[:7], f(*range(5)), math.dist((0, 0), iter([3, 4])),"
                + " list(itertools.batched(range(5), 2)), T._make(iter([1, 2])), re.compile('b').search('abcb', 0, 3).span(),"
                + " struct.calcsize('ihh'), operator.attrgetter('y.a')(O), urljoin('http://h/a/b/c', '../../d'),"
                + " urlencode({'k': [1, 2]}, doseq=True), repr([[1, (2,)], 'x']), '{0}-{0}'.format('ab'))\n";
            Assert.Equal("(\"'\\\\xe9\\\\U0001f600x'\", '[\"\\\\u00e9\", {\"k\": [1, [2, 3]]}]', '&lt;&#x27;&amp;&quot;&gt;',"
                + " ['>a', '-b', '-c'], '1.5', 'Mon Tue', 5, 5.0, [(0, 1), (2, 3), (4,)], T(a=1, b=2), (1, 2),"
                + " 8, 5, 'http://h/d', 'k=1&k=2', \"[[1, (2,)], 'x']\", 'ab-ab')", Ev.Run(src).Repr("r"));
        }
    }
}
