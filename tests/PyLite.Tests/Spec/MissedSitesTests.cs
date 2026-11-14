using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Spec
{
    // Review 3, P4 (G1-G9): sites the classes fixed in rounds 1-2 left behind. Expected values are CPython 3.14's.
    [Trait("Category", "Regression")]
    public class MissedSitesTests
    {
        private const string Dq = "from collections import deque\n";

        [Theory]
        [InlineData("d = deque([1, 2, 3])\nfor x in d:\n    d[0] = 7\nr = list(d)\n", "[7, 2, 3]")]
        [InlineData("d = deque([5])\nfor x in d:\n    d.rotate(1)\n    d.reverse()\nr = list(d)\n", "[5]")]
        [InlineData("class K:\n    def __eq__(self, o):\n        d[0] = 9\n        return False\n    __hash__ = None\nd = deque([1, 2])\nr = (K() in d, d.count(K()))\n", "(False, 0)")]
        public void ADequeCountsTheMutationsCPythonCounts(string src, string expected)   // G1
        {
            Assert.Equal(expected, Ev.Run(Dq + src).Repr("r"));
        }

        [Fact]
        public void ARotationOfTwoItemsIsAMutation()
        {
            Assert.Equal("deque mutated during iteration", Ev.Err(Dq + "d = deque([1, 2])\nfor x in d:\n    d.rotate(0)\n"));
        }

        // G2: a million f-strings and a million caught KeyErrors were 256 MB and 101 MB of budget for what stays
        // 26 MB of working set, so the first failed the default limit. Measured per turn over 100,000 turns: an
        // f-string must fit a million in 256 MB, a caught KeyError is a third of what it was, a kept one is charged.
        [Fact]
        public void SmallTemporariesFitTheDefaultBudget()
        {
            var limits = TestEngines.Unhurried;
            var engine = TestEngines.WithCeiling(limits);
            RunResult fs = engine.Run("x = [1, 2, 3]\nfor i in range(100000):\n    s = f'{i}:{x}'\n", new RunRequest { Limits = limits });
            Assert.True(fs.Succeeded, fs.Error == null ? "" : fs.Error.Message);
            Assert.True(fs.Stats.AllocatedBytes < (256L << 20) / 10, fs.Stats.AllocatedBytes.ToString());
            RunResult ke = engine.Run("d = {}\nfor i in range(100000):\n    try:\n        d[i]\n    except KeyError:\n        pass\n",
                new RunRequest { Limits = limits });
            Assert.True(ke.Succeeded, ke.Error == null ? "" : ke.Error.Message);
            Assert.True(ke.Stats.AllocatedBytes < 6L << 20, ke.Stats.AllocatedBytes.ToString());
            RunResult kept = engine.Run("d = {}\nk = []\nfor i in range(100000):\n    try:\n        d[i]\n    except KeyError as e:\n        k.append(e)\n",
                new RunRequest { Limits = limits });
            Assert.True(kept.Succeeded, kept.Error == null ? "" : kept.Error.Message);
            Assert.True(kept.Stats.AllocatedBytes - ke.Stats.AllocatedBytes > 64L * 100000, kept.Stats.AllocatedBytes.ToString());
        }

        [Theory]
        [InlineData("\U0001d413\U0001d42b\U0001d42e\U0001d41e = 5\n", "identifier field can't represent 'True' constant")]
        [InlineData("x = \U0001d40d\U0001d428\U0001d427\U0001d41e\n", "identifier field can't represent 'None' constant")]
        public void ANormalizedNameIsNeverAKeyword(string src, string message)   // G3
        {
            var ex = Assert.Throws<PySyntaxErrorException>(() => Ev.Run(src));
            Assert.Contains(message, ex.Message);
        }

        [Fact]
        public void ABoldMatchIsNoMatchStatement()
        {
            Assert.Throws<PySyntaxErrorException>(() => Ev.Run("\U0001d426\U0001d41a\U0001d42d\U0001d41c\U0001d421 5:\n    case 5: pass\n"));
        }

        [Fact]
        public void ANormalizedNameIsAName()
        {
            EvResult r = Ev.Run("\U0001d426\U0001d41a\U0001d42d\U0001d41c\U0001d421 = 7\n\U0001d422\U0001d41f = 3\nr = (match, \U0001d422\U0001d41f)\n");
            Assert.Equal("(7, 3)", r.Repr("r"));
        }

        [Fact]
        public void ALongCombiningNameKeepsItsSurrogatePairs()
        {
            string name = "\u210c" + string.Concat(System.Linq.Enumerable.Repeat("\U000101fd", 5000));
            Assert.Equal("1", Ev.Run(name + " = 1\nr = " + name + "\n").Repr("r"));
        }

        [Theory]
        [InlineData("'a b'.split(None, 1, 5)", "split() takes at most 2 arguments (3 given)")]
        [InlineData("'ab'.encode('utf-8', 'strict', 99)", "encode() takes at most 2 arguments (3 given)")]
        [InlineData("statistics.mean([1], [2])", "mean() takes 1 positional argument but 2 were given")]
        [InlineData("itertools.count(0, 1, 5)", "count() takes at most 2 arguments (3 given)")]
        [InlineData("random.shuffle([1, 2], random.random)", "Random.shuffle() takes 2 positional arguments but 3 were given")]
        public void ExtraArgumentsAreRefused(string expr, string message)   // G4, G6
        {
            Assert.Equal(message, Ev.Err("import statistics, itertools, random\n" + expr + "\n"));
        }

        [Theory]
        [InlineData("namedtuple('P', 'a\\x1cb')._fields", "('a', 'b')")]
        [InlineData("float.fromhex(' 0x1p0\\n')", "1.0")]
        [InlineData("urllib.parse.unwrap('\\x1c<URL:x>\\x1c')", "'x'")]
        [InlineData("time.strptime('2020\\u2003 01', '%Y %m')[:2]", "(2020, 1)")]
        [InlineData("statistics.mean(data=[1, 2])", "1.5")]
        public void PythonWhitespaceAndKeywords(string expr, string expected)   // G5
        {
            Assert.Equal(expected, Ev.ReprOf("from collections import namedtuple\nimport urllib.parse, time, statistics\n", expr));
        }

        [Theory]
        [InlineData("uuid.UUID(' 12345678123456781234567812345678 ')", "badly formed hexadecimal UUID string")]
        [InlineData("float.fromhex('\\xa00x1p0')", "invalid hexadecimal floating-point string")]
        [InlineData("struct.pack('<f', 2**200)", "int too large to convert")]
        [InlineData("struct.pack('<f', 1e60)", "float too large to pack with f format")]
        public void ConversionsRefuseAsCPython(string expr, string message)   // G5, G6
        {
            Assert.Equal(message, Ev.Err("import uuid, struct\n" + expr + "\n"));
        }

        [Fact]
        public void CopyingADictNeverComparesItsKeys()   // G7
        {
            const string src = "class C:\n    n = 0\n    def __hash__(self):\n        return 1\n    def __eq__(self, o):\n        C.n += 1\n        return self is o\n"
                + "a = {C(): i for i in range(6)}\nC.n = 0\nb = dict(a)\nr = (C.n, len(b))\n";
            Assert.Equal("(0, 6)", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void ASourceEmptiedDuringAnUpdateIsAnError()
        {
            const string src = "class M:\n    def __init__(self, t):\n        self.t = t\n    def __hash__(self):\n        return 1\n"
                + "    def __eq__(self, o):\n        self.t.clear()\n        return False\n"
                + "x = {}\ny = {}\nx[M(y)] = 1\ny[M(y)] = 2\ny[M(y)] = 3\nx | y\n";
            Assert.Equal("dict mutated during update", Ev.Err(src));
        }

        [Fact]
        public void AViewIntersectionKeepsTheSmallerSidesKeys()
        {
            const string src = "class K:\n    def __init__(self, v, tag):\n        self.v, self.tag = v, tag\n    def __hash__(self):\n        return hash(self.v)\n"
                + "    def __eq__(self, o):\n        return self.v == o.v\n"
                + "big = {K(1, 'big'): 0, K(2, 'big'): 0, K(3, 'big'): 0}\nsmall = {K(2, 'small'): 0}\n"
                + "r = [k.tag for k in big.keys() & small.keys()] + [k.tag for k in small.keys() & big.keys()]\n";
            Assert.Equal("['small', 'small']", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void Uuid1NeverRepeatsAcrossParallelSeededRuns()   // G8
        {
            var engine = new ScriptEngine(new EngineOptions());
            var seen = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
            System.Threading.Tasks.Parallel.For(0, 160, i =>
            {
                RunResult r = engine.Run("import random, uuid\nrandom.seed(7)\nu = str(uuid.uuid1())\n",
                    new RunRequest { ScalarOutputNames = new[] { "u" } });
                Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Message);
                seen[r.ScalarsOut["u"]] = true;
            });
            Assert.Equal(160, seen.Count);
        }

        // G9: hash() of a class, an enum, a builtin or an exception class never depends on what other runs made.
        [Fact]
        public void AHashTellsNothingOfOtherRuns()
        {
            var engine = new ScriptEngine(new EngineOptions());
            const string reader = "import enum, math\nclass A: pass\nclass E(enum.Enum):\n    X = 1\nclass Err(Exception): pass\n"
                + "r = (hash(A), hash(E), hash(Err), hash(math.sqrt), hash(len), hash(int))\n";
            var request = new RunRequest { ScalarOutputNames = new[] { "r" } };
            string before = engine.Run(reader, request).ScalarsOut["r"];
            engine.Run("import enum\nfor i in range(300):\n    class X: pass\n    class Y(enum.Enum):\n        A = 1\n    class Z(Exception): pass\n", null);
            Assert.Equal(before, engine.Run(reader, request).ScalarsOut["r"]);
        }
    }
}
