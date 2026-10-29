using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Builtins
{
    // impl/06 T-BLT-08/09/10: lazy iterators, aggregates, sorted.
    [Trait("Category", "Compatibility")]
    public class BuiltinsIterTests
    {
        private static string R(string expr)
        {
            return Ev.ReprOf(expr);
        }

        // ---- enumerate / zip / map / filter / reversed (T-BLT-08) ----

        [Theory]
        [InlineData("list(enumerate(['a', 'b']))", "[(0, 'a'), (1, 'b')]")]
        [InlineData("list(enumerate('ab', 10**20))", "[(100000000000000000000, 'a'), (100000000000000000001, 'b')]")]
        [InlineData("list(zip([1, 2, 3], 'ab'))", "[(1, 'a'), (2, 'b')]")]
        [InlineData("list(zip())", "[]")]
        [InlineData("list(map(lambda x, y: x + y, [1, 2], [10, 20, 30]))", "[11, 22]")]
        [InlineData("list(filter(None, [0, 1, '', 'a']))", "[1, 'a']")]
        [InlineData("list(filter(lambda x: x > 1, [1, 2, 3]))", "[2, 3]")]
        [InlineData("list(reversed('abc'))", "['c', 'b', 'a']")]
        [InlineData("list(reversed(range(1, 6)))", "[5, 4, 3, 2, 1]")]
        [InlineData("list(reversed([1, 2, 3]))", "[3, 2, 1]")]
        // reversed(dict) yields keys in reverse insertion order (Python 3.8+; RFC-003 §4).
        [InlineData("list(reversed({1: 'a', 2: 'b', 3: 'c'}))", "[3, 2, 1]")]
        public void Iterators(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("zip(1)", "zip argument #1 must support iteration")]
        [InlineData("map(abs)", "map() must have at least two arguments.")]
        [InlineData("reversed(5)", "argument to reversed() must be a sequence")]
        public void IteratorErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        [Fact]
        public void IterOfIteratorIsSelf()
        {
            Assert.Equal("True", Ev.Run("e = enumerate([1])\nsame = iter(e) is e\n").Repr("same"));
        }

        // ---- any / all / sum / min / max (T-BLT-09) ----

        [Theory]
        [InlineData("any([])", "False")]
        [InlineData("all([])", "True")]
        [InlineData("any([0, '', 3])", "True")]
        [InlineData("all([1, 'a', 0])", "False")]
        [InlineData("sum([1, 2, 3])", "6")]
        [InlineData("sum([1, 2], 10)", "13")]
        [InlineData("sum([[1], [2]], [])", "[1, 2]")]
        [InlineData("min([3, 1, 2])", "1")]
        [InlineData("max('bca')", "'c'")]
        [InlineData("min([], default=7)", "7")]
        [InlineData("min(['bb', 'a', 'cc'], key=len)", "'a'")]
        [InlineData("min(2, 1)", "1")]
        [InlineData("max([1, 2.5])", "2.5")]
        public void Aggregates(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("sum(['a', 'b'], '')", "sum() can't sum strings [use ''.join(seq) instead]")]
        [InlineData("min([])", "min() arg is an empty sequence")]
        [InlineData("min(1, 2, default=0)", "Cannot specify a default for min() with multiple positional arguments")]
        [InlineData("min([1, 'a'])", "'<' not supported between instances of 'str' and 'int'")]
        public void AggregateErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        // ---- sorted (T-BLT-10) ----

        [Theory]
        [InlineData("sorted([3, 1, 2])", "[1, 2, 3]")]
        [InlineData("sorted('cba')", "['a', 'b', 'c']")]
        [InlineData("sorted([(1, 'b'), (0, 'x'), (1, 'a')], key=lambda t: t[0])", "[(0, 'x'), (1, 'b'), (1, 'a')]")]
        [InlineData("sorted([2, 1, 3], reverse=True)", "[3, 2, 1]")]
        [InlineData("sorted([(1, 'b'), (1, 'a')], key=lambda t: t[0], reverse=True)", "[(1, 'b'), (1, 'a')]")]
        public void Sorted(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("sorted([1, 'a'])", "'<' not supported between instances of 'str' and 'int'")]
        [InlineData("sorted([1, 2, 3], True)", "sorted() takes exactly one argument (2 given)")]
        public void SortedErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        [Fact]
        public void SortedKeyCalledOncePerElement()
        {
            var r = Ev.Run("calls = 0\ndef k(x):\n    global calls\n    calls += 1\n    return -x\nout = sorted([3, 1, 2], key=k)\n");
            Assert.Equal("[3, 2, 1]", r.Repr("out"));
            Assert.Equal("3", r.Repr("calls"));
        }

        [Fact]
        public void SortStabilityFuzz()
        {
            // Equal keys keep their original order (stability), checked against a large random input.
            var r = Ev.Run(
                "import math\n" +
                "data = [(i * 7 % 5, i) for i in range(500)]\n" +
                "out = sorted(data, key=lambda p: p[0])\n" +
                "ok = True\n" +
                "for a in range(len(out) - 1):\n" +
                "    if out[a][0] == out[a + 1][0] and out[a][1] > out[a + 1][1]:\n" +
                "        ok = False\n");
            Assert.Equal("True", r.Repr("ok"));
        }
    }
}
