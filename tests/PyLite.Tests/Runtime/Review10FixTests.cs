using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // Review 10 (the last before 1.2.0): false limits and charges of the build under review. Each answer is CPython 3.14.2's; a limit is
    // proved by the limit that ends the run or by the run fitting one that build refused, never by timing. The XPath cases
    // run each scenario clean and "poisoned" (an earlier move, sort, share or copy): the same answers, the same costs.
    [Trait("Category", "Regression")]
    public class Review10FixTests
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

        private const string Loop = "import xml.etree.ElementTree as ET, copy\n"
            + "doc = ET.Element('doc')\ndraft = ET.SubElement(doc, 'draft')\ntable = ET.SubElement(doc, 'table')\n"
            + "other = ET.Element('other')\nfirst = ET.SubElement(draft, 'row', n='-1')\nkeep = None\n";

        // B-1: the fast path stayed off for the rest of the run after one move (a mark that never cleared): 6,000 rounds of
        // add-a-row + find row[last()] after a move ran to the deadline (1.1.0: 1.5 s, 18M steps). Each prelude is a
        // different earlier change; the loop's answers are CPython's, its steps 1.1.0's or fewer.
        [Theory]
        [InlineData("none", "")]
        [InlineData("move", "draft.remove(first)\ntable.append(first)\n")]
        [InlineData("sort", "for i in range(5):\n    ET.SubElement(table, 'row', n=str(9 - i))\ntable[:] = sorted(table, key=lambda e: e.get('n'))\n")]
        [InlineData("shared_elsewhere", "other.append(first)\n")]
        [InlineData("copy_elsewhere", "keep = copy.copy(draft)\n")]
        [InlineData("deepcopy", "keep = copy.deepcopy(doc)\n")]
        [InlineData("replace_and_delete", "draft[0] = ET.Element('row', n='-2')\ndel draft[0]\ntable.extend([first])\n")]
        public void AnEarlierChangeLeavesTheAddAndFindLoopFast(string name, string prelude)
        {
            Assert.Equal("0\n", Out(Loop + prelude + "bad = 0\nfor i in range(6000):\n    row = ET.SubElement(table, 'row', n='r%d' % i)\n"
                + "    if table.find('row[last()]') is not row:\n        bad += 1\nprint(bad)\n", 16L << 20, 20_000_000));
            Assert.NotNull(name);
        }

        // The answers of both searches after each prelude are CPython's (300 rounds; `.//row[last()]` finds draft's row
        // first where it is still there).
        [Fact]
        public void PoisonedTreesAnswerAsCPython()
        {
            string[] preludes =
            {
                "", "draft.remove(first)\ntable.append(first)\n",
                "for i in range(5):\n    ET.SubElement(table, 'row', n=str(9 - i))\ntable[:] = sorted(table, key=lambda e: e.get('n'))\n",
                "other.append(first)\n", "keep = copy.copy(draft)\n", "keep = copy.deepcopy(doc)\n",
            };
            string[] expected =
            {
                "300 300 r0 2", "301 0 -1 1", "305 300 5 2", "300 300 r0 2", "300 300 r0 2", "300 300 r0 2",
            };
            for (int k = 0; k < preludes.Length; k++)
            {
                Assert.Equal(expected[k] + "\n", Out(Loop + preludes[k] + "bad = 0\nfor i in range(300):\n"
                    + "    row = ET.SubElement(table, 'row', n='r%d' % i)\n"
                    + "    if table.find('row[last()]') is not row or doc.find('.//row[last()]') is not row:\n        bad += 1\n"
                    + "print(len(table), bad, table.find('row[1]').get('n'), len(doc.findall('.//row[1]')))\n"));
            }
        }

        // A-1/B-3: one positional search kept the sibling groups of every parent it passed, 240 B and more each: 270,000
        // records ended on MaxAllocBytes (1.1.0 searched them in 3 MB). Small parents are counted in place now, and a big
        // one's groups go back when the walk leaves it: the searches cost next to nothing over the parse.
        [Fact]
        public void PositionalSearchesHoldNoGroupsPerParent()
        {
            const string Parse = "import xml.etree.ElementTree as ET\nr = ET.fromstring('<r>' + '<p><ph>1</ph><ph>2</ph></p>' * 60000"
                + " + ('<big>' + '<x/>' * 5000 + '</big>') * 40 + '</r>')\n";
            RunResult parse = Run(Parse);
            RunResult search = Run(Parse + "print(len(r.findall('p/ph[1]')), len(r.findall('.//ph[last()]')), len(r.findall('*/*[2]')), "
                + "len(r.findall('big/x[last()]')), len(r.findall('.//x[1]')))\n");
            Assert.True(parse.Succeeded && search.Succeeded, search.Error == null ? "" : search.Error.Message);
            Assert.Equal("60000 60000 60040 40 40\n", search.PrintOutput);
            Assert.InRange(search.Stats.AllocatedBytes - parse.Stats.AllocatedBytes, 0, 3L << 20);
        }

        // B-2: the first-match idiom `next(rec.iterfind(path), None)` per record kept each dropped walk's sibling groups
        // charged (1,000 B a call; 240,000 calls past 256 MB). A walk holds no groups between items now, and a walk that
        // cannot descend is charged no descent state: 77 MB (1.1.0: 58, the build under review: 108).
        [Fact]
        public void TheFirstMatchIdiomKeepsNoGroups()
        {
            RunResult r = Run("import xml.etree.ElementTree as ET\n"
                + "root = ET.fromstring('<d>' + ''.join('<rec><f>%d</f><f>%d</f><g>x</g><f>9</f></rec>' % (i, i + 1) for i in range(20000)) + '</d>')\n"
                + "s = 0\nfor k in range(12):\n    for rec in root:\n        s += int(next(rec.iterfind('f[2]'), None).text)\nprint(s)\n");
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName);
            Assert.Equal("2400120000\n", r.PrintOutput);
            Assert.InRange(r.Stats.AllocatedBytes, 0, 96L << 20);
        }

        // C-1: a compiled pattern kept in a list was charged 40 B for ~1.5 KB held (100,000 grew the process 1.6 GB under
        // 64 MB). Live patterns are charged at their high-water mark: kept ones meet the limit, dropped ones do not add up.
        // The dropped loop's charge follows the CLR's collections (10 MB alone, 310 MB if all were held): hence 64.
        [Fact]
        public void KeptPatternsAreChargedDroppedOnesAreNot()
        {
            Refused("MaxAllocBytes", "import re\nkeep = [re.compile('a%d' % i) for i in range(50000)]\n", 32L << 20);
            Assert.Equal("1\n", Out("import re\nn = 0\nfor i in range(100000):\n    if re.compile('b%d' % i).match('b1'):\n"
                + "        n += 1\nprint(n)\n", 64L << 20));
        }

        // C-2: a key's __eq__ that adds and deletes another key restarted the probe for ever (DeadlineMs; 1.1.0 and CPython
        // answer False). CPython restarts only when the table is rebuilt or that entry changed.
        [Fact]
        public void AKeyEqThatChangesTheTableStillAnswers()
        {
            Assert.Equal("dict False 2\nset False 2\nfound a True\n", Out("d = {}\nclass K:\n    def __init__(self, n):\n        self.n = n\n"
                + "    def __hash__(self):\n        return 1\n    def __eq__(self, other):\n        d['x%d' % self.n] = 1\n"
                + "        del d['x%d' % self.n]\n        return False\nd[K(0)] = 1\nd[K(1)] = 2\nprint('dict', K(2) in d, len(d))\n"
                + "s = set()\nclass S:\n    def __hash__(self):\n        return 7\n    def __eq__(self, other):\n        s.add('y')\n"
                + "        s.discard('y')\n        return False\ns.add(S())\ns.add(S())\nprint('set', S() in s, len(s))\n"
                + "d2 = {1: 'a'}\nclass Same:\n    def __hash__(self):\n        return hash(1)\n    def __eq__(self, other):\n"
                + "        return other == 1\nprint('found', d2.get(Same()), Same() in d2)\n"));
            // A delete whose __eq__ deletes and re-adds the key: it lands in the same trimmed entry at another index
            // position, which the first cut of the rule above marked dead through the stale one (an engine fault next).
            Assert.Equal("1 False True\n", Out("done = []\nclass H:\n    def __init__(s, n): s.n = n\n    def __hash__(s): return 0\n"
                + "    def __eq__(s, o):\n        if s.n == 'S' and not done:\n            done.append(1); del d[s]; d[s] = 's'\n"
                + "        return s.n in 'SK' and o.n in 'SK'\nA, S, K = H('A'), H('S'), H('K')\nd = {A: 1, 3: 'b', S: 2}\n"
                + "del d[A]\ndel d[K]\nprint(len(d), S in d, list(d) == [3])\n"));
        }
    }
}
