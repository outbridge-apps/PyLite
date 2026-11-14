using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Modules.Support;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // The rows of review 13 the owner chose before 1.2.0 (24 to 28, 30, 35). An answer is CPython 3.14.2's; a cost is
    // proved by the memory limit the run fits and the build under review did not, or by the charge a held regex now carries, never by
    // timing. The same scripts at their full size are the bank's `r19_*` probes.
    [Trait("Category", "Regression")]
    public class Review13FixTests
    {
        private static RunResult Run(string source, long? maxAllocBytes = null, int? maxStrChars = null)
        {
            ResourceLimits limits = TestEngines.Unhurried.With(maxAllocBytes: maxAllocBytes, maxStrChars: maxStrChars);
            return new ScriptEngine(new EngineOptions { DefaultLimits = limits }).Run(source, new RunRequest());
        }

        private static string Out(string source, long? maxAllocBytes = null, int? maxStrChars = null)
        {
            RunResult r = Run(source, maxAllocBytes, maxStrChars);
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName + " " + r.Error.Message);
            return r.PrintOutput;
        }

        private static long Charged(string source, string expected, long maxAllocBytes)
        {
            RunResult r = Run(source, maxAllocBytes);
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName + " " + r.Error.Message);
            Assert.Equal(expected, r.PrintOutput);
            return r.Stats.AllocatedBytes;
        }

        // Row 24: a walk paid 12 B for each frame past its eighth, every walk. 300 lookups with a text predicate over
        // 300 rows 60 deep were 57 MB charged (1.1.0: 2 MB); the walks that end with their call are lent by the run.
        [Fact]
        public void ADeepTextPredicateLoopPaysItsFramesOnce()
        {
            string source = "import xml.etree.ElementTree as ET\nroot = ET.Element('r')\nfor i in range(300):\n"
                + "    n = ET.SubElement(root, 'row')\n    for d in range(60):\n        n = ET.SubElement(n, 'c')\n    n.text = str(i)\n"
                + "n = 0\nfor k in range(300):\n    n += len(root.findall(\"row[.='7']\"))\nprint(n)\n";
            long charged = Charged(source, "300\n", 16L << 20);
            Assert.True(charged < 4L << 20, "charged " + charged);
        }

        // The same for a descendant step and for iter() and itertext() that run to their end (13 and 16 MB before).
        [Theory]
        [InlineData("for d in range(60):\n    n = ET.SubElement(n, 'c')\nET.SubElement(n, 'leaf')\nk = 0\n"
            + "for i in range(20000):\n    k += root.find('.//leaf') is not None\nprint(k)\n", "20000\n", 4)]
        [InlineData("for d in range(40):\n    n = ET.SubElement(n, 'c'); n.text = 't'\nk = 0\n"
            + "for i in range(10000):\n    k += len(''.join(root.itertext())) + sum(1 for e in root.iter())\nprint(k)\n", "810000\n", 12)]
        public void ADeepWalkLoopPaysItsFramesOnce(string body, string expected, int underMb)
        {
            long charged = Charged("import xml.etree.ElementTree as ET\nroot = ET.Element('r'); n = root\n" + body, expected, 32L << 20);
            Assert.True(charged < (long)underMb << 20, "charged " + charged);
        }

        // A lent walk is no other walk's until it is given back: iterators interleaved, asked again after their end,
        // a search inside a tag's __eq__, a predicate whose join raises.
        [Fact]
        public void LentWalksAreNeverShared()
        {
            string source = "import xml.etree.ElementTree as ET\n"
                + "root = ET.fromstring('<r><a>1<b>2<c>3</c>t</b>u</a><a>4</a></r>')\n"
                + "it = root.iter(); print([e.tag for e in it], [e.tag for e in it])\n"
                + "tx = root.itertext(); print(list(tx), list(tx))\n"
                + "i1 = root.iter('a'); i2 = root.iter(); i3 = root.itertext()\n"
                + "print([(next(i1, None) is not None, getattr(next(i2, None), 'tag', '-'), next(i3, '-')) for k in range(7)])\n"
                + "aux = ET.fromstring('<q><a>1<b>2<c>3</c>t</b>u</a><a>4</a></q>')\n"
                + "class Odd:\n    def __eq__(self, o):\n"
                + "        return len(aux.findall('.//c')) == 1 and ''.join(aux.itertext()) == '123tu4' and o == 'zz'\n"
                + "    def __hash__(self):\n        return 1\n"
                + "x = ET.SubElement(root, Odd())\n"
                + "print(len(list(root.iter('zz'))), [e.tag for e in root.iter('a')])\n"
                + "print(len(root.findall(\".//b[.='23t']\")), len(root.findall(\"a[.='123tu']\")), root.findtext('a'))\n"
                + "e = ET.Element('e'); s = ET.SubElement(e, 's'); s.text = 5\n"
                + "for k in range(3):\n    try:\n        e.findall(\"s[.='5']\")\n    except TypeError as ex:\n        print(ex)\n"
                + "print(len(root.findall(\".//b[.='23t']\")))\n";
            Assert.Equal("['r', 'a', 'b', 'c', 'a'] []\n['1', '2', '3', 't', 'u', '4'] []\n"
                + "[(True, 'r', '1'), (True, 'a', '2'), (False, 'b', '3'), (False, 'c', 't'), (False, 'a', 'u'), (False, '-', '4'), (False, '-', '-')]\n"
                + "1 ['a', 'a']\n1 1 1\n"
                + "sequence item 0: expected str instance, int found\nsequence item 0: expected str instance, int found\n"
                + "sequence item 0: expected str instance, int found\n1\n", Out(source));
        }

        // Row 25: a group loop's stack is as long as the subject in each regex the pattern runs. Four rules used every
        // way over 200,000 characters were charged 12 MB for one stack each; three are 37.
        [Fact]
        public void EachRegexOfAPatternPaysItsOwnStack()
        {
            string head = "import re\ns = 'ab' * 100000\nkeep = []\nfor i in range(4):\n    c = re.compile(r'(?:ab|x%d)*$' % i)\n";
            long all = Charged(head + "    c.search(s); c.match(s); c.fullmatch(s)\n    keep.append(c)\nprint(len(keep))\n", "4\n", 256L << 20);
            long one = Charged(head + "    c.search(s)\n    keep.append(c)\nprint(len(keep))\n", "4\n", 256L << 20);
            Assert.True(all > 30L << 20, "all three: charged " + all);
            Assert.True(one < 16L << 20, "search only: charged " + one);
        }

        // Row 27: a regex that has run keeps a runner sized by its backtracking nodes. 300 kept patterns of 500
        // optional parts used every way were charged 6 MB and held 45; now 34. Compiled and never run, 7 MB: 18 B a
        // character outside a class held (review 14).
        [Fact]
        public void ARegexThatHasRunHoldsItsRunner()
        {
            string head = "import re\nkeep = []\nfor i in range(300):\n    c = re.compile('a?' * 500 + 'z%d' % i)\n";
            long used = Charged(head + "    s = 'z%d' % i\n    c.search(s); c.match(s); c.fullmatch(s)\n    keep.append(c)\nprint(len(keep))\n", "300\n", 256L << 20);
            long kept = Charged(head + "    keep.append(c)\nprint(len(keep))\n", "300\n", 256L << 20);
            Assert.True(used > 24L << 20, "used: charged " + used);
            Assert.True(kept < 10L << 20, "compiled only: charged " + kept);
        }

        // The runner's size as .NET Framework 4.8 keeps it: 512 B, 64 a quantifier (128 on a group), 128 a capturing or
        // atomic group or a branch, 256 a lookaround, 384 a conditional; (?:, inline options, comments, escaped and class
        // characters none.
        [Theory]
        [InlineData("abc", 512)]
        [InlineData("a?b*c+d{2}", 512 + 4 * 64)]
        [InlineData("(a)|(?:b)", 512 + 2 * 128)]
        [InlineData("(?:a)(?i)b(?i:c)(?-i:d)", 512)]
        [InlineData("(?=a)(?!b)(?<=c)(?<!d)", 512 + 4 * 256)]
        [InlineData("(?<n>a)(?'m'b)(?>c)", 512 + 3 * 128)]
        [InlineData("(x)(?(1)a|b)", 512 + 384 + 3 * 128)]
        [InlineData("(?#(|*)x", 512)]
        [InlineData("(?:ab|cd){2}", 512 + 128 + 128)]
        [InlineData("(?:ab)*a*", 512 + 128 + 64)]
        [InlineData(@"\(\?\*\|", 512)]
        [InlineData("[(?*|+{]x", 512)]
        [InlineData("[]?(]+", 512 + 64)]
        [InlineData("[^]?]?", 512 + 64)]
        public void RunnerBytesCountTheNodes(string netPattern, long expected)
        {
            Assert.Equal(expected, RegexFootprint.Runner(netPattern));
        }

        // Review 14: what a regex keeps, past 1,536 B and 2 B a character of its text: 6 a literal (an escaped
        // punctuation or \uXXXX too), 16 another token, 48 a branch, 16 a class and 8 a member, a long class once; a
        // literal run a match starts with pays 2 KB and 1 KB a page past ASCII, unless a branch around it offers
        // another start.
        [Theory]
        [InlineData("ab", 6 + 6)]
        [InlineData(@"a\-\d", 6 + 6 + 16)]
        [InlineData("a|b", 6 + 48 + 6)]
        [InlineData("(?:a)*", 16 + 6 + 6 + 16 + 16 + 16)]
        [InlineData("[]x]a[^]]", 16 + 16 + 6 + 16 + 16)]
        [InlineData("[abc]", 16 + 24)]
        [InlineData("\u0430\u0431", 6 + 6 + 2048 + 1024)]
        [InlineData("\u0430\u0531", 6 + 6 + 2048 + 2048)]
        [InlineData(@"\u0430", 6 + 2048 + 1024)]
        [InlineData("^(?:\\b\u0430)", 16 + 16 + 16 + 6 + 16 + 6 + 16 + 2048 + 1024)]
        [InlineData("\u0430|x", 6 + 48 + 6)]
        [InlineData("(\u0430|x)", 16 + 6 + 48 + 6 + 16)]
        [InlineData("(?:\u0430)?", 16 + 16 + 6 + 6 + 16 + 16)]
        [InlineData("(\u0430)(x|y)", 16 + 6 + 16 + 16 + 6 + 48 + 6 + 16 + 2048 + 1024)]
        [InlineData("x*\u0430", 6 + 16 + 6)]
        [InlineData(@"a\", 6 + 16)]
        [InlineData("[ab", 16 + 16)]
        public void AHeldRegexIsReadOffItsTokens(string netPattern, long code)
        {
            Assert.Equal(1536 + 2 * netPattern.Length + code, RegexFootprint.Held(netPattern));
        }

        [Fact]
        public void ALongClassIsKeptOnce()
        {
            string cls = "[" + new string('b', 40) + "]";
            Assert.Equal(1536 + 2 * 84 + 16 + 320 + 16, RegexFootprint.Held(cls + cls));
        }

        // Review 14: a pattern is paid while the CLR keeps it, not again by every compile. 300 rows each building a
        // 500-word alternation, run once and purged, were 19 MB past the strings that build them; now 7, the 64
        // patterns the cache waits for before it sweeps.
        [Fact]
        public void ACompiledAndDroppedPatternIsNotPaidPerCompile()
        {
            string head = "import re\nwords = ['w%d' % i for i in range(1000)]\nn = 0\nfor row in range(300):\n"
                + "    src = r'\\b(?:' + '|'.join(words[row: row + 500]) + r')\\b'\n";
            long strings = Charged(head + "    n += len(src)\nprint(n > 0)\n", "True\n", 256L << 20);
            long compiled = Charged(head + "    n += len(re.compile(src).findall('w5 w400 x'))\n    re.purge()\nprint(n > 0)\n",
                "True\n", 256L << 20);
            Assert.True(compiled - strings < 10L << 20, "compiled and dropped: charged " + (compiled - strings));
        }

        // A regex that cannot be held is refused before .NET builds it: the exact translation of 2,000 Unicode \b holds
        // 4 MB, refused under 2 MB, compiled under 40 as 1.1.0 compiled it.
        [Fact]
        public void ARegexThatCannotBeHeldIsRefused()
        {
            const string Source = "import re\nprint(re.compile(r'\\b' * 2000).search('a' + chr(0x301)) is not None)\n";
            RunResult r = Run(Source, 2L << 20);
            Assert.False(r.Succeeded);
            Assert.Equal("MaxAllocBytes", r.Error.LimitName);
            Assert.Equal("True\n", Out(Source, 40L << 20));
        }

        private const string Stops = "import itertools\n"
            + "class I:\n    def __iter__(self):\n        raise StopIteration('iter')\n"
            + "class V:\n    def __iter__(self):\n        raise ValueError('v')\n"
            + "def again(*src):\n    c = itertools.chain(*src)\n    return (list(c), list(c))\n"
            + "def value():\n    c = itertools.chain([1], V(), [2])\n    out = [next(c)]\n    try:\n        next(c)\n"
            + "    except ValueError:\n        out.append('VE')\n    return out + [list(c)]\n"
            + "def t(fn):\n    try:\n        print(fn())\n    except BaseException as e:\n        print(type(e).__name__, e)\n";

        // Row 26: a StopIteration out of a member's __iter__ ends the chain, which then stays ended, as CPython's
        // chain drops its source on any error there. The build under review raised it out of filter(None, chain(...)).
        [Theory]
        [InlineData("list(itertools.chain([1], I(), [2]))", "[1]")]
        [InlineData("list(filter(None, itertools.chain([1], I(), [2])))", "[1]")]
        [InlineData("list(itertools.filterfalse(None, itertools.chain([0], I(), [2])))", "[0]")]
        [InlineData("list(itertools.chain.from_iterable([[1], I(), [2]]))", "[1]")]
        [InlineData("list(itertools.chain(itertools.chain([1], I()), [5]))", "[1, 5]")]
        [InlineData("again([1], I(), [2])", "([1], [])")]
        [InlineData("value()", "[1, 'VE', []]")]
        [InlineData("next(itertools.chain(I(), [2]))", "StopIteration iter")]
        [InlineData("list(itertools.chain([1], V(), [2]))", "ValueError v")]
        public void AChainEndsOnAStopIterationFromAMembersIter(string expression, string expected)
        {
            Assert.Equal(expected + "\n", Out(Stops + "t(lambda: " + expression + ")\n"));
        }

        private const string Resuming = "import itertools, heapq, csv\n"
            + "class R:\n    def __init__(s, tup=False):\n        s.i = 0; s.tup = tup\n    def __iter__(s):\n        return s\n"
            + "    def __next__(s):\n        s.i += 1\n        if s.i == 3 or s.i == 6 or s.i > 8:\n            raise StopIteration\n"
            + "        return (s.i, s.i) if s.tup else s.i\n"
            + "def rs(f, tup=False):\n    it = f(R(tup))\n    return (list(it), list(it), list(it))\n";

        // Row 30: an iterator of the script that ends and then yields again is asked again by what CPython asks again,
        // and not by what drops its source at its end (islice, pairwise, chain, batched, zip_longest, a merge).
        [Theory]
        [InlineData("filter(None, r)", "([1, 2], [4, 5], [7, 8])")]
        [InlineData("map(str, r)", "(['1', '2'], ['4', '5'], ['7', '8'])")]
        [InlineData("zip(r, itertools.count())", "([(1, 0), (2, 1)], [(4, 2), (5, 3)], [(7, 4), (8, 5)])")]
        [InlineData("enumerate(r, 5)", "([(5, 1), (6, 2)], [(7, 4), (8, 5)], [(9, 7), (10, 8)])")]
        [InlineData("iter(r)", "([1, 2], [4, 5], [7, 8])")]
        [InlineData("itertools.takewhile(lambda x: True, r)", "([1, 2], [4, 5], [7, 8])")]
        [InlineData("itertools.dropwhile(lambda x: x < 2, r)", "([2], [4, 5], [7, 8])")]
        [InlineData("itertools.compress(itertools.count(), r)", "([0, 1], [3, 4], [6, 7])")]
        [InlineData("itertools.accumulate(r, initial=0)", "([0, 1, 3], [7, 12], [19, 27])")]
        [InlineData("itertools.filterfalse(lambda x: x > 100, r)", "([1, 2], [4, 5], [7, 8])")]
        [InlineData("map(lambda kg: kg[0], itertools.groupby(r))", "([1, 2], [4, 5], [7, 8])")]
        [InlineData("csv.reader(map(str, r))", "([['1'], ['2']], [['4'], ['5']], [['7'], ['8']])")]
        [InlineData("itertools.islice(r, 10)", "([1, 2], [], [])")]
        [InlineData("itertools.islice(r, 1, None)", "([2], [], [])")]
        [InlineData("itertools.islice(r, 0, 4, 2)", "([1], [], [])")]
        [InlineData("itertools.pairwise(r)", "([(1, 2)], [], [])")]
        [InlineData("itertools.chain([0], r)", "([0, 1, 2], [], [])")]
        [InlineData("itertools.batched(r, 1)", "([(1,), (2,)], [], [])")]
        [InlineData("itertools.zip_longest(r)", "([(1,), (2,)], [], [])")]
        [InlineData("heapq.merge(r)", "([1, 2], [], [])")]
        public void AResumingIteratorIsAskedAgainAsCPythonAsks(string wrap, string expected)
        {
            Assert.Equal(expected + "\n", Out(Resuming + "print(rs(lambda r: " + wrap + "))\n"));
        }

        // What the fix review found: chain, islice and pairwise drop their source when it ends or raises, as CPython's do;
        // batched ends only on an empty batch, a short one asks again.
        [Fact]
        public void WhatDropsItsSourceDropsItOnAnErrorToo()
        {
            string source = "import itertools\n"
                + "class R:\n    def __init__(self, data):\n        self.data = list(data); self.calls = 0\n"
                + "    def __iter__(self):\n        return self\n    def __next__(self):\n        self.calls += 1\n"
                + "        x = self.data.pop(0) if self.data else None\n        if x is None:\n            raise StopIteration\n"
                + "        if x == 'err':\n            raise ValueError('boom')\n        return x\n"
                + "r = R([[1], [2], None, [3]]); c = itertools.chain.from_iterable(r)\nprint(list(c), list(c), r.calls)\n"
                + "r = R([[1], 'err', [3]]); c = itertools.chain.from_iterable(r)\n"
                + "try:\n    list(c)\nexcept ValueError as e:\n    print('err', e)\nprint(list(c), r.calls)\n"
                + "r = R([1, 2, None, 3]); c = itertools.chain.from_iterable(map(lambda x: [x], r))\nprint(list(c), list(c), r.calls)\n"
                + "for nm, mk in ((\"islice\", lambda r: itertools.islice(r, 5)), (\"pairwise\", lambda r: itertools.pairwise(r))):\n"
                + "    r = R([1, 'err', 2, 3]); it = mk(r)\n    try:\n        print(nm, list(it))\n    except ValueError:\n"
                + "        print(nm, 'raised')\n    print(nm, list(it), r.calls)\n"
                + "r = R([1, 2, None, 3]); b = itertools.batched(r, 3)\nprint('batched', list(b), list(b), r.calls)\n";
            Assert.Equal("[1, 2] [] 3\nerr boom\n[] 2\n[1, 2] [] 3\nislice raised\nislice [] 2\npairwise raised\npairwise [] 2\n"
                + "batched [(1, 2), (3,)] [] 6\n", Out(source));
        }

        [Fact]
        public void AResumingIteratorThroughTheOtherConsumers()
        {
            string source = Resuming
                + "print(rs(lambda r: itertools.starmap(lambda a, b: a + b, r), True))\n"
                + "a, b = itertools.tee(R())\nprint(list(a), list(b), list(a), list(b))\n"
                + "r = R()\nprint(sum(r), sum(r), sum(r), sum(r))\n"
                + "r = R(); f = filter(None, r)\nprint([next(f, '-') for _ in range(10)])\n"
                + "r = R(); f = map(lambda x: x, r)\nprint(2 in f, 2 in f, 5 in f, 7 in f)\n"
                + "r = R(); f = filter(None, r)\na, b = f\nprint(a, b, list(f))\n"
                + "print(list(itertools.zip_longest(R(), 'abcd')))\n"
                + "print(list(itertools.islice(itertools.cycle(R()), 7)))\n";
            Assert.Equal("([2, 4], [8, 10], [14, 16])\n[1, 2] [1, 2, 4, 5] [4, 5, 7, 8] [7, 8]\n3 9 15 0\n"
                + "[1, 2, '-', 4, 5, '-', 7, 8, '-', '-']\nTrue False True False\n1 2 [4, 5]\n"
                + "[(1, 'a'), (2, 'b'), (None, 'c'), (None, 'd')]\n[1, 2, 1, 2, 1, 2, 1]\n", Out(source));
        }

        private const string Spaced = "import xml.etree.ElementTree as ET\n"
            + "r = ET.fromstring('<r><a><b>1</b></a><a v=\"x\"><b>2</b></a><b>3</b></r>')\n"
            + "e0 = ET.SubElement(r, ''); ET.SubElement(e0, 'b').text = '4'\nET.SubElement(r[0], '')\n"
            + "def show(p):\n"
            + "    res = [x.tag + ':' + str(x.text) for x in r.iterfind(p)]\n"
            + "    fa = [x.tag for x in r.findall(p, {'q': 'u'})]\n"
            + "    f = r.find(p)\n"
            + "    print(res, fa, None if f is None else repr(f.tag))\n";

        // Row 35: ElementPath's tokenizer reads whitespace outside a predicate or a {uri} as a step of its own, the
        // children tagged '' ('a b' is a, then '', then b); inside a predicate it is skipped. find() without a map
        // compares a name with no path character as a tag, whitespace and all.
        [Theory]
        [InlineData(" ", "[':None'] [''] None")]
        [InlineData("\\t", "[':None'] [''] None")]
        [InlineData("a b", "[] [] None")]
        [InlineData(" b", "['b:4'] ['b'] None")]
        [InlineData("b ", "[] [] None")]
        [InlineData("a/ b", "[] [] None")]
        [InlineData("a / b", "[] [] None")]
        [InlineData(" .//b", "['b:4'] ['b'] 'b'")]
        [InlineData("a\\n", "[':None'] [''] None")]
        [InlineData(". ", "[':None'] [''] ''")]
        [InlineData(" .", "[':None'] [''] ''")]
        [InlineData("a/ ", "[':None'] [''] ''")]
        [InlineData(" /b", "['b:4'] ['b'] 'b'")]
        [InlineData("a[ b ]", "['a:None', 'a:None'] ['a', 'a'] 'a'")]
        [InlineData("a[ @v = 'x' ]", "['a:None'] ['a'] 'a'")]
        [InlineData("*[ b ]", "['a:None', 'a:None', ':None'] ['a', 'a', ''] 'a'")]
        [InlineData("{u}a b", "[] [] None")]
        [InlineData("a [1]b", "[] [] None")]
        public void WhitespaceInAPathIsAStepOfItsOwn(string path, string expected)
        {
            Assert.Equal(expected + "\n", Out(Spaced + "show(\"" + path + "\")\n"));
        }

        // What the fix review found: a brace protects whitespace only where it opens a name ('a{ b' is a tag to find,
        // steps to iterfind), a '{}' or '{*}' prefix is a wildcard path, and a name after a predicate is a step.
        [Fact]
        public void BracesAndPredicatesAroundWhitespaceReadAsCPythonReadsThem()
        {
            string source = "import xml.etree.ElementTree as ET\nr = ET.Element('r')\n"
                + "for t in ['a{ b', '{ab c', 'x{u v}y', 'a', 'q']:\n    ET.SubElement(r, t)\n"
                + "a = r[3]; e = ET.SubElement(a, ''); ET.SubElement(e, 'b')\nET.SubElement(r[0], 'zz')\n"
                + "for p in ['a{ b', '{ab c', 'x{u v}y', 'a [1]b', '{}a b', '{*}a b', 'a{ b/zz', './a{ b']:\n"
                + "    f = r.find(p)\n"
                + "    print([x.tag for x in r.findall(p)], [x.tag for x in r.iterfind(p)], None if f is None else f.tag)\n";
            Assert.Equal("['a{ b'] [] a{ b\n['{ab c'] [] {ab c\n['x{u v}y'] [] x{u v}y\n['b'] ['b'] b\n['b'] ['b'] b\n"
                + "['b'] ['b'] b\n[] [] None\n[] [] None\n", Out(source));
        }

        // Row 28: a long piece past the string limit is refused before it is composed, and only then: composed, a piece
        // keeps every code point that is no second. A Hangul syllable decomposes to two and composes back to one, so
        // NFKC of 2,000 of them after U+FB01 (one piece, 4,002 decomposed) fits a limit of 2,002; U+FDFA's 18 stay 18.
        [Theory]
        [InlineData("chr(0xfb01) + chr(0xac00) * 2000", 2002)]
        [InlineData("chr(0xfdfa) * 200", 3600)]
        public void ANormalFormAtTheLimitFitsAndOnePastIsRefused(string text, int length)
        {
            string source = "import unicodedata\ns = " + text + "\nprint(len(unicodedata.normalize('NFKC', s)))\n";
            Assert.Equal(length + "\n", Out(source, null, length));
            RunResult r = Run(source, null, length - 1);
            Assert.False(r.Succeeded, "the run was not refused");
            Assert.Equal("MaxStrChars", r.Error.LimitName);
        }
    }
}
