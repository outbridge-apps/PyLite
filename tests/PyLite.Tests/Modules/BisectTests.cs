using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // bisect: Lib/bisect.py semantics incl. the 3.10 key= and the lo/hi window.
    [Trait("Category", "Compatibility")]
    public class BisectTests
    {
        private const string Imp = "import bisect\nfrom bisect import bisect_left, bisect_right, insort, insort_left, insort_right\n";

        private static string R(string body)
        {
            return Ev.Run(Imp + body + "\n").Repr("r");
        }

        [Theory]
        [InlineData("r = bisect_left([1, 2, 2, 3], 2)", "1")]
        [InlineData("r = bisect_right([1, 2, 2, 3], 2)", "3")]
        [InlineData("r = bisect.bisect([1, 2, 2, 3], 2)", "3")]
        [InlineData("r = bisect_left([], 5)", "0")]
        [InlineData("r = bisect_left([1, 3], 2)", "1")]
        [InlineData("r = bisect_right((1, 3, 5), 6)", "3")]                       // a tuple searches too
        [InlineData("r = bisect_left([1, 2, 3, 4, 5], 3, 3)", "3")]               // lo bounds the window
        [InlineData("r = bisect_right([1, 2, 3, 4, 5], 3, hi=2)", "2")]           // hi does too
        [InlineData("r = bisect_left([1, 2, 3], 9, hi=100)", "3")]                // hi is clipped to len
        [InlineData("r = bisect_left(['a', 'dd', 'ccc'], 2, key=len)", "1")]      // key is applied to items, x is a key
        [InlineData("r = bisect_right(['a', 'dd', 'ccc'], 2, key=len)", "2")]
        [InlineData("r = bisect_left([(1, 'x'), (3, 'y')], 2, key=lambda p: p[0])", "1")]
        public void Searches(string body, string expected)
        {
            Assert.Equal(expected, R(body));
        }

        [Theory]
        [InlineData("a = [1, 3, 5]\ninsort(a, 4)\nr = a", "[1, 3, 4, 5]")]
        [InlineData("a = [1, 3, 3, 5]\ninsort_left(a, 3)\nr = a", "[1, 3, 3, 3, 5]")]
        [InlineData("a = []\ninsort_right(a, 1)\ninsort_right(a, 0)\nr = a", "[0, 1]")]
        [InlineData("a = [(1, 'a'), (3, 'c')]\ninsort(a, (2, 'b'), key=lambda p: p[0])\nr = a", "[(1, 'a'), (2, 'b'), (3, 'c')]")]
        [InlineData("a = [1, 2, 3, 4]\ninsort(a, 0, lo=2)\nr = a", "[1, 2, 0, 3, 4]")]   // the window is trusted, as in CPython
        public void Insertions(string body, string expected)
        {
            Assert.Equal(expected, R(body));
        }

        [Theory]
        [InlineData("insort((1, 2), 3)", "TypeError")]
        [InlineData("bisect_left(5, 3)", "TypeError")]
        [InlineData("bisect_left([1], 3, -1)", "ValueError")]
        [InlineData("bisect_left([1], 3, bogus=1)", "TypeError")]
        [InlineData("bisect_left([1, 'a'], 2)", "TypeError")]
        public void Errors(string expr, string exc)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }
    }
}
