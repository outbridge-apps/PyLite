using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-17/18/19: copy.copy, copy.Error, copy.deepcopy.
    [Trait("Category", "Compatibility")]
    public class CopyTests
    {
        private const string P = "import copy\nfrom collections import Counter, defaultdict, deque, OrderedDict, namedtuple\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        [Theory]
        // ---- copy.copy ----
        [InlineData("l = [1,2,3]\nc = copy.copy(l)\nr = (c is l, c == l)", "(False, True)")]
        [InlineData("t = (1,2)\nr = (copy.copy(t) is t)", "True")]
        [InlineData("r = (copy.copy(5) is 5)", "True")]
        [InlineData("r = copy.copy('abc')", "'abc'")]
        [InlineData("d = {'a':[1]}\nc = copy.copy(d)\nr = (c['a'] is d['a'])", "True")]
        [InlineData("dd = defaultdict(list)\nc = copy.copy(dd)\nr = (c.default_factory is dd.default_factory)", "True")]
        [InlineData("q = deque([1,2], maxlen=3)\nc = copy.copy(q)\nr = (list(c), c.maxlen)", "([1, 2], 3)")]
        [InlineData("P = namedtuple('P','x')\np = P(1)\nr = (copy.copy(p) is p)", "True")]
        [InlineData("od = OrderedDict([('a',1),('b',2)])\nr = list(copy.copy(od))", "['a', 'b']")]
        [InlineData("s = {1,2}\nc = copy.copy(s)\nr = (c is s, c == s)", "(False, True)")]
        [InlineData("c = copy.copy(Counter(a=1))\nr = dict(c)", "{'a': 1}")]
        // ---- copy.deepcopy ----
        [InlineData("l = [1,[2,3]]\nc = copy.deepcopy(l)\nr = (c == l, c[1] is l[1])", "(True, False)")]
        [InlineData("l = []\nl.append(l)\nc = copy.deepcopy(l)\nr = (c[0] is c)", "True")]
        [InlineData("a = [1]\nb = [a,a]\nc = copy.deepcopy(b)\nr = (c[0] is c[1], c[0] is a)", "(True, False)")]
        [InlineData("t = (1,2,3)\nr = (copy.deepcopy(t) is t)", "True")]
        [InlineData("t = (1,[2])\nc = copy.deepcopy(t)\nr = (c is t, c[1] is t[1])", "(False, False)")]
        [InlineData("d = {}\nd['self'] = d\nc = copy.deepcopy(d)\nr = (c['self'] is c)", "True")]
        [InlineData("dd = defaultdict(list, {'a':[1]})\nc = copy.deepcopy(dd)\nr = (c.default_factory is dd.default_factory, c['a'] is dd['a'])", "(True, False)")]
        [InlineData("q = deque([1,2], maxlen=5)\nc = copy.deepcopy(q)\nr = (list(c), c.maxlen)", "([1, 2], 5)")]
        [InlineData("f = lambda: 1\nr = (copy.deepcopy(f) is f)", "True")]
        // (deepcopy preserves the namedtuple type — verified via _fields, since type() maps by name here)
        [InlineData("P = namedtuple('P','x')\np = P([1])\nc = copy.deepcopy(p)\nr = (c._fields == ('x',), c[0] is p[0])", "(True, False)")]
        [InlineData("c = copy.deepcopy(Counter(a=1,b=2))\nr = dict(c)", "{'a': 1, 'b': 2}")]
        public void CopyAndDeepCopy(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Fact]
        public void ErrorIsCatchableException()
        {
            string body = "caught = ''\ntry:\n    raise copy.Error('x')\nexcept copy.Error as e:\n    caught = str(e)\nr = caught\n";
            Assert.Equal("'x'", Body(body));
        }

        [Fact]
        public void ErrorIsExceptionSubclass()
        {
            string body = "caught = ''\ntry:\n    raise copy.Error('x')\nexcept Exception:\n    caught = 'caught'\nr = caught\n";
            Assert.Equal("'caught'", Body(body));
        }

        [Fact]
        public void CounterSubtypePreservedOnCopy()
        {
            // A copied Counter keeps Counter behavior: a miss returns 0 (a plain dict would KeyError).
            Assert.Equal("0", Body("c = copy.copy(Counter(a=1))\nr = c['zzz']"));
        }

        [Fact]
        public void DeepDiamondDoesNotExplode()
        {
            // A DAG of depth 20: memo dedups, so this is linear, not 2**20 nodes.
            string body = "x = [0]\nfor _ in range(20):\n    x = [x, x]\nc = copy.deepcopy(x)\nr = (c[0] is c[1])\n";
            Assert.Equal("True", Body(body));
        }

        [Fact]
        public void DeepStackDoesNotGrow()
        {
            // 100k-deep nesting copies iteratively without a C# stack overflow.
            string body = "x = []\ncur = x\nfor _ in range(100000):\n    n = []\n    cur.append(n)\n    cur = n\n"
                + "c = copy.deepcopy(x)\nr = 'ok'\n";
            Assert.Equal("'ok'", Body(body));
        }
    }
}
