using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // Review 9 (regressions, crashes, limits, slowdowns against 1.1.0 and the build before it). Each answer is CPython 3.14.2's.
    // Limits are proved by the limit that ends the run, never by timing.
    [Trait("Category", "Regression")]
    public class Review9FixTests
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

        private static void Refused(string limit, string source, long? maxAllocBytes = null, long? maxSteps = null)
        {
            RunResult r = Run(source, maxAllocBytes, maxSteps);
            Assert.False(r.Succeeded, r.PrintOutput);
            Assert.Equal(limit, r.Error.LimitName);
        }

        // tostring's namespace pass walked 2,000 attributes on each of 500,000 shared copies on no step: 21 s past a 3 s
        // deadline (1.1.0 leaked the runner). Each attribute is a tree node now: the step limit ends it first.
        [Fact]
        public void TostringStepsThroughAttributes()
        {
            Refused("MaxSteps", "import xml.etree.ElementTree as ET\ne = ET.Element('e', {('a%d' % i): 'v' for i in range(2000)})\n"
                + "root = ET.Element('r')\nroot.extend([e] * 500000)\nET.tostring(root)\n", maxSteps: 20_000_000);
        }

        private const string Table = "from xml.etree import ElementTree as ET\n"
            + "row = '<row>' + ''.join('<c%d><v>%d</v></c%d>' % (j, j, j) for j in range(15)) + '</row>'\n"
            + "r = ET.fromstring('<table>' + row * 2000 + '</table>')\n";

        private const string Rounds = "n = 0\nfor _ in range(80):\n    n += len(r.findall('row[1]'))\n    n += len(r.findall('row[last()]'))\nprint(n)\n";

        // One parent map per unchanged tree, whatever was shared or copied: after a reorder or a copy.copy every row
        // has two parents, which sent the earlier shortcut back to a map per call (the deadline at 245 MB).
        [Theory]
        [InlineData("")]
        [InlineData("t2 = ET.Element('table')\nt2.extend(sorted(r, key=lambda e: e.findtext('c0/v')))\nr = t2\n")]
        [InlineData("import copy\nsnap = copy.copy(r)\n")]
        public void PositionalSearchesShareOneMapPerTree(string prelude)
        {
            Assert.Equal("160\n", Out(Table + prelude + Rounds, 64L << 20));
        }

        // The map is taken when the search first needs it, as CPython's: a lazy iterfind whose loop shares, copies,
        // moves or detaches elements keeps it (the build under review answered 1 1 1 1 and ['1', '5']).
        [Fact]
        public void ALazyWalkKeepsItsMapWhateverTheLoopDoes()
        {
            Assert.Equal("share 2\ncopy 2\ndesc 3\nlast 2\ndetach ['1', '1b']\n", Out("import xml.etree.ElementTree as ET, copy\n"
                + "r = ET.fromstring('<r><a><x/></a><a><x/></a></r>')\nb = ET.Element('b')\ngot = []\n"
                + "for x in r.iterfind('a/x[1]'):\n    got.append(x)\n    b.append(r[1][0])\nprint('share', len(got))\n"
                + "r = ET.fromstring('<r><a><x/></a><a><x/></a></r>')\ngot = []\nkeep = []\n"
                + "for x in r.iterfind('a/x[1]'):\n    got.append(x)\n    keep.append(copy.copy(r[1]))\nprint('copy', len(got))\n"
                + "r = ET.fromstring('<r><a><x/><x/></a><a><x/><x/></a><a><x/></a></r>')\ngot = []\n"
                + "for x in r.iterfind('.//x[1]'):\n    got.append(x)\n    keep.append(copy.copy(r))\nprint('desc', len(got))\n"
                + "r = ET.fromstring('<r><a><x/><x/></a><a><x/><x/></a></r>')\ngot = []\n"
                + "for x in r.iterfind('a/x[last()]'):\n    got.append(x)\n    keep.append(copy.copy(r[1]))\nprint('last', len(got))\n"
                + "src = ET.fromstring('<r><rec><f>1</f><s><f>1b</f></s></rec><rec><f>3</f><f>4</f></rec><rec><f>5</f></rec></r>')\n"
                + "out = ET.Element('out')\ngot = []\nfor f in src.iterfind('.//f[1]'):\n    got.append(f.text)\n    rec = src[0]\n"
                + "    src.remove(rec)\n    ET.SubElement(out, 'done')\nprint('detach', got)\n"));
        }

        // Every kind of change between two searches is seen by the next one: a kept map is never stale.
        [Fact]
        public void EveryChangeIsSeenByTheNextSearch()
        {
            Assert.Equal("[[1, 2, 1], [1, 2, 1], [1, 2, 1], [1, 2, 1], [1, 2, 1], [1, 2, 1], [1, 2, 1], [1, 2, 1], [1, 3, 1], [1, 3, 1, 1], [0, 0, 0], [1, 1, 1]]\n",
                Out("import xml.etree.ElementTree as ET, copy\nout = []\nr = ET.fromstring('<r><a/><a/><b><a/></b></r>')\n"
                + "def q(): return [len(r.findall('a[last()]')), len(r.findall('.//a[1]')), len(r.findall('*[2]'))]\n"
                + "out.append(q())\nr.append(ET.Element('a')); out.append(q())\nr.remove(r[0]); out.append(q())\n"
                + "r[0:1] = [ET.Element('c'), ET.Element('a')]; out.append(q())\nr.insert(0, ET.Element('a')); out.append(q())\n"
                + "r.extend([ET.Element('a'), ET.Element('d')]); out.append(q())\nr[1] = ET.Element('a'); out.append(q())\n"
                + "del r[0:2]; out.append(q())\nET.SubElement(r[0], 'a'); out.append(q())\n"
                + "r2 = copy.copy(r); out.append(q() + [len(r2.findall('a[last()]'))])\nr.clear(); out.append(q())\n"
                + "ns = {'p': 'urn:x'}\nt = ET.fromstring('<r xmlns:p=\"urn:x\" xmlns:q=\"urn:y\"><p:a/><q:a/></r>')\n"
                + "out.append([len(t.findall('p:a', ns)), len(t.findall('p:a', {'p': 'urn:y'})), len(t.findall('{urn:x}a'))])\n"
                + "print(out)\n"));
        }

        // ElementPath's path cache: 800,000 find() calls parse 10 paths (1.1.0: 14 MB, 1.2.0: MaxAllocBytes); a path
        // that does not parse raises every time; more than 100 paths still answer.
        [Fact]
        public void PathsAreParsedOncePerRun()
        {
            Assert.Equal("0\n", Out("import xml.etree.ElementTree as ET\n"
                + "fields = ['Id', 'Name', 'City', 'Zip', 'Qty', 'Price', 'Cur', 'Date', 'Ref', 'Note']\n"
                + "rec = '<R>' + ''.join('<%s>%d</%s>' % (f, k + 1, f) for k, f in enumerate(fields)) + '</R>'\n"
                + "root = ET.fromstring('<Rows>' + rec * 5000 + '</Rows>')\nbad = 0\nfor rule in range(16):\n    for r in root:\n"
                + "        for f in fields:\n            if r.find(f) is None:\n                bad += 1\nprint(bad)\n", 64L << 20));
            Assert.Equal("['XPath position >= 1 expected', 'XPath position >= 1 expected'] 2 2\n", Out("import xml.etree.ElementTree as ET\n"
                + "r = ET.fromstring('<r><a/><b/></r>')\nerrs = []\nfor i in range(2):\n    try:\n        r.find('*[0]')\n"
                + "    except SyntaxError as e:\n        errs.append(str(e))\n"
                + "n = sum(len(r.findall('*[%d]' % (1 + i))) for i in range(250))\nprint(errs, len(r.findall('*')), n)\n"));
        }

        // An extend that raises part way keeps the children it added, and the next search sees them; a predicate's script
        // code that adds an element runs after the search took its map, as in CPython.
        [Fact]
        public void APartialExtendAndAPredicatesChangeAnswerAsCPython()
        {
            Assert.Equal("before None\nTypeError expected an Element, not \"int\"\nlen 2\na[2] is x True\nx/..  True\nfindall a[last()] [True]\nfind None\ndotdot []\n",
                Out("import xml.etree.ElementTree as ET\nr = ET.Element('r')\na1 = ET.SubElement(r, 'a')\nprint('before', r.find('a[2]'))\n"
                + "x = ET.Element('a')\ntry:\n    r.extend([x, 5])\nexcept TypeError as e:\n    print('TypeError', e)\nprint('len', len(r))\n"
                + "print('a[2] is x', r.find('a[2]') is x)\nprint('x/.. ', r.find('a[2]/..') is r)\n"
                + "print('findall a[last()]', [e is x for e in r.findall('a[last()]')])\n"
                + "r = ET.Element('r')\nx = ET.SubElement(r, 'x')\nt = ET.SubElement(x, 't')\nclass Sneaky:\n"
                + "    def __init__(self): self.n = 0\n    def __bool__(self):\n        self.n += 1\n        if self.n == 1:\n"
                + "            ET.SubElement(x, 'y')\n        return False\nt.text = Sneaky()\nprint('find', r.find(\"x[t='']/y[1]\"))\n"
                + "r2 = ET.Element('r'); x2 = ET.SubElement(r2, 'x'); t2 = ET.SubElement(x2, 't')\nclass S2:\n    def __bool__(self):\n"
                + "        ET.SubElement(x2, 'z')\n        return False\nt2.text = S2()\nprint('dotdot', [e.tag for e in r2.findall(\"x[t='']/z/..\")])\n"));
        }

        // A path of a million steps pays for its walk on every call; a namespace map passed again costs no new key.
        [Fact]
        public void LongPathsPayAndNamespaceLoopsDoNot()
        {
            Refused("MaxAllocBytes", "import xml.etree.ElementTree as ET\nr = ET.Element('r')\np = './' * 1000000 + '.'\n"
                + "for i in range(12):\n    r.find(p)\n", 64L << 20);
            Assert.Equal("100000\n", Out("import xml.etree.ElementTree as ET\nns = {'a': 'urn:a', 'b': 'urn:b', 'c': 'urn:c', 'd': 'urn:d'}\n"
                + "r = ET.fromstring('<r xmlns:a=\"urn:a\"><a:item/></r>')\nn = 0\nfor i in range(100000):\n"
                + "    if r.find('a:item', ns) is not None:\n        n += 1\nprint(n)\n", 24L << 20));
        }

        // A bounded loop's stack stops at its passes: 12 rules of {1,10} in {1,10} over a 3.6 MB document were refused.
        [Fact]
        public void BoundedNestedCountsPayForTheirPassesOnly()
        {
            RunResult r = Run("import re\ndoc = 'Alpha beta, gamma delta; epsilon zeta: eta theta iota kappa lambda mu nu ' * 50000\n"
                + "keep = []\nn = 0\nfor i in range(12):\n    p = re.compile(r'(?:(?:[A-Za-z]+[.:;,]? ){1,10}){1,10}(?:%d)?' % i)\n"
                + "    n += len(p.findall(doc, 0, 300)) + (1 if p.search(doc, 3000000) else 0)\n    keep.append(p)\nprint(n)\n");
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName);
            Assert.True(r.Stats.AllocatedBytes < 32L << 20, r.Stats.AllocatedBytes.ToString());
        }

        // A comment between a group and its count, and a backreference under one, loop as far as the plain forms do.
        [Theory]
        [InlineData("(?:(?:(?:ab)(?#c){1,64})(?#c){1,64})(?#c){1,64}(?:x%d)?", "'ab' * 500000", 150)]
        [InlineData("(?:(?:(?:ab){1,64}){1,64}){1,64}(?:x%d)?", "'ab' * 500000", 150)]
        [InlineData("(a)\\1*(?:x%d)?", "'a' * 1000000", 60)]
        [InlineData("(?P<g>a)(?P=g)+(?:x%d)?", "'a' * 1000000", 60)]
        [InlineData("(?:(?:(a)\\1{1,64}){1,64}){1,64}(?:x%d)?", "'a' * 1000000", 60)]
        [InlineData("(?:a|b){1,999990}(?:x%d)?", "'a' * 1000000", 60)]   // a large bounded count is its passes, not 1,000
        [InlineData("(a)\\1{1,999999}(?:x%d)?", "'a' * 1000000", 60)]
        public void HiddenLoopsPayForTheStack(string pattern, string subject, int kept)
        {
            Refused("MaxAllocBytes", "import re\ns = " + subject + "\nkeep = []\nfor i in range(" + kept + "):\n"
                + "    p = re.compile(r'" + pattern + "' % i)\n    p.match(s)\n    keep.append(p)\n", 32L << 20);
        }

        // IGNORECASE: .NET's own folding where the subject holds none of the characters sre adds (a fresh keyword
        // alternation per batch was 2.5 times 1.1.0 and refused); the subject that holds one gets sre's answers.
        [Fact]
        public void IgnoreCaseCompileLoopsFitAndAnswerAsSre()
        {
            Assert.Equal("1000\n", Out("import re\nhits = 0\nfor b in range(1000):\n    kws = ['kw%d_%d' % (b, i) for i in range(400)]\n"
                + "    p = re.compile(r'\\b(?:' + '|'.join(kws) + r')\\b', re.I)\n    if p.search('nothing here kw%d_7 ok' % b):\n"
                + "        hits += 1\nprint(hits)\n"));
            Assert.Equal("[[(0, 1), (1, 2), (2, 3)], [(0, 1), (1, 2), (2, 3)], [(3, 4), (4, 5), (5, 6), (6, 7), (7, 8), (8, 9), (9, 10), (10, 11), (11, 12), (12, 13), (13, 14)], "
                + "[(3, 4), (4, 5)], [(3, 4), (4, 5)], [(5, 6), (6, 7), (7, 8), (8, 9)], [(5, 6), (6, 7), (7, 8), (8, 9)], "
                + "[(0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 6), (6, 7), (7, 8), (8, 9)], [(9, 10), (10, 11), (11, 12), (12, 13), (13, 14)], "
                + "[(11, 12), (12, 13), (13, 14)], [(9, 10), (10, 11)], [], [(0, 1)], [], [(0, 1)]]\n"
                + "[[(0, 1), (5, 6), (10, 11)], [(0, 1), (5, 6), (10, 11)], [(1, 2), (2, 3), (3, 4), (4, 5), (6, 7), (7, 8), (8, 9), (9, 10), (11, 12)], "
                + "[(7, 8), (11, 12)], [(7, 8), (11, 12)], [(8, 9)], [(8, 9)], [(0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 6), (7, 8), (8, 9), (9, 10), (10, 11), (11, 12)], "
                + "[(6, 7)], [], [], [], [(0, 1), (5, 6), (10, 11)], [(0, 6)], [(0, 1)]]\n",
                Out("import re\ns = 's' + chr(0x17f) + 'S' + chr(0x212a) + 'k' + chr(0x131) + 'iI' + chr(0x130) + chr(0xb5) + chr(0x3bc) + chr(0x3c2) + chr(0x3c3) + chr(0x3a3)\n"
                + "pats = ['s', 'S', '[^s]', 'k', 'K', 'i', 'I', '[a-z]', '[^a-z]', chr(0x3c3), chr(0xb5), '(s)\\\\1', '(?-i:s)', 'status', '\\\\bs']\n"
                + "print([[m.span() for m in re.finditer(p, s, re.I)] for p in pats])\n"
                + "print([[m.span() for m in re.finditer(p, 'status kiosk', re.I)] for p in pats])\n"));
        }
    }
}
