using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // The rows reviews 9 and 10 left to the owner and the owner chose before 1.2.0. Each answer is CPython 3.14.2's (the
    // ssize_t ones by its source: the oracle is a 32-bit build); a cost is proved by the step or memory limit the run
    // fits and the build under review did not, never by timing. The wider answers are the `Oracle/r10b_*` scripts.
    [Trait("Category", "Regression")]
    public class Review10LeftoverFixTests
    {
        private static RunResult Run(string source, long? maxAllocBytes = null, long? maxSteps = null)
        {
            ResourceLimits limits = TestEngines.Unhurried.With(maxAllocBytes: maxAllocBytes, maxSteps: maxSteps);
            return new ScriptEngine(new EngineOptions { DefaultLimits = limits }).Run(source, new RunRequest());
        }

        private static string Out(string source, long? maxAllocBytes = null, long? maxSteps = null)
        {
            RunResult r = Run(source, maxAllocBytes, maxSteps);
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName + " " + r.Error.Message);
            return r.PrintOutput;
        }

        private const string Tree = "import xml.etree.ElementTree as ET\ndoc = ET.Element('doc')\ndraft = ET.SubElement(doc, 'draft')\n"
            + "table = ET.SubElement(doc, 'table')\nother = ET.Element('other')\nbad = 0\n";

        // Review 10 A-3 and review 9's `..` and `*[last()]` rows: add a row, search, 6,000 times. An appended leaf is one
        // entry more in the kept parent map, and `*` over rows of one tag is that tag's step: 2.3M steps (4.6M for the two
        // `*` searches), where the build under review took 40M to 56M and 1.1.0 18M to 36M.
        [Theory]
        [InlineData("lazy", "next(table.iterfind('row[last()]')) is not row", 6_000_000)]
        [InlineData("parent", "table.find('row[last()]/..') is not table", 6_000_000)]
        [InlineData("star", "table.find('*[last()]') is not row or table.find('*[1]') is not table[0]", 10_000_000)]
        public void TheAddARowLoopBuildsNoMapPerSearch(string name, string wrong, int maxSteps)
        {
            Assert.Equal("0\n", Out(Tree + "for i in range(6000):\n    row = ET.SubElement(table, 'row', n='r%d' % i)\n    if " + wrong
                + ":\n        bad += 1\nprint(bad)\n", 16L << 20, maxSteps));
            Assert.NotNull(name);
        }

        // Review 9: a change to any tree dropped every tree's parent map. A map is dropped by a change under its own root
        // now: 2,000 changes to another tree between `..` searches of 20,000 rows are 5M steps (the build under review 45M, 1.1.0 80M).
        [Theory]
        [InlineData("append", "ET.SubElement(other, 'x')\n    other.append(ET.Element('y'))")]
        [InlineData("insert_and_delete", "del other[:]\n    other.insert(0, ET.Element('y'))")]
        public void AChangeToAnotherTreeKeepsThisTreesMap(string name, string change)
        {
            Assert.Equal("0\n", Out(Tree + "for i in range(20000):\n    ET.SubElement(table, 'row')\nfor i in range(2000):\n    " + change
                + "\n    if doc.find('table/row[1]/..') is not table:\n        bad += 1\nprint(bad)\n", 16L << 20, 12_000_000));
            Assert.NotNull(name);
        }

        // Review 10 C-7 and C-8, review 9 C-7: sizes up to ssize_t's 2**63 - 1 are sizes, not errors at 2**31.
        [Fact]
        public void SizesAboveTwoToThe31AreTaken()
        {
            const string Expected = "(1, 2, 3) True [3, 1, 2] [2, 3, 1]\n(1, 2, 3) True [2, 3, 1] [3, 1, 2]\n(1, 2, 3) True [3, 1, 2] [2, 3, 1]\n"
                + "(1, 2, 3) True [3, 1, 2] [2, 3, 1]\nOverflowError OverflowError OverflowError [3, 1, 2]\n5000\n";
            Assert.Equal(Expected, Out("import itertools\nfrom collections import deque, Counter\n"
                + "def t(f):\n    try:\n        return f()\n    except Exception as e:\n        return type(e).__name__\n"
                + "def rot(n):\n    d = deque([1, 2, 3])\n    d.rotate(n)\n    return list(d)\n"
                + "for n in (2 ** 31 - 1, 2 ** 31, 2 ** 32, 2 ** 63 - 1, 2 ** 63):\n"
                + "    print(t(lambda: next(itertools.batched([1, 2, 3], n))), t(lambda: deque([1, 2], maxlen=n).maxlen == n), "
                + "t(lambda: rot(n)), t(lambda: rot(-n)))\n"
                + "print(len(list(itertools.islice(Counter({'a': 2 ** 31}).elements(), 5000))))\n"));
        }

        // Review 9 C-9: lcm divides the shorter operand by the gcd, either way round (the answers; the cost was time alone).
        [Fact]
        public void LcmOfALongAndAShortNumber()
        {
            Assert.Equal("2003004 202144000 0 True\n", Out("import math\nbig = math.factorial(2500)\ns = 0\nfor i in range(1, 2000):\n"
                + "    s += math.gcd(big, i) + (math.lcm(big, i) & 1) + (math.lcm(i, big) & 1) + math.lcm(i, i + 1, i + 2) % 7\n"
                + "print(s, math.lcm(*range(1, 300)) % 10 ** 9, math.lcm(big, 2 ** 9000 + 1) % 10 ** 9, "
                + "math.lcm(2 ** 9000 + 1, big) == math.lcm(big, 2 ** 9000 + 1))\n"));
        }

        // Review 9 C-5: a comment's and a PI's text are part of itertext (CPython's C iterator reads no tag), and a
        // position on a child whose tag is not a str fails as CPython's findall(tag) does.
        [Fact]
        public void NodesThatAreNotElementsInTextAndPositions()
        {
            const string Expected = "[' note ', 'T0', 'php echo', 'T1', 'v'] [' note '] ['php echo'] b' note T0php echoT1v'\n"
                + "TypeError: 'function' object is not subscriptable\n1\n"
                + "5 | TypeError: 'int' object is not subscriptable | 1 | 1\n"
                + "None | TypeError: 'NoneType' object is not subscriptable | 1 | 1\n"
                + "{} | 2 | 1 | 1\nb'c' | 3 | 1 | 1\n"
                + "b'a/b' | TypeError: cannot use a string pattern on a bytes-like object | 1 | 1\n"
                + "(1, 2) | TypeError: expected string or bytes-like object, got 'tuple' | 1 | 1\n";
            Assert.Equal(Expected, Out("import xml.etree.ElementTree as ET\nr = ET.Element('r')\nr.append(ET.Comment(' note '))\n"
                + "r.append(ET.ProcessingInstruction('php', 'echo'))\nET.SubElement(r, 'x').text = 'v'\nr[0].tail = 'T0'\nr[1].tail = 'T1'\n"
                + "print(list(r.itertext()), list(r[0].itertext()), list(r[1].itertext()), ET.tostring(r, method='text'))\n"
                + "def t(f):\n    try:\n        return f()\n    except Exception as e:\n        return type(e).__name__ + ': ' + str(e)\n"
                + "def tagged(tag):\n    e = ET.fromstring('<r><c/><c/><d/></r>')\n    e[0].tag = tag\n    return e\n"
                + "print(t(lambda: len(r.findall('*[1]'))))\nprint(t(lambda: len(r.findall('x[1]'))))\n"
                + "for tag in (5, None, {}, b'c', b'a/b', (1, 2)):\n"
                + "    print(repr(tag), '|', t(lambda: len(tagged(tag).findall('*[1]'))), '|', t(lambda: len(tagged(tag).findall('c[last()]'))), "
                + "'|', t(lambda: len(tagged(tag).findall('d[1]'))))\n"));
        }

        // Found beside C-5 (wrong in 1.1.0 too): [@a!='v'] matches an element that has the attribute with another value,
        // not one without it.
        [Fact]
        public void AttributeNotEqualNeedsTheAttribute()
        {
            Assert.Equal("[[0], [0], [2, 3], [0], [2, 3], [3], [], [0, 1, 2, 3, 4], [0]]\n", Out("import xml.etree.ElementTree as ET\n"
                + "r = ET.fromstring('<r><b x=\"2\"/><b/><b x=\"1\"/><b x=\"1\" y=\"3\"/><b y=\"1\"/></r>')\n"
                + "def n(p):\n    return [list(r).index(e) for e in r.findall(p)]\n"
                + "print([n('b[@x!=\"1\"]'), n(\"b[@x!='1']\"), n('b[@x=\"1\"]'), n(\"b[@x != '1']\"), n('b[@x!=\"2\"]'), n('b[@y!=\"1\"]'), "
                + "n('b[x!=\"1\"]'), n('b[.!=\"1\"]'), n('*[@x!=\"1\"]')])\n"));
        }

        // The review of this batch: `*[N]` over 12,000 children of one tag and then 12,000 of others. That the children
        // are not one tag's is remembered for the node (260K steps; counted again per child it was 36M).
        [Fact]
        public void AStarPositionOverARunOfOneTagAndThenOthers()
        {
            Assert.Equal("12001 12001 1\n", Out("import xml.etree.ElementTree as ET\nroot = ET.Element('root')\n"
                + "for i in range(12000):\n    ET.SubElement(root, 'a')\nfor i in range(12000):\n    ET.SubElement(root, 't%d' % i)\n"
                + "print(len(root.findall('*[1]')), len(root.findall('*[last()]')), len(list(root.iterfind('*[2]'))))\n", 32L << 20, 1_000_000));
        }

        // The same review: a child counted for `*[1]` is not taken as the first again once a rename (which no version
        // counts) has put another before it; the wider cases are Oracle/r10b_review.
        [Fact]
        public void ARenameBetweenTheItemsOfALazySearch()
        {
            Assert.Equal("['x0', None]\n", Out("import xml.etree.ElementTree as ET\nroot = ET.Element('root')\n"
                + "x0 = ET.SubElement(root, 'x', {'id': 'x0'})\nET.SubElement(root, 'a', {'id': 'a1'})\n"
                + "it = root.iterfind('a[1]/..//*[1]')\nr = [next(it).get('id')]\nx0.tag = 'a'\ne = next(it, None)\n"
                + "r.append(None if e is None else e.get('id'))\nprint(r)\n"));
        }

        // The same review: gzip checks the level before the mtime, as CPython does; mtime=None stays 0 (docs/deviations.md).
        [Fact]
        public void GzipChecksTheLevelBeforeTheMtime()
        {
            Assert.Equal("Bad compression level | Bad compression level | 'L' format requires 0 <= number <= 4294967295 | 0 0\n",
                Out("import gzip\ndef t(f):\n    try:\n        return f()\n    except Exception as e:\n        return str(e)\n"
                + "print(t(lambda: gzip.compress(b'abc', compresslevel=10, mtime=-1)), '|', t(lambda: gzip.compress(b'abc', 10, mtime='x')), '|', "
                + "t(lambda: gzip.compress(b'abc', mtime=-1)), '|', int.from_bytes(gzip.compress(b'abc', mtime=None)[4:8], 'little'), "
                + "int.from_bytes(gzip.compress(b'abc')[4:8], 'little'))\n"));
        }

        // The regression review of that fix: four trees of 60,000, each changed and searched with `..` in turn, kept
        // all four maps held (45 MB charged; before that fix and now 37 MB). A build drops the maps kept across changes.
        [Fact]
        public void FourTreesChangedAndSearchedInTurnHoldOneMap()
        {
            Assert.Equal("12\n", Out("import xml.etree.ElementTree as ET\ndocs = []\nfor d in range(4):\n    r = ET.Element('r')\n"
                + "    for i in range(60000):\n        ET.SubElement(r, 'x')\n    docs.append(r)\nn = 0\nfor rnd in range(3):\n"
                + "    for r in docs:\n        del r[0]\n        if r.find('x/..') is r:\n            n += 1\nprint(n)\n", 41L << 20));
        }

        // The same review: f(**obj) as CPython's _PyEval_FormatKwargsError words it, and Element's positional attrib
        // checked before an attrib keyword (which after a dict is an attribute named 'attrib').
        [Fact]
        public void KeywordMappingAndAttribArgumentErrors()
        {
            const string Expected = "f() got multiple values for keyword argument 'a' | f() got multiple values for keyword argument '5' | "
                + "KeyError | M.keys() returned a non-iterable (type int)\n"
                + "Element() argument 2 must be dict, not None | SubElement() argument 3 must be dict, not None | "
                + "SubElement() argument 3 must be dict, not int | {'attrib': {'x': '1'}} | {'k': 'v', 'attrib': {}}\n"
                + "f() got multiple values for keyword argument 'z' | K.keys() returned a non-iterable (type BadIt)\n";
            Assert.Equal(Expected, Out("import xml.etree.ElementTree as ET\n"
                + "def t(f):\n    try:\n        return f()\n    except Exception as e:\n        return str(e) if type(e) is TypeError else type(e).__name__\n"
                + "def f(**k):\n    return k\nclass M:\n    def __init__(self, keys, exc):\n        self.k, self.e = keys, exc\n"
                + "    def keys(self):\n        return self.k\n    def __getitem__(self, k):\n        raise self.e\n"
                + "print(t(lambda: f(**M(['a'], KeyError('a')))), '|', t(lambda: f(**M(['a'], KeyError(5)))), '|', "
                + "t(lambda: f(**M(['a'], KeyError()))), '|', t(lambda: f(**M(5, None))))\np = ET.Element('p')\n"
                + "print(t(lambda: ET.Element('a', None, attrib={'x': '1'})), '|', t(lambda: ET.SubElement(p, 'a', None, attrib={})), '|', "
                + "t(lambda: ET.SubElement(p, 'a', 5, attrib={})), '|', ET.Element('a', {}, attrib={'x': '1'}).attrib, '|', "
                + "ET.SubElement(p, 'a', {'k': 'v'}, attrib={}).attrib)\n"
                + "class BadIt:\n    def __iter__(self):\n        raise TypeError('no iter')\n"
                + "class K:\n    def __init__(self, k):\n        self.k = k\n    def keys(self):\n        if self.k is None:\n"
                + "            raise KeyError('z')\n        return self.k\n    def __getitem__(self, k):\n        return 1\n"
                + "print(t(lambda: f(**K(None))), '|', t(lambda: f(**K(BadIt()))))\n"));
        }
    }
}
