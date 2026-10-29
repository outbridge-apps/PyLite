using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    // The small-int fast loops in list/tuple `in`, count(), index() and deque.count() must keep the
    // full Equals semantics: 1 == True == 1.0 across types, user __eq__ still consulted, big ints
    // never conflated with small ones. Each case exercises both the fast decisions and the
    // per-element fallback (mixed lists interleave int and non-int elements).
    [Trait("Category", "Compatibility")]
    public class MembershipFastPathTests
    {
        [Theory]
        [InlineData("r = 3 in [1, 2, 3]", "True")]
        [InlineData("r = 4 in [1, 2, 3]", "False")]
        [InlineData("r = 3 in (1, 2, 3)", "True")]
        [InlineData("r = 4 in (1, 2, 3)", "False")]
        [InlineData("r = True in [0, 1, 2]", "True")]           // bool needle == int element
        [InlineData("r = 1 in [False, True]", "True")]          // int needle == bool element
        [InlineData("r = 1 in [0.5, 1.0]", "True")]             // float element -> full-equals fallback
        [InlineData("r = 1 in ['x', [], 1]", "True")]           // mixed junk then a hit
        [InlineData("r = 2 in ['x', [], 1]", "False")]
        [InlineData("r = 10**30 in [1, 2, 3]", "False")]        // big needle, small elements
        [InlineData("r = 10**30 in [10**30]", "True")]          // big needle, big element (full path)
        [InlineData("r = 1 in [10**30]", "False")]              // small needle, big element (never equal)
        [InlineData("r = 0 in [-0.0]", "True")]                 // 0 == -0.0
        public void ContainsSemantics(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("r = [1, True, 1.0, 2, 1].count(1)", "4")]  // CPython: 1 == True == 1.0
        [InlineData("r = [1, True, 1.0, 2].count(True)", "3")]
        [InlineData("r = (1, 2, 1, 3).count(1)", "2")]
        [InlineData("r = [10**30, 1].count(10**30)", "1")]
        [InlineData("r = [10**30, 1].count(1)", "1")]
        [InlineData("r = ['a', 'b', 'a'].count('a')", "2")]     // non-int needle keeps the full path
        public void CountSemantics(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("r = [5, 6, 7].index(7)", "2")]
        [InlineData("r = (5, 6, 7).index(6)", "1")]
        [InlineData("r = [0, True, 2].index(1)", "1")]          // True is found by int needle
        [InlineData("r = [0.0, 1.0, 2].index(1)", "1")]         // float element found via fallback
        [InlineData("r = [1, 2, 3, 2].index(2, 2)", "3")]       // start honoured on the fast loop
        public void IndexSemantics(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void IndexMissingStillRaises()
        {
            var ex = Assert.Throws<Outbridge.PyLite.Runtime.Errors.ScriptException>(() => Ev.Run("r = [1, 2].index(9)"));
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
        }

        // NOTE: user __eq__ on record classes is not consulted by the equality protocol at all
        // (pre-existing engine-wide behavior, tracked separately) — so the fallback contract here is
        // proven by the float / mixed-list cases above, which do exercise the full-Equals path.

        [Fact]
        public void RecordElementFallsBackWithoutFaulting()
        {
            // A record mid-list must route through the full Equals (not the fast compare) and simply
            // not match an int needle — no fault, later elements still found.
            var r = Ev.Run(
                "class Box:\n"
                + "    def __init__(self, v):\n"
                + "        self.v = v\n"
                + "r = 5 in [1, Box(5), 5]\n"
                + "n = [1, Box(7), 2].count(7)\n");
            Assert.Equal("True", r.Repr("r"));
            Assert.Equal("0", r.Repr("n"));
        }

        [Theory]
        [InlineData("from collections import deque\nr = deque([1, True, 1.0, 5]).count(1)", "3")]
        [InlineData("from collections import deque\nr = deque([10**30, 1]).count(10**30)", "1")]
        public void DequeCountSemantics(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }
    }
}
