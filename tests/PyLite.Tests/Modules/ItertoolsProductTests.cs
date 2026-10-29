using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-07/08: tee (+TeeBuffer) and product.
    [Trait("Category", "Compatibility")]
    public class ItertoolsProductTests
    {
        private const string P = "from itertools import tee, product, count, islice\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        [Theory]
        // ---- tee ----
        [InlineData("a, b = tee([1,2,3])\nr = (list(a), list(b))", "([1, 2, 3], [1, 2, 3])")]
        [InlineData("a, b = tee('XY')\nr = (next(a), next(b), next(a))", "('X', 'X', 'Y')")]
        [InlineData("r = list(tee([1,2], 1)[0])", "[1, 2]")]
        [InlineData("r = tee([1,2], 0)", "()")]
        [InlineData("a, b = tee(range(3))\nr = (list(b), list(a))", "([0, 1, 2], [0, 1, 2])")]
        [InlineData("a, b, c = tee([1,2,3], 3)\nr = (list(a), list(b), list(c))", "([1, 2, 3], [1, 2, 3], [1, 2, 3])")]
        [InlineData("a, b = tee(count())\nr = [next(a) for _ in range(3)]", "[0, 1, 2]")]
        // ---- product ----
        [InlineData("r = list(product('AB','xy'))", "[('A', 'x'), ('A', 'y'), ('B', 'x'), ('B', 'y')]")]
        [InlineData("r = list(product([1,2],repeat=2))", "[(1, 1), (1, 2), (2, 1), (2, 2)]")]
        [InlineData("r = list(product())", "[()]")]
        [InlineData("r = list(product('AB',repeat=0))", "[()]")]
        [InlineData("r = list(product([],[1,2]))", "[]")]
        [InlineData("r = list(product('A','B','C'))", "[('A', 'B', 'C')]")]
        [InlineData("r = len(list(product(range(3),repeat=3)))", "27")]
        [InlineData("r = list(product([1],[2],[3]))", "[(1, 2, 3)]")]
        public void Iterators(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Fact]
        public void ProductOfInfiniteAbortsInConstructor()
        {
            var lim = ResourceLimits.Default.With(maxSteps: 200_000);
            Assert.Throws<EngineAbort>(() => Ev.Run(P + "x = product(count(), [1])\n", lim));
        }

        [Fact]
        public void TeeCapExceededAborts()
        {
            // One clone races far ahead while the other stays put -> the ring exceeds TeeBufferCap (10000).
            var lim = ResourceLimits.Default.With(maxSteps: 5_000_000);
            string src = P + "a, b = tee(count())\nfor _ in range(20000):\n    next(a)\n";
            Assert.Throws<EngineAbort>(() => Ev.Run(src, lim));
        }

        [Fact]
        public void SynchronizedClonesStayBounded()
        {
            // Both clones advance in lockstep: no unbounded ring growth even far past the cap.
            string src = P + "a, b = tee(count())\ns = 0\nfor _ in range(20000):\n    s += next(a) + next(b)\nr = s\n";
            Assert.Equal("399980000", Ev.Run(src).Repr("r"));
        }
    }
}
