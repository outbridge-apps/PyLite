using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    // P4 (1.1.0): a dict/set comprehension over distinct items starts at its final size, a table never
    // outgrows MaxCollectionItems, and an insert takes the lookup's probe instead of walking it twice.
    [Trait("Category", "Compatibility")]
    public class TablePresizeTests
    {
        private static RunResult Run(string src, ResourceLimits limits = null)
        {
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = limits ?? ResourceLimits.Default });
            return engine.Run(src, new RunRequest { ScalarOutputNames = new[] { "r" } });
        }

        [Theory]
        [InlineData("r = {i: i for i in range(1500)}")]
        [InlineData("r = {i for i in range(1500)}")]
        [InlineData("r = {i % 1500: i for i in range(3000) if i >= 0}")]
        [InlineData("r = set(range(1500))")]
        [InlineData("r = dict.fromkeys(range(1500))")]
        [InlineData("r = {}\nfor i in range(1500):\n    r[i] = i")]
        [InlineData("r = set()\nfor i in range(1500):\n    r.add(str(i))")]
        public void ATableStopsOnMaxCollectionItems(string src)
        {
            RunResult r = Run(src, ResourceLimits.Default.With(maxCollectionItems: 1000));
            Assert.False(r.Succeeded);
            Assert.Equal("MaxCollectionItems", r.Error.LimitName);
        }

        [Fact]
        public void ATableAtTheLimitIsFine()
        {
            RunResult r = Run("d = {i: i for i in range(1000)}\nd[0] = 'update'\nr = len(d)", ResourceLimits.Default.With(maxCollectionItems: 1000));
            Assert.True(r.Succeeded, r.Error?.Message);
            Assert.Equal("1000", r.ScalarsOut["r"]);
        }

        [Fact]
        public void RepeatedKeysAreNotPresized()
        {
            // 100,000 items but three keys: a presized table would charge megabytes and keep them
            RunResult dupes = Run("s = {x % 3 for x in range(100000)}\nr = len(s)");
            RunResult distinct = Run("s = {x for x in range(100000)}\nr = len(s)");
            Assert.True(dupes.Succeeded && distinct.Succeeded);
            Assert.Equal("3", dupes.ScalarsOut["r"]);
            Assert.True(dupes.Stats.AllocatedBytes < 1_000_000, dupes.Stats.AllocatedBytes.ToString());
            Assert.True(distinct.Stats.AllocatedBytes > 3_000_000, distinct.Stats.AllocatedBytes.ToString());
        }

        [Fact]
        public void PresizedAndGrownTablesAgree()
        {
            Assert.Equal("True", Ev.Run("a = {i: str(i) for i in range(5000)}\nb = {}\nfor i in range(5000):\n    b[str(i)[:0] + str(i)] = 0\n"
                + "b = {}\nfor i in range(5000):\n    b[i] = str(i)\n"
                + "d = dict.fromkeys(range(5000), 1)\nfor k in range(0, 5000, 3):\n    del d[k]\n"
                + "c = {k: v + 1 for k, v in d.items()}\ne = {k: 0 for k in d}\nf = set(d.items())\n"
                + "r = a == b and list(a) == list(b) and list(c) == list(d) and list(e) == list(d) and len(f) == len(d)"
                + " and {x for x in range(10)} == set(range(10)) and list({x: 1 for x in range(9, -1, -1)}) == list(range(9, -1, -1))\n").Repr("r"));
        }

        [Fact]
        public void AnEqThatChangesTheSetDuringTheLookupLeavesItConsistent()
        {
            Assert.Equal("(20, True)", Ev.Run("s = set()\nclass K:\n    def __init__(self, v):\n        self.v = v\n"
                + "    def __hash__(self):\n        return 1\n"
                + "    def __eq__(self, o):\n        if len(s) < 50:\n            s.add(len(s) + 1000)\n        return False\n"
                + "for i in range(20):\n    s.add(K(i))\n"
                + "ints = [x for x in s if not isinstance(x, K)]\n"
                + "r = (sum(1 for x in s if isinstance(x, K)), len(s) == len(list(s)) == 20 + len(set(ints)) and len(ints) > 0)\n").Repr("r"));
        }
    }
}
