using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // heapq: Lib/heapq.py over a list; nlargest / nsmallest / merge with key= and reverse=.
    [Trait("Category", "Compatibility")]
    public class HeapqTests
    {
        private const string Imp = "import heapq\nfrom heapq import heappush, heappop, heapify, heapreplace, heappushpop, nlargest, nsmallest, merge\n";

        private static string R(string body)
        {
            return Ev.Run(Imp + body + "\n").Repr("r");
        }

        [Theory]
        [InlineData("h = []\nfor x in [5, 1, 4, 2, 3]:\n    heappush(h, x)\nr = [heappop(h) for _ in range(5)]", "[1, 2, 3, 4, 5]")]
        [InlineData("h = [5, 1, 4, 2, 3]\nheapify(h)\nr = (h[0], sorted(h) == [1, 2, 3, 4, 5])", "(1, True)")]
        [InlineData("h = [1, 3, 2]\nheapify(h)\nr = (heapreplace(h, 0), h[0])", "(1, 0)")]
        [InlineData("h = [1, 3, 2]\nheapify(h)\nr = (heappushpop(h, 0), heappushpop(h, 5), sorted(h))", "(0, 1, [2, 3, 5])")]
        [InlineData("h = [(2, 'b'), (1, 'a')]\nheapify(h)\nr = heappop(h)", "(1, 'a')")]
        [InlineData("r = nlargest(2, [3, 1, 4, 1, 5, 9, 2, 6])", "[9, 6]")]
        [InlineData("r = nsmallest(3, [3, 1, 4, 1, 5, 9, 2, 6])", "[1, 1, 2]")]
        [InlineData("r = nlargest(2, ['aa', 'b', 'ccc'], key=len)", "['ccc', 'aa']")]
        [InlineData("r = nsmallest(1, [(1, 'x'), (1, 'y')], key=lambda p: p[0])", "[(1, 'x')]")]         // stable: first of equals
        [InlineData("r = nlargest(1, [(1, 'x'), (1, 'y')], key=lambda p: p[0])", "[(1, 'x')]")]          // as sorted(reverse=True)
        [InlineData("r = nlargest(0, [1, 2])", "[]")]
        [InlineData("r = nsmallest(5, [2, 1])", "[1, 2]")]
        [InlineData("r = list(merge([1, 4], [2, 3], [0]))", "[0, 1, 2, 3, 4]")]
        [InlineData("r = list(merge([4, 1], [3, 2], reverse=True))", "[4, 3, 2, 1]")]
        [InlineData("r = list(merge(['a', 'ccc'], ['bb'], key=len))", "['a', 'bb', 'ccc']")]
        [InlineData("r = list(merge())", "[]")]
        // merge is lazy: an infinite input costs what is consumed, and equal keys keep input order
        [InlineData("import itertools\nr = next(merge(itertools.count(), itertools.count(5)))", "0")]
        [InlineData("import itertools\nit = merge([1, 3], itertools.count(2))\nr = [next(it), next(it), next(it)]", "[1, 2, 3]")]
        [InlineData("r = list(merge([(1, 'a'), (2, 'a')], [(1, 'b')], key=lambda p: p[0]))", "[(1, 'a'), (1, 'b'), (2, 'a')]")]
        [InlineData("r = list(merge([(2, 'a'), (1, 'a')], [(2, 'b')], key=lambda p: p[0], reverse=True))", "[(2, 'a'), (2, 'b'), (1, 'a')]")]
        public void Behaviour(string body, string expected)
        {
            Assert.Equal(expected, R(body));
        }

        [Theory]
        [InlineData("heappop([])", "IndexError")]
        [InlineData("heapreplace([], 1)", "IndexError")]
        [InlineData("heappush((1,), 2)", "TypeError")]
        [InlineData("h = [1, 'a']\nheapify(h)", "TypeError")]
        [InlineData("nlargest(2, [1], bogus=1)", "TypeError")]
        public void Errors(string body, string exc)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + body + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }
    }
}
