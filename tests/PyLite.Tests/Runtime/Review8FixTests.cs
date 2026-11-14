using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // Review 8 (leaks, speed, regressions, crashes against 1.1.0). Each answer is CPython 3.14's.
    [Trait("Category", "Regression")]
    public class Review8FixTests
    {
        private static RunResult Run(string source, long? maxAllocBytes = null)
        {
            ResourceLimits limits = TestEngines.Unhurried.With(maxAllocBytes: maxAllocBytes);
            return new ScriptEngine(new EngineOptions { DefaultLimits = limits }).Run(source, new RunRequest());
        }

        private static string Out(string source, long? maxAllocBytes = null)
        {
            RunResult r = Run(source, maxAllocBytes);
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName + " " + r.Error.Message);
            return r.PrintOutput;
        }

        // The earlier "expected ':'" after '->' took unary + and - for a missing annotation.
        [Fact]
        public void AReturnAnnotationMayStartWithASign()
        {
            Assert.Equal("5 2 3\n", Out("def f() -> -1:\n    return 5\nclass C:\n    def m(self) -> +1:\n        return 2\n"
                + "def g() -> - 1 if True else 0:\n    return 3\nprint(f(), C().m(), g())\n"));
        }

        // row[1] built a parent map of the whole tree per call, 48 B an element charged: 80 rounds over 62K elements
        // ran out of MaxAllocBytes where 1.1.0 finished in 0.5 s.
        [Fact]
        public void PositionalPredicatesBuildNoMapPerCall()
        {
            Assert.Equal("160\n", Out("from xml.etree import ElementTree as ET\n"
                + "row = '<row>' + ''.join('<c%d><v>%d</v></c%d>' % (j, j, j) for j in range(15)) + '</row>'\n"
                + "r = ET.fromstring('<table>' + row * 2000 + '</table>')\nn = 0\nfor _ in range(80):\n"
                + "    n += len(r.findall('row[1]'))\n    n += len(r.findall('row[last()]'))\nprint(n)\n", 256L << 20));
        }

        // A child under two parents (append, copy.copy) still takes the last parent in document order, as CPython's map.
        [Fact]
        public void ASharedChildKeepsCPythonsParent()
        {
            Assert.Equal("[('x', False), ('x', True), ('x', True)]\n[]\n[('x', True), ('x', True)]\n3 2\n[True, True, True]\n1 1\n",
                Out("import xml.etree.ElementTree as ET, copy\nr = ET.fromstring('<r><a><x/><y/><x/></a><b><y/></b></r>')\n"
                + "a, b = r[0], r[1]\nsx = a[2]\nb.append(sx)\n"
                + "print([(e.tag, e is sx) for e in r.findall('*/x[1]')])\nprint([(e.tag, e is sx) for e in r.findall('*/x[2]')])\n"
                + "print([(e.tag, e is sx) for e in r.findall('*/x[last()]')])\nprint(len(r.findall('.//x[1]')), len(r.findall('.//y[1]')))\n"
                + "c = copy.copy(a)\nr.append(c)\nprint([e is sx for e in r.findall('*/x[2]')])\nprint(len(a.findall('x[2]')), len(c.findall('x[2]')))\n"));
        }

        // CPython takes its parent map when the walk begins: what a lazy iterfind's loop inserts or moves later is not in it.
        [Fact]
        public void ATreeChangedDuringALazyWalkKeepsTheFirstMap()
        {
            Assert.Equal("A 1\nB 1\nC ['x', 'a']\nD 1\n", Out("import xml.etree.ElementTree as ET\n"
                + "r = ET.Element('r'); ET.SubElement(r, 'a')\nout = []\nfor x in r.iterfind('a[last()]'):\n    out.append(x)\n"
                + "    if len(out) < 3:\n        ET.SubElement(r, 'a')\nprint('A', len(out))\n"
                + "r = ET.Element('r'); b = ET.SubElement(r, 'b'); ET.SubElement(b, 'a')\nout = []\nfor x in r.iterfind('.//a[1]'):\n"
                + "    out.append(x)\n    if len(out) < 3:\n        nb = ET.SubElement(r, 'b'); ET.SubElement(nb, 'a')\nprint('B', len(out))\n"
                + "r = ET.Element('r'); ET.SubElement(r, 'x'); ET.SubElement(r, 'a')\nout = []\nfor x in r.iterfind('*[1]'):\n"
                + "    out.append(x.tag)\n    if len(out) == 1:\n        r.append(ET.Element('c'))\nprint('C', out)\n"
                + "r = ET.Element('r'); p = ET.SubElement(r, 'p'); q = ET.SubElement(r, 'q')\na = ET.SubElement(p, 'a')\nout = []\n"
                + "for x in r.iterfind('*/a[1]'):\n    out.append(x)\n    if len(out) == 1:\n        p.remove(a); q.append(a)\nprint('D', len(out))\n"));
        }

        // Nested small counts multiply: 64^3 passes keep the backtracking stack (?:ab)* keeps, and pay for it.
        [Fact]
        public void NestedSmallCountsPayForTheStack()
        {
            RunResult nested = Run("import re\ns = 'ab' * 500000\nprint(re.compile(r'(?:(?:(?:ab){1,64}){1,64}){1,64}').match(s).end())\n");
            Assert.True(nested.Succeeded, nested.Error == null ? "" : nested.Error.Message);
            Assert.Equal("524288\n", nested.PrintOutput);
            RunResult small = Run("import re\ns = 'ab' * 500000\nprint(re.compile(r'(?:ab){1,64}').match(s).end())\n");
            Assert.Equal("128\n", small.PrintOutput);
            Assert.True(nested.Stats.AllocatedBytes - small.Stats.AllocatedBytes >= 4L << 20,   // 262,144 passes, 16 B each
                nested.Stats.AllocatedBytes + " vs " + small.Stats.AllocatedBytes);
        }

        // f(*seq) copies alive at once (min's key recursing into min(*a)) are each paid; a loop at one depth pays one.
        [Fact]
        public void StarCopiesAliveTogetherArePaid()
        {
            RunResult r = Run("a = [0] * 50000\na[0] = -1\ndef h(n):\n    return min(*a, key=lambda v, n=n: (h(n - 1) if v < 0 and n > 0 else v))\n"
                + "print(h(150))\n", 16L << 20);
            Assert.False(r.Succeeded);
            Assert.Equal("MaxAllocBytes", r.Error.LimitName);
            // depth 0 made the free copy: the loop's copies inside f() are paid once, not 50 times (80 MB)
            Assert.Equal("9999950\n", Out("max(*[1, 2])\ndef f():\n    a = list(range(200000))\n    t = 0\n    for i in range(50):\n"
                + "        t += max(*a)\n    return t\nprint(f())\n", 16L << 20));
        }

        // CPython's other names for UTF-8, ASCII and Latin-1 (cp65001, us, ANSI_X3.4-1968...) were LookupError in 1.2.0.
        [Fact]
        public void CodecAliasesAreCPythons()
        {
            Assert.Equal("x x x x x x x x x x x x\nx x x\n", Out("import xml.etree.ElementTree as ET\nout = []\n"
                + "for n in ['cp65001', 'utf', 'utf8-ucs2', 'us', 'ANSI_X3.4-1968', 'csASCII', 'iso646-us', 'cp367', 'cp819', 'ibm819', 'iso-ir-100', 'csISOLatin1']:\n"
                + "    out.append(ET.fromstring(('<?xml version=\"1.0\" encoding=\"%s\"?><a>x</a>' % n).encode()).text)\n"
                + "print(*out)\nprint(b'x'.decode('cp65001'), 'x'.encode('us').decode(), b'x'.decode('latin'))\n"));
        }

        // \b and \B next to a plain word character test one side only (.NET keeps its literal scan); answers unchanged.
        [Fact]
        public void BoundariesNextToALiteralKeepTheirAnswers()
        {
            Assert.Equal("[] [(7, 8), (9, 10)] [(7, 8), (9, 10)] [] [] [] [] [(15, 16)] [(15, 17)]\n",
                Out("import re\ns = 'm' + chr(0xb2) + ' am ' + chr(0x301) + 'x x' + chr(0x301) + ' _k ' + chr(0x212a) + 'a'\n"
                + "print(*[[m.span() for m in re.finditer(p, s)] for p in [r'\\bm\\b', r'\\bx', r'x\\b', r'\\Bx', r'x\\B', r'\\bk', r'\\ba\\b', r'(?i)\\bk', r'(?i)\\bka']])\n"));
            // a quantifier past a comment still makes the letter optional
            Assert.Equal("[(1, 1), (2, 2)] [(0, 0), (3, 3)] [(1, 1), (2, 2)] [(1, 1), (2, 2)]\n",
                Out("import re\ns = ' b' + chr(0x301)\n"
                + "print(*[[m.span() for m in re.finditer(p, s)] for p in [r'\\ba(?#c)*', r'\\Ba(?#c)?', r'(?x)\\ba (?#c) *', r'\\ba(?#c){0,2}']])\n"));
        }

        // match() and fullmatch() build regexes of their own: charged as the plain one is.
        [Fact]
        public void MatchAndFullmatchRegexesArePaid()
        {
            const string build = "import re\np = re.compile('|'.join('word%d' % i for i in range(400)))\np.search('x')\n";
            RunResult search = Run(build);
            RunResult all = Run(build + "p.match('x')\np.fullmatch('x')\n");
            Assert.True(all.Succeeded);
            Assert.True(all.Stats.AllocatedBytes - search.Stats.AllocatedBytes >= 120_000,
                all.Stats.AllocatedBytes + " vs " + search.Stats.AllocatedBytes);
        }

        // A tree walk is a step per eight nodes (1.1.0 charged iter nothing; 1.2.0 a step a node).
        [Fact]
        public void TreeWalksStepPerEightNodes()
        {
            const string build = "import xml.etree.ElementTree as ET\nr = ET.fromstring('<r>' + '<i><s/><n/></i>' * 2700 + '</r>')\n";
            RunResult bare = Run(build);
            RunResult walked = Run(build + "n = 0\nfor _ in r.iter('none'):\n    n += 1\n");
            Assert.True(walked.Succeeded);
            long walk = walked.Stats.Steps - bare.Stats.Steps;
            Assert.True(walk > 0 && walk <= 8101 / 8 + 64, walk.ToString());
        }
    }
}
