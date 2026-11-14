using System.Text;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // The rows of review 12 the owner chose before 1.2.0 (5, 6, 12 to 19). An answer is CPython 3.14.2's; a cost is
    // proved by the step or memory limit the run fits and the build under review did not, never by timing. The same scripts at their
    // full size are the bank's `r17_*` probes.
    [Trait("Category", "Regression")]
    public class Review12FixTests
    {
        private static RunResult Run(string source, long? maxAllocBytes = null, long? maxSteps = null, int? maxStrChars = null)
        {
            ResourceLimits limits = TestEngines.Unhurried.With(maxAllocBytes: maxAllocBytes, maxSteps: maxSteps, maxStrChars: maxStrChars);
            return new ScriptEngine(new EngineOptions { DefaultLimits = limits }).Run(source, new RunRequest());
        }

        private static string Out(string source, long? maxAllocBytes = null, long? maxSteps = null, int? maxStrChars = null)
        {
            RunResult r = Run(source, maxAllocBytes, maxSteps, maxStrChars);
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName + " " + r.Error.Message);
            return r.PrintOutput;
        }

        private static string Limit(string source, long? maxAllocBytes = null, long? maxSteps = null, int? maxStrChars = null)
        {
            RunResult r = Run(source, maxAllocBytes, maxSteps, maxStrChars);
            Assert.False(r.Succeeded, "the run was not refused");
            return r.Error.LimitName;
        }

        private const string Rows = "import xml.etree.ElementTree as ET\nroot = ET.Element('R')\nfor i in range(1500):\n"
            + "    r = ET.SubElement(root, 'row', id=str(i))\n    ET.SubElement(r, 'v').text = str(i)\nn = 0\n";

        // Row 12: a text predicate walked each candidate's subtree with a paid frame, 96 bytes a candidate that the
        // budget never got back: 1,000 lookups over 1,500 rows were 18 MB charged (1.1.0 and now: under 2 MB).
        [Fact]
        public void ATextPredicateLookupLoopChargesNothingPerCandidate()
        {
            RunResult r = Run(Rows + "for i in range(1000):\n    n += len(root.findall(\"row[v='%d']\" % i))\nprint(n)\n", 8L << 20);
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName);
            Assert.Equal("1000\n", r.PrintOutput);
            Assert.True(r.Stats.AllocatedBytes < 3L << 20, "charged " + r.Stats.AllocatedBytes);
        }

        // Row 15: itertext() and iter() of a small element paid for their first frames too. 150,000 walks of a row
        // with one child are 12.7 MB charged, not 15.6.
        [Theory]
        [InlineData("for t in r.itertext()", "150000\n", 14)]
        [InlineData("for e in r.iter()", "300000\n", 19)]
        public void AWalkOfASmallElementPaysNoFrames(string loop, string expected, int underMb)
        {
            RunResult r = Run(Rows + "for k in range(100):\n    for r in root:\n        " + loop + ":\n            n += 1\nprint(n)\n");
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName);
            Assert.Equal(expected, r.PrintOutput);
            Assert.True(r.Stats.AllocatedBytes < (long)underMb << 20, "charged " + r.Stats.AllocatedBytes);
        }

        private const string Accents = "import unicodedata as u\ns = (chr(0xe9) * 9 + ' ') * 6000\nt = s[:-2] + 'e' + chr(0x301)\n";

        // Row 13: the composed forms were built from the whole decomposed text, and the string limit was read on that:
        // NFC of a 60,000-character text was refused at MaxStrChars = 100,000 because its NFD is 114,000.
        [Fact]
        public void AComposedFormIsLimitedByItsOwnLength()
        {
            Assert.Equal("59999 True\n59999 True\nFalse False\nTrue True\n", Out(Accents
                + "print(len(u.normalize('NFC', t)), u.normalize('NFC', t) == s[:-1])\n"
                + "print(len(u.normalize('NFKC', t)), u.normalize('NFKC', t) == s[:-1])\n"
                + "print(u.is_normalized('NFC', t), u.is_normalized('NFKC', t))\n"
                + "print(u.is_normalized('NFC', s), u.normalize('NFC', s) is s)\n", maxStrChars: 100_000));
        }

        // The limit itself stands: a form longer than MaxStrChars is refused, and is not the text's form to is_normalized.
        [Theory]
        [InlineData("u.normalize('NFD', s)")]
        [InlineData("u.normalize('NFKD', s)")]
        [InlineData("u.normalize('NFKC', chr(0xfdfa) * 5556)")]   // 18 characters each: 100,008
        [InlineData("u.normalize('NFKD', chr(0xfdfa) * 5556)")]
        public void AFormLongerThanTheStringLimitIsRefused(string call)
        {
            Assert.Equal("MaxStrChars", Limit(Accents + "r = " + call + "\n", maxStrChars: 100_000));
        }

        [Fact]
        public void AFormAtTheStringLimitIsBuilt()
        {
            Assert.Equal("99990 99990\nFalse False\n", Out(Accents
                + "print(len(u.normalize('NFKC', chr(0xfdfa) * 5555)), len(u.normalize('NFKD', chr(0xfdfa) * 5555)))\n"
                + "print(u.is_normalized('NFD', s), u.is_normalized('NFKD', s))\n", maxStrChars: 100_000));
        }

        // Row 17: the code point buffer was the whole text's, 4 bytes a character beside the result. It is the longest
        // piece's now: NFC of 1.2M characters is 7 MB charged, not 13.
        [Fact]
        public void NormalizingALongTextBuffersOnePiece()
        {
            RunResult r = Run("import unicodedata as u\nbase = 'Cafe' + chr(0x301) + ' r' + chr(0xe9) + 'sum' + chr(0xe9) + ' na' + chr(0xef) + 've '"
                + " + chr(0xfb01) + 'n ' + chr(0x2460) + ' '\ns = base * 50000\nr = u.normalize('NFC', s)\nprint(len(s), len(r))\n", 10L << 20);
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName);
            Assert.Equal("1200000 1150000\n", r.PrintOutput);
        }

        // What lets a text be cut before a character below U+0300, for every such character.
        [Fact]
        public void EveryCharacterBelowTheMarksStartsAPiece()
        {
            for (int cp = 0; cp < PyUnicodeNormalize.CutBelow; cp++)
            {
                Assert.True(PyUnicodeProps.Combining(cp) == 0 && !PyUnicodeProps.IsCompositionSecond(cp), "U+" + cp.ToString("X4"));
                foreach (bool compat in new[] { false, true })
                {
                    int[] d = PyUnicodeProps.FullDecompositionOf(cp, compat);
                    if (d != null)
                    {
                        Assert.True(PyUnicodeProps.Combining(d[0]) == 0 && !PyUnicodeProps.IsCompositionSecond(d[0]),
                            "U+" + cp.ToString("X4") + " starts with U+" + d[0].ToString("X4"));
                    }
                }
            }
        }

        // Starters, marks of several classes, compatibility characters (U+00B5 becomes a letter above the cut), Hangul,
        // astral ones, a pair of starters that composes, and lone surrogates.
        private static readonly int[] Alphabet =
        {
            'a', ' ', 'e', 'A', 0xE9, 0xC5, 0xA8, 0xB5, 0xBC, 0x149, 0x1C4, 0x2D8, 0x301, 0x316, 0x327, 0x340, 0x344, 0x9C7, 0x9BE, 0xF73,
            0x1100, 0x1161, 0x11A8, 0xAC00, 0x212B, 0x2460, 0xFB01, 0xFDFA, 0x1D15E, 0x1D165, 0x1F600, 0xD83D, 0xDC00,
        };

        // The piecewise build against the same build over the text as one piece (cutBelow 0), on 3,000 generated
        // texts in the four forms, and the limit at the result's length and one under it.
        [Fact]
        public void ThePiecewiseFormIsTheWholeTextsForm()
        {
            uint seed = 12345;
            for (int n = 0; n < 3000; n++)
            {
                var sb = new StringBuilder();
                seed = seed * 1664525 + 1013904223;
                int length = 1 + (int)((seed >> 8) % 40);
                for (int i = 0; i < length; i++)
                {
                    seed = seed * 1664525 + 1013904223;
                    int cp = Alphabet[(seed >> 8) % (uint)Alphabet.Length];
                    if (cp >= 0xD800 && cp < 0xE000)
                        sb.Append((char)cp);
                    else
                        sb.Append(char.ConvertFromUtf32(cp));
                }
                string s = sb.ToString();
                for (int form = 0; form < 4; form++)
                {
                    bool compat = (form & 1) != 0, compose = (form & 2) != 0;
                    string whole = PyUnicodeNormalize.Rebuild(s, compat, compose, long.MaxValue, null, 0);
                    string pieces = PyUnicodeNormalize.Rebuild(s, compat, compose, long.MaxValue, null, PyUnicodeNormalize.CutBelow);
                    Assert.True(whole == pieces, "text " + n + " form " + form);
                    Assert.Equal(whole, PyUnicodeNormalize.Rebuild(s, compat, compose, whole.Length, null, PyUnicodeNormalize.CutBelow));
                    if (whole.Length > 0)
                        Assert.Null(PyUnicodeNormalize.Rebuild(s, compat, compose, whole.Length - 1, null, PyUnicodeNormalize.CutBelow));
                }
            }
        }

        // Row 18: the empty path. CPython's find, findall and findtext compare it with the children's tags when no map
        // is given and fail on iterfind's None when one is; 1.1.0 raised ValueError and the build under review SyntaxError for all.
        [Fact]
        public void TheEmptyPathAnswersAsCPythonDoes()
        {
            const string Expected = "find None\nfindall []\nfindtext None\nfindtext dflt 'dflt'\niterfind None\niterfind ns None\n"
                + "find ns TypeError 'NoneType' object is not an iterator\nfind ns2 TypeError 'NoneType' object is not an iterator\n"
                + "findall ns TypeError 'NoneType' object is not iterable\nfindtext ns TypeError 'NoneType' object is not an iterator\n"
                + "for iterfind TypeError 'NoneType' object is not iterable\n"
                + "tree find None\ntree findall []\ntree iterfind None\ntree findtext None\n"
                + "empty tag find True\nempty tag findall 1\nempty tag findtext 'T'\nempty tag iterfind None\n"
                + "space None\nslash SyntaxError cannot use absolute path on element\n";
            Assert.Equal(Expected, Out("import xml.etree.ElementTree as ET\nroot = ET.fromstring('<r><a/></r>')\n"
                + "def t(label, fn):\n    try:\n        print(label, repr(fn()))\n    except Exception as e:\n        print(label, type(e).__name__, e)\n"
                + "t('find', lambda: root.find(''))\nt('findall', lambda: root.findall(''))\nt('findtext', lambda: root.findtext(''))\n"
                + "t('findtext dflt', lambda: root.findtext('', 'dflt'))\nt('iterfind', lambda: root.iterfind(''))\n"
                + "t('iterfind ns', lambda: root.iterfind('', {'p': 'urn:x'}))\n"
                + "t('find ns', lambda: root.find('', {}))\nt('find ns2', lambda: root.find('', {'p': 'urn:x'}))\n"
                + "t('findall ns', lambda: root.findall('', {'p': 'urn:x'}))\nt('findtext ns', lambda: root.findtext('', 'dflt', {'p': 'urn:x'}))\n"
                + "t('for iterfind', lambda: [e for e in root.iterfind('')])\n"
                + "tree = ET.ElementTree(root)\nt('tree find', lambda: tree.find(''))\nt('tree findall', lambda: tree.findall(''))\n"
                + "t('tree iterfind', lambda: tree.iterfind(''))\nt('tree findtext', lambda: tree.findtext(''))\n"
                + "e = ET.SubElement(root, '')\ne.text = 'T'\n"
                + "t('empty tag find', lambda: root.find('') is e)\nt('empty tag findall', lambda: len(root.findall('')))\n"
                + "t('empty tag findtext', lambda: root.findtext(''))\nt('empty tag iterfind', lambda: root.iterfind(''))\n"
                + "t('space', lambda: root.find(' '))\nt('slash', lambda: root.find('/'))\n"));
        }

        private const string Feed = "import xml.etree.ElementTree as ET\nroot = ET.Element('{urn:x}feed')\n";

        // Row 19: a namespaced name was a step of its own, three an element: serializing an ordinary namespaced tree
        // took 2.3 times the steps of 1.1.0. Five tostring calls over 3,000 entries are 164K steps, not 374K.
        [Fact]
        public void ANamespacedTreeSerializesAtThePlainTreesSteps()
        {
            Assert.Equal("288465\n", Out(Feed + "for i in range(3000):\n    e = ET.SubElement(root, '{urn:x}entry', id=str(i))\n"
                + "    e.set('{urn:m}t', 'ab'[i % 2])\n    ET.SubElement(e, '{urn:x}title').text = 'T & %d' % i\n"
                + "    ET.SubElement(e, '{urn:m}v').text = str(i * 3)\nout = ''\nfor k in range(5):\n"
                + "    out = ET.tostring(root, encoding='unicode')\nprint(len(out))\n", maxSteps: 250_000));
        }

        // ...and a long name is still paid by the character: 300 names of 6,000 characters cost steps that 300 short
        // ones do not.
        [Fact]
        public void ALongNamespacedNameIsStillPaidByTheCharacter()
        {
            const string Dump = "for i in range(300):\n    ET.SubElement(root, name + str(i))\nprint(len(ET.tostring(root, encoding='unicode')))\n";
            RunResult longNames = Run(Feed + "name = '{urn:' + 'u' * 3000 + '}' + 'n' * 3000\n" + Dump);
            RunResult shortNames = Run(Feed + "name = '{urn:u}n'\n" + Dump);
            Assert.True(longNames.Succeeded && shortNames.Succeeded);
            Assert.True(longNames.Stats.Steps - shortNames.Stats.Steps >= 300 * 6000 / 4096 * 2,
                "long " + longNames.Stats.Steps + ", short " + shortNames.Stats.Steps);
        }

        // Row 5: filter(None, ...) and filterfalse(None, ...) read the truth of an item through the general call
        // path, a native frame more per nested filter: 100,000 nested ones overflowed the guard where 1.1.0 ran 200,000.
        [Fact]
        public void TwoHundredThousandNestedTruthFiltersRun()
        {
            Assert.Equal("[5, 7]\n[0, 0, 0]\n", Out("import itertools\nit = iter([0, 0, 5, 0, 7])\nfor i in range(200000):\n    it = filter(None, it)\n"
                + "print(list(it))\nit = iter([0, 0, 5, 0, 7])\nfor i in range(200000):\n    it = itertools.filterfalse(None, it)\nprint(list(it))\n"));
        }

        // The truth filter keeps what the general path did with an item whose __bool__ raises.
        [Fact]
        public void ATruthFilterPassesWhatBoolRaises()
        {
            const string Expected = "[]\nStopIteration B\n[]\nStopIteration B\nValueError V\nValueError V\n[1, 2]\n[0, '']\n";
            Assert.Equal(Expected, Out("import itertools\nclass B:\n    def __init__(self, e):\n        self.e = e\n    def __bool__(self):\n        raise self.e\n"
                + "def t(fn):\n    try:\n        print(fn())\n    except BaseException as e:\n        print(type(e).__name__, e)\n"
                + "t(lambda: list(filter(None, [B(StopIteration('B')), 1])))\nt(lambda: next(filter(None, [B(StopIteration('B'))])))\n"
                + "t(lambda: list(itertools.filterfalse(None, [B(StopIteration('B')), 1])))\n"
                + "t(lambda: next(itertools.filterfalse(None, [B(StopIteration('B'))])))\n"
                + "t(lambda: list(filter(None, [1, B(ValueError('V'))])))\nt(lambda: list(itertools.filterfalse(None, [0, B(ValueError('V'))])))\n"
                + "t(lambda: list(filter(None, [0, 1, '', 2])))\nt(lambda: list(itertools.filterfalse(None, [0, 1, '', 2])))\n"));
        }

        private const string Digits = "def t(v):\n    try:\n        return len(str(v))\n    except ValueError as e:\n        return str(e)[:31]\n";

        // Row 6: the 4,300-digit cap was read after the conversion. It is read on the bit length first, as CPython
        // reads it: 3,000 refused str() of a 14,464-bit number are 400K steps, where the build under review ran out of 200M.
        [Fact]
        public void TheDigitCapIsReadBeforeTheConversion()
        {
            const string Refused = "Exceeds the limit (4300 digits)";
            Assert.Equal("4300\n" + Refused + "\n4301\n4300\n" + Refused + "\n" + Refused + "\n" + Refused + "\n3000\n6\n3618 14466 4824\n",
                Out(Digits + "print(t(10 ** 4300 - 1))\nprint(t(10 ** 4300))\nprint(t(-(10 ** 4300 - 1)))\nprint(t(2 ** 14284))\n"
                + "print(t(2 ** 14285 - 1))\nprint(t(2 ** 14285))\nprint(t(2 ** 20000))\nbig = 2 ** 14463\nn = 0\n"
                + "for i in range(3000):\n    try:\n        str(big)\n    except ValueError:\n        n += 1\nprint(n)\nn = 0\n"
                + "for f in (repr, lambda v: '%d' % v, lambda v: format(v, 'd'), lambda v: f'{v}', lambda v: '{:,}'.format(v), print):\n"
                + "    try:\n        f(big)\n    except ValueError:\n        n += 1\nprint(n)\n"
                + "print(len(hex(big)), len(bin(big)), len(oct(big)))\n", maxSteps: 2_000_000));
        }

        private const string SixWays = "import re\nrisky = 'ab' + chr(0x300) + 'cd'\nplain = 'abcd'\nkept = []\nfor i in range(2000):\n"
            + "    p = re.compile(r'\\b\\w+%d|x\\b' % i)\n    p.search(plain); p.match(plain); p.fullmatch(plain)\n"
            + "    p.search(risky); p.match(risky); p.fullmatch(risky)\n";

        // Row 14: a pattern is up to six regexes (search, match and fullmatch, each again for a subject the fast
        // translation cannot take) and one was charged: 2,000 kept patterns used every way held 36 MB on 3 MB charged.
        [Fact]
        public void AKeptPatternIsChargedForEveryRegexItBuilds()
        {
            Assert.Equal("MaxAllocBytes", Limit(SixWays + "    kept.append(p)\nprint(len(kept))\n", 16L << 20));
            RunResult r = Run(SixWays + "    kept.append(p)\nprint(len(kept))\n");
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName);
            Assert.True(r.Stats.AllocatedBytes >= 2000L * 6 * RegexFootprint.Held(""), "charged " + r.Stats.AllocatedBytes);
        }

        // Patterns used one way are charged as before, and dropped ones are given back: the limit is the worst case
        // of a run in which the CLR collected nothing.
        [Fact]
        public void OrdinaryAndDroppedPatternsFitAsBefore()
        {
            Assert.Equal("2000\n", Out("import re\nkept = []\nn = 0\nfor i in range(2000):\n    p = re.compile(r'id-%d-(\\d+)' % i)\n"
                + "    if p.search('id-%d-77' % i):\n        n += 1\n    kept.append(p)\nprint(n)\n", 8L << 20));
            Assert.Equal("1\n", Out(SixWays + "    kept = [p]\nprint(len(kept))\n", 64L << 20));
        }

        [Fact]
        public void ARegexIsHeldByItsTranslationsLength()
        {
            Assert.Equal(1536, RegexFootprint.Held(""));
            Assert.Equal(1536 + (2 + 6) * 1000, RegexFootprint.Held(new string('a', 1000)));
        }

        // Row 16: the cache weighed a script by its characters, and a program of one-character tokens keeps 80 times
        // its source once it has run: four such scripts held 330 MB under the 2M-character cap.
        [Fact]
        public void TheCacheWeighsAScriptByItsTokensToo()
        {
            Assert.Equal(1000, ScriptEngine.CacheWeight(1000, 100));
            Assert.Equal(1000, ScriptEngine.CacheWeight(1000, 375));
            Assert.Equal(2400, ScriptEngine.CacheWeight(1000, 900));
            var engine = new ScriptEngine(new EngineOptions());
            long one = 0;
            for (int i = 0; i < 4; i++)
            {
                var sb = new StringBuilder("x = " + i + "\n");
                for (int k = 0; k < 120000; k++)
                    sb.Append("a=0\n");
                string source = sb.ToString();
                CompiledScript compiled = engine.Compile(source);
                Assert.True(compiled.TokenCount >= 3 * 120000, "tokens " + compiled.TokenCount);
                Assert.True(engine.CachedSourceChars <= 2000000, "cached " + engine.CachedSourceChars);
                if (i == 0)
                {
                    one = engine.CachedSourceChars;
                    Assert.True(one > source.Length, "weighs " + one + " for " + source.Length + " characters");
                }
                Assert.True(engine.CachedSourceChars <= one + 16, "more than one dense script cached: " + engine.CachedSourceChars);
            }
        }

        private const string Stops = "import itertools, heapq\n"
            + "class Seq:\n    def __getitem__(self, i):\n        if i == 3:\n            raise StopIteration('G')\n        return i\n"
            + "class Sub(StopIteration):\n    pass\n"
            + "class Seq2:\n    def __getitem__(self, i):\n        if i == 2:\n            raise Sub('G2')\n        return i + 1\n"
            + "class Seq3:\n    def __getitem__(self, i):\n        if i == 2:\n            raise KeyError('k')\n        return i + 1\n"
            + "class B:\n    def __init__(self, n):\n        self.n = n\n    def __bool__(self):\n        if self.n == 2:\n"
            + "            raise StopIteration('B')\n        return True\n"
            + "class K:\n    def __eq__(self, o):\n        raise StopIteration('E')\n    def __hash__(self):\n        return 1\n"
            + "class L:\n    def __init__(self, n):\n        self.n = n\n    def __lt__(self, o):\n        raise StopIteration('L')\n"
            + "sel = [B(1), B(1), B(2), B(1)]\n"
            + "def unpack():\n    a, b, c = Seq()\n    return (a, b, c)\n"
            + "def twice():\n    it = iter(Seq())\n    return (list(it), list(it))\n"
            + "def third():\n    it = filter(None, Seq())\n    return (next(it), next(it), next(it, 'end'))\n"
            + "def t(fn):\n    try:\n        print(fn())\n    except BaseException as e:\n        print(type(e).__name__, e)\n";

        // Row 5, what its review found: the truth filter no longer reads a StopIteration out of its source as the
        // end, so every source says its own end. A sequence ends on StopIteration from __getitem__ as on IndexError.
        [Theory]
        [InlineData("list(Seq())", "[0, 1, 2]")]
        [InlineData("tuple(Seq())", "(0, 1, 2)")]
        [InlineData("sum(Seq())", "3")]
        [InlineData("max(Seq())", "2")]
        [InlineData("unpack()", "(0, 1, 2)")]
        [InlineData("(2 in Seq(), 7 in Seq())", "(True, False)")]
        [InlineData("twice()", "([0, 1, 2], [])")]
        [InlineData("list(enumerate(Seq()))", "[(0, 0), (1, 1), (2, 2)]")]
        [InlineData("list(itertools.batched(Seq(), 2))", "[(0, 1), (2,)]")]
        [InlineData("list(Seq2())", "[1, 2]")]
        [InlineData("list(filter(None, Seq()))", "[1, 2]")]
        [InlineData("third()", "(1, 2, 'end')")]
        [InlineData("list(itertools.filterfalse(None, Seq()))", "[0]")]
        [InlineData("list(filter(None, itertools.chain([1], Seq(), [9])))", "[1, 1, 2, 9]")]
        [InlineData("list(Seq3())", "KeyError 'k'")]
        [InlineData("list(filter(None, Seq3()))", "KeyError 'k'")]
        public void ASequenceEndsOnStopIterationFromGetItem(string expression, string expected)
        {
            Assert.Equal(expected + "\n", Out(Stops + "t(lambda: " + expression + ")\n"));
        }

        // The same for the iterators that call script on their own: a selector's __bool__ in compress, the keys'
        // __eq__ in groupby.
        [Theory]
        [InlineData("list(itertools.compress([1, 2, 3, 4], sel))", "[1, 2]")]
        [InlineData("list(filter(None, itertools.compress([1, 2, 3, 4], sel)))", "[1, 2]")]
        [InlineData("list(itertools.filterfalse(None, itertools.compress([1, 2, 3, 4], sel)))", "[]")]
        [InlineData("len(list(itertools.groupby([K(), K()])))", "1")]
        [InlineData("len(list(filter(None, itertools.groupby([K(), K()]))))", "1")]
        [InlineData("[len(list(g)) for k, g in itertools.groupby([K(), K()])]", "[1]")]
        public void AnIteratorEndsOnStopIterationFromTheScriptItCalls(string expression, string expected)
        {
            Assert.Equal(expected + "\n", Out(Stops + "t(lambda: " + expression + ")\n"));
        }

        // heapq.merge raises it, bare and filtered alike, where CPython's merge (a generator) raises RuntimeError:
        // an error either way, not a shorter list.
        [Theory]
        [InlineData("list(heapq.merge([L(1), L(3)], [L(2)]))")]
        [InlineData("list(filter(None, heapq.merge([L(1), L(3)], [L(2)])))")]
        public void AMergeRaisesTheStopIterationOfACompare(string expression)
        {
            Assert.Equal("StopIteration L\n", Out(Stops + "t(lambda: " + expression + ")\n"));
        }

        // The handlers above are off the frames that nest: a chain of these goes as deep as it did before.
        [Theory]
        [InlineData("itertools.compress(it, itertools.repeat(1))", "list(it)", 200000, "[1, 0, 3]")]
        [InlineData("itertools.compress(itertools.repeat(1), it)", "next(it)", 200000, "1")]
        [InlineData("next(itertools.groupby(it))[1]", "len(list(it))", 125000, "1")]
        [InlineData("heapq.merge(it)", "list(it)", 75000, "[1, 0, 3]")]
        public void NestedIteratorsGoAsDeepAsBefore(string wrap, string read, int levels, string expected)
        {
            Assert.Equal(expected + "\n", Out("import itertools, heapq\nit = iter([1, 0, 3])\nfor i in range(" + levels + "):\n    it = " + wrap
                + "\nprint(" + read + ")\n"));
        }

        // Row 13's bound: a composed piece is taken as a quarter of its decomposed length, the longest canonical
        // decomposition in the table.
        [Fact]
        public void NoCanonicalDecompositionIsLongerThanTheBoundSays()
        {
            int longest = 0;
            for (int cp = 0; cp <= 0x10FFFF; cp++)
            {
                int[] d = PyUnicodeProps.FullDecompositionOf(cp, false);
                if (d != null && d.Length > longest)
                {
                    longest = d.Length;
                }
            }
            Assert.Equal(PyUnicodeNormalize.MaxCanonicalLength, longest);
        }

        // is_normalized stops once the form has outgrown the string: 200,000 of U+FDFA (18 characters each in the
        // compatibility forms) were 49 MB charged for the two answers, now 8.
        [Fact]
        public void IsNormalizedStopsAtTheStringsOwnLength()
        {
            string source = "import unicodedata\ns = chr(0xfdfa) * 200000\n"
                + "print(unicodedata.is_normalized('NFKC', s), unicodedata.is_normalized('NFKD', s))\n"
                + "print(unicodedata.is_normalized('NFC', 'abc' * 1000), unicodedata.is_normalized('NFD', chr(0x439)), "
                + "unicodedata.is_normalized('NFC', chr(0x439)))\n"
                + "print(unicodedata.is_normalized('NFC', 'a' + chr(0x301)), unicodedata.is_normalized('NFD', 'a' + chr(0x301)), "
                + "unicodedata.is_normalized('NFKC', chr(0xfb01)))\n";
            Assert.Equal("False False\nTrue False True\nFalse True False\n", Out(source, 16L << 20));
        }

        // Row 17, what its review found: a text that is one piece (no character below U+0300) grew its buffer by
        // doubling on every call. Eight NFD of 400,000 such characters are 49 MB charged, not 57.
        [Fact]
        public void AOnePieceTextSizesItsBufferOnce()
        {
            RunResult r = Run("import unicodedata\ns = chr(0x439) * 400000\nn = 0\nfor i in range(8):\n"
                + "    n += len(unicodedata.normalize('NFD', s))\nprint(n)\n");
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName);
            Assert.Equal("6400000\n", r.PrintOutput);
            Assert.True(r.Stats.AllocatedBytes < 53L << 20, "charged " + r.Stats.AllocatedBytes);
        }
    }
}
