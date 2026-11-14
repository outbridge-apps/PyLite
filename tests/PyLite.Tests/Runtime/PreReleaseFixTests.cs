using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // Review 9's leftovers fixed before 1.2.0: iterparse under root.clear(), the element's weight, scratch parent maps and
    // positions without them, \U escapes, a step per match. Each answer is CPython 3.14.2's; limits are proved by the
    // limit that ends the run (or by the run fitting one 1.2.0 refused), never by timing.
    [Trait("Category", "Regression")]
    public class PreReleaseFixTests
    {
        private static readonly string B = ((char)92).ToString();   // a backslash, spelled so no tool rewrites an escape

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

        private const string Doc = "import xml.etree.ElementTree as ET, io\n"
            + "doc = '<root>' + ''.join('<rec id=\"%d\"><v>%d</v><w/></rec>' % (i, i) for i in range(12)) + '</root>'\n";

        // The streaming idiom cleared the root after each record and got 1 of 10: the events were replayed from the live
        // tree. They are the parse's now, as CPython queues them: a change to the tree changes no event.
        [Fact]
        public void IterparseHandsOutTheParsesEvents()
        {
            string rec = "srecsvevswewerec";
            Assert.Equal("['0:0', '1:1', '2:2', '3:3', '4:4', '5:5', '6:6', '7:7', '8:8', '9:9', '10:10', '11:11'] 0\n"
                + "sroot" + string.Concat(System.Linq.Enumerable.Repeat(rec, 12)) + "eroot\n"
                + "['a2', 'b0', 'c1', 'd0']\n[('end', 'b'), ('end', 'a')]\n",
                Out(Doc + "seen = []\nctx = ET.iterparse(io.StringIO(doc), events=('start', 'end'))\nev0, root = next(ctx)\n"
                + "for ev, el in ctx:\n    if ev == 'end' and el.tag == 'rec':\n"
                + "        seen.append(el.get('id') + ':' + el.findtext('v', ''))\n        root.clear()\nprint(seen, len(root))\n"
                + "seen = []\nfor ev, el in ET.iterparse(io.StringIO(doc), events=('start', 'end')):\n    seen.append(ev[0] + el.tag)\n"
                + "    if ev == 'start' and el.tag == 'rec':\n        el.append(ET.Element('added'))\n"
                + "        if el.get('id') == '3':\n            el.clear()\nprint(''.join(seen))\n"
                + "print(['%s%d' % (el.tag, len(el)) for ev, el in ET.iterparse(io.StringIO('<a><b/><c><d/></c></a>'), events=('start',))])\n"
                + "print([(e, x.tag) for e, x in ET.iterparse(io.StringIO('<a><b/></a>'))])\n"));
        }

        // An element held an attrib dict from birth (~290 of its 392 bytes, 160 charged: 2M elements peaked at 700 MB under
        // 256 MB). It is made when asked for, as CPython's `extra` is; what CPython answers about it is unchanged.
        [Fact]
        public void AttributesAreMadeWhenAskedFor()
        {
            Assert.Equal("None 1\n[('a', '1'), ('b', '2')]\n9 True ['z']\nFalse {} {'z': '9'}\n[] [] dflt\n[('p', '1')] []\n"
                + "b'<a x=\"1\"><b /><c y=\"2\" /></a>'\n"
                + "[{'{u}t': '1'}, {}] {} b'<r xmlns:ns0=\"u\"><s ns0:t=\"1\" /><s /></r>'\n['s']\nm b'<t n=\"m\" />'\n['1']\n",
                Out("import xml.etree.ElementTree as ET, copy\n"
                + "e = ET.Element('x')\nc = copy.copy(e)\ne.set('a', '1')\nprint(c.get('a'), e.get('a'))\n"
                + "e2 = ET.Element('x', {'a': '1'})\nc2 = copy.copy(e2)\ne2.set('b', '2')\nprint(sorted(c2.items()))\n"
                + "e3 = ET.Element('x')\nd = e3.attrib\nd['z'] = '9'\nprint(e3.get('z'), e3.attrib is d, e3.keys())\n"
                + "e3.clear()\nprint(e3.attrib is d, e3.attrib, d)\n"
                + "e4 = ET.Element('x', {})\nprint(e4.items(), e4.keys(), e4.get('q', 'dflt'))\n"
                + "e5 = e4.makeelement('y', {})\ne5.set('p', '1')\nprint(e5.items(), e4.items())\n"
                + "print(ET.tostring(copy.deepcopy(ET.fromstring('<a x=\"1\"><b/><c y=\"2\"/></a>'))))\n"
                + "r = ET.fromstring('<r xmlns:q=\"u\"><s q:t=\"1\"/><s/></r>')\nprint([x.attrib for x in r], r.attrib, ET.tostring(r))\n"
                + "print([x.tag for x in r.findall('*[@{u}t]')])\n"
                + "el = ET.Element('t')\nel.attrib = {'n': 'm'}\nprint(el.get('n'), ET.tostring(el))\n"
                + "x1, ids = ET.XMLID('<r><a id=\"1\"/><b/></r>')\nprint(sorted(ids))\n"));
            // deepcopy copies the attrib dict and, as CPython's create_new_element, keeps it only when it is not empty
            Assert.Equal("None False\n", Out("import xml.etree.ElementTree as ET, copy\ne = ET.Element('a')\n"
                + "d, c = copy.deepcopy([e.attrib, e])\nd['k'] = 'v'\nprint(c.get('k'), c.attrib is d)\n"));
        }

        // The charge follows what the CLR holds: an element without attributes measured 112 bytes (120 charged), one with
        // an attribute 432 (348 charged). A million elements fit 160 MB; 1.2.0 charged 160 per element and held 392.
        [Fact]
        public void ElementsAreChargedAsTheyWeigh()
        {
            RunResult plain = Run("import xml.etree.ElementTree as ET\nr = ET.Element('r')\nfor i in range(100):\n"
                + "    p = ET.SubElement(r, 'p')\n    for j in range(1000):\n        ET.SubElement(p, 'c')\n");
            RunResult attr = Run("import xml.etree.ElementTree as ET\nr = ET.Element('r')\nfor i in range(100):\n"
                + "    p = ET.SubElement(r, 'p')\n    for j in range(1000):\n        ET.SubElement(p, 'c', id='x')\n");
            Assert.True(plain.Succeeded && attr.Succeeded);
            Assert.InRange(plain.Stats.AllocatedBytes / 100_000, 100, 180);
            Assert.InRange(attr.Stats.AllocatedBytes / 100_000, 300, 520);
            Assert.Equal("1000000\n", Out("import xml.etree.ElementTree as ET\nr = ET.Element('r')\nfor i in range(200):\n"
                + "    p = ET.SubElement(r, 'p')\n    for j in range(5000):\n        ET.SubElement(p, 'c')\n"
                + "print(sum(len(p) for p in r))\n", 160L << 20));
        }

        // Without the parent map (each element in one list, no script code run yet), and with it once an element is in two
        // lists, moved, or copied: the same answers.
        [Fact]
        public void PositionsAnswerAsCPythonWithAndWithoutTheMap()
        {
            Assert.Equal("shared ['1', '3'] ['2', '2'] ['2', '2'] ['2']\nmoved ['2', '1'] ['2', '3'] ['2', '3']\n"
                + "copied ['2'] ['3'] ['2']\ndeep ['s', 's'] ['s', 's'] ['t'] ['s', 's']\n"
                + "mixed ['2', '4', '5'] ['0'] ['3'] ['2', '3'] ['1'] [] 4\nloop 0\n",
                Out("import xml.etree.ElementTree as ET, copy\nids = lambda es: [e.get('id') for e in es]\n"
                + "r = ET.fromstring('<r><p><a id=\"1\"/><a id=\"2\"/></p><q><a id=\"3\"/></q></r>')\np, q = r\nq.append(p[1])\n"
                + "print('shared', ids(r.findall('*/a[1]')), ids(r.findall('*/a[last()]')), ids(r.findall('.//a[2]')), ids(r.findall('q/a[2]')))\n"
                + "r = ET.fromstring('<r><p><a id=\"1\"/><a id=\"2\"/></p><q><a id=\"3\"/></q></r>')\np, q = r\nx = p[0]\np.remove(x)\n"
                + "q.insert(0, x)\nprint('moved', ids(r.findall('*/a[1]')), ids(r.findall('*/a[last()]')), ids(r.findall('.//a[last()]')))\n"
                + "r = ET.fromstring('<r><a id=\"1\"/><a id=\"2\"/></r>')\nc = copy.copy(r)\nc.append(ET.Element('a', id='3'))\n"
                + "print('copied', ids(r.findall('a[last()]')), ids(c.findall('a[last()]')), ids(c.findall('a[2]')))\n"
                + "s = ET.Element('a', id='s')\nr = ET.Element('r')\nr.extend([s, ET.Element('a', id='t'), s])\nd = copy.deepcopy(r)\n"
                + "print('deep', ids(d.findall('a[1]')), ids(d.findall('a[last()]')), ids(d.findall('a[2]')), ids(r.findall('a[last()]')))\n"
                + "r = ET.fromstring('<r><b id=\"0\"/><a id=\"1\"/><b id=\"2\"/><a id=\"3\"/><c id=\"4\"/><a id=\"5\"/></r>')\n"
                + "print('mixed', ids(r.findall('*[last()]')), ids(r.findall('b[last()-1]')), ids(r.findall('a[2]')), ids(r.findall('*[2]')),\n"
                + "      ids(r.findall('a[last()-2]')), ids(r.findall('a[4]')), r.find('c[last()]').get('id'))\n"
                + "root = ET.Element('t')\nbad = 0\nfor i in range(300):\n    g = ET.SubElement(root, 'g', id='g%d' % i)\n"
                + "    ET.SubElement(g, 'row', id=str(i))\n"
                + "    if root.find('g[last()]/row[1]').get('id') != str(i) or root.find('.//row[1]').get('id') != '0' or root.find('g[1]').get('id') != 'g0':\n"
                + "        bad += 1\nprint('loop', bad)\n"));
        }

        // Adding a row and finding row[last()] built and charged a map of every row per search: 2,900 rows ended on
        // MaxAllocBytes (1.1.0 ran 6,000 on 18M steps). No map now, and the rows before the last are not checked one by
        // one; the search still groups the rows by tag, a step a row, as 1.1.0 paid.
        [Fact]
        public void AnAddAndFindLastLoopBuildsNoMap()
        {
            Assert.Equal("6000 0\n", Out("import xml.etree.ElementTree as ET\nroot = ET.Element('t')\nbad = 0\nfor i in range(6000):\n"
                + "    ET.SubElement(root, 'row', id=str(i))\n    if root.find('row[last()]').get('id') != str(i):\n        bad += 1\n"
                + "print(len(root), bad)\n", 16L << 20, 25_000_000));
            Assert.Equal("2000 0\n", Out("import xml.etree.ElementTree as ET\nroot = ET.Element('t')\ng = ET.SubElement(root, 'g')\n"
                + "bad = 0\nfor i in range(2000):\n    ET.SubElement(g, 'row', id=str(i))\n"
                + "    if root.find('.//row[1]').get('id') != '0':\n        bad += 1\nprint(len(g), bad)\n", 16L << 20));
        }

        // A map is scratch: held while a search or the run's cache keeps it, charged at the most held at once. A search
        // that needs one after each change (an element in two lists) charged every build: 200 of 20,000 elements, 192 MB.
        [Fact]
        public void ParentMapsAreGivenBackAfterTheSearch()
        {
            Assert.Equal("200\n", Out("import xml.etree.ElementTree as ET\nr = ET.Element('r')\nfor i in range(20000):\n"
                + "    ET.SubElement(r, 'c')\nr.append(r[0])\nn = 0\nfor i in range(200):\n    ET.SubElement(r, 'c')\n"
                + "    n += len(r.findall('c[last()]'))\nprint(n)\n", 32L << 20));
        }

        // An iterfind left part way keeps its map: those over a tree that changed in between are each charged a map and
        // refused under 64 MB (a row appended is one entry more in the one map they share, which fits). 1,500 rounds, not
        // more: the appending loop then stays far from the deadline in a loaded parallel run. It keeps no sibling
        // groups between items (review 10, B-2): 40 kept `.//*[2]` walks held 1.9 GB of groups at the fix's first cut,
        // then were charged for them; they hold and are charged next to nothing now.
        [Fact]
        public void AbandonedIterfindsKeepTheirMapsCharged()
        {
            string kept = "import xml.etree.ElementTree as ET\nr = ET.Element('r')\nfor i in range(100000):\n"
                + "    ET.SubElement(r, 'c')\nits = []\nfor i in range(1500):\n    GROW\n"
                + "    it = r.iterfind('c[1]')\n    next(it)\n    its.append(it)\nprint(len(its))\n";
            Refused("MaxAllocBytes", kept.Replace("GROW", "r.insert(0, ET.Element('c'))"), 64L << 20);
            Assert.Equal("1500\n", Out(kept.Replace("GROW", "ET.SubElement(r, 'c')"), 64L << 20));
            Assert.Equal("40\n", Out("import xml.etree.ElementTree as ET\nr = ET.Element('r')\nfor i in range(20000):\n"
                + "    ET.SubElement(ET.SubElement(r, 'p%d' % i), 'c')\nET.SubElement(r[-1], 'c')\nits = []\nfor k in range(40):\n"
                + "    it = r.iterfind('.//*[2]')\n    next(it)\n    its.append(it)\nprint(len(its))\n", 24L << 20));
        }

        // \UHHHHHHHH went to .NET as written, which has no such escape: "invalid regular expression". It is the character
        // now, an astral one grouped so a quantifier takes both halves.
        [Fact]
        public void WideEscapesAreTheCharacters()
        {
            string u = B + "U";
            Assert.Equal("['x\\U0001f600y', 'xy', 'xy'] ['x\\U0001f600\\U0001f600y'] ['abc', 'a'] ['\\xe9', '\\xc9'] ['a', 'A', 'a']\n"
                + "bad escape \\U00110000 at position 0\nincomplete escape \\U0000006 at position 0\n",
                Out("import re\ns = 'abc x" + u + "0001F600y xy x" + u + "0001F600" + u + "0001F600y xy " + u + "000000e9" + u + "000000c9 Aa'\n"
                + "print(ascii(re.findall(r'x" + u + "0001F600?y', s)), ascii(re.findall(r'x" + u + "0001F600{2}y', s)), "
                + "re.findall(r'[" + u + "00000061-" + u + "00000063]+', s), ascii(re.findall(r'" + u + "000000e9', s, re.I)), "
                + "re.findall(r'[" + u + "00000041]', s, re.I))\n"
                + "for p in [r'" + u + "00110000', r'" + u + "0000006']:\n    try:\n        re.compile(p)\n"
                + "    except re.error as e:\n        print(e)\n"));
            // An astral one in a class stays refused (deviations.md: astral ranges), never two members: a range to one
            // from a BMP character matched the wrong set (CPython: 'ab').
            Assert.Equal("error\nerror\n", Out("import re\nfor p in [r'[" + B + "x80-" + u + "0010ffff]', r'[" + u + "0001F600]']:\n"
                + "    try:\n        print(re.sub(p, '', 'a" + B + "uff01b'))\n    except re.error:\n        print('error')\n"));
        }

        // A match is a step per eight (the deadline is still read at each): findall and sub took 3-5 times 1.1.0's steps.
        [Fact]
        public void MatchesPayAStepPerEight()
        {
            RunResult r = Run("import re\nre.findall('a', 'a' * 80000)\n");
            Assert.True(r.Succeeded);
            Assert.InRange(r.Stats.Steps, 80000 / 8, 80000 / 8 + 2000);
        }
    }
}
