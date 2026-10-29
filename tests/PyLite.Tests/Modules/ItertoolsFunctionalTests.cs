using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-03/04: accumulate, chain(+from_iterable), compress, dropwhile, takewhile, filterfalse.
    [Trait("Category", "Compatibility")]
    public class ItertoolsFunctionalTests
    {
        private const string P = "import itertools\n"
            + "from itertools import accumulate, chain, compress, dropwhile, takewhile, filterfalse\n"
            + "from itertools import count, repeat, islice\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- accumulate ----
        [InlineData("r = list(accumulate([1,2,3,4]))", "[1, 3, 6, 10]")]
        [InlineData("r = list(accumulate([1,2,3], lambda a,b: a*b))", "[1, 2, 6]")]
        [InlineData("r = list(accumulate([]))", "[]")]
        [InlineData("r = list(accumulate([5]))", "[5]")]
        [InlineData("r = list(accumulate(['a','b','c']))", "['a', 'ab', 'abc']")]
        // ---- chain ----
        [InlineData("r = list(chain([1,2],[3,4],[5]))", "[1, 2, 3, 4, 5]")]
        [InlineData("r = list(chain())", "[]")]
        [InlineData("r = list(chain.from_iterable(['AB','CD']))", "['A', 'B', 'C', 'D']")]
        [InlineData("r = list(islice(chain.from_iterable(repeat([1,2])), 5))", "[1, 2, 1, 2, 1]")]
        [InlineData("r = list(itertools.chain('AB', [1,2]))", "['A', 'B', 1, 2]")]
        // ---- compress ----
        [InlineData("r = list(compress('ABCDEF',[1,0,1,0,1,1]))", "['A', 'C', 'E', 'F']")]
        [InlineData("r = list(compress('ABC',[0,0,0]))", "[]")]
        [InlineData("r = list(compress('ABC',[]))", "[]")]
        // ---- dropwhile / takewhile / filterfalse ----
        [InlineData("r = list(dropwhile(lambda x:x<5, [1,4,6,2,1]))", "[6, 2, 1]")]
        [InlineData("r = list(dropwhile(lambda x:x<5, [6,7]))", "[6, 7]")]
        [InlineData("r = list(dropwhile(lambda x:True, [1,2,3]))", "[]")]
        [InlineData("r = list(takewhile(lambda x:x<5, [1,4,6,2,1]))", "[1, 4]")]
        [InlineData("r = list(takewhile(lambda x:True, [1,2,3]))", "[1, 2, 3]")]
        [InlineData("r = list(takewhile(lambda x:x<0, [1,2]))", "[]")]
        [InlineData("r = list(filterfalse(lambda x:x%2, range(6)))", "[0, 2, 4]")]
        [InlineData("r = list(filterfalse(None, [0,1,0,2,'',3]))", "[0, 0, '']")]
        [InlineData("r = list(filterfalse(None, []))", "[]")]
        public void Iterators(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Fact]
        public void ChainIsLazy()
        {
            // The second (infinite) source is only touched after the first is exhausted, bounded by islice.
            Assert.Equal("[1, 2, 9, 9, 9]", Body("r = list(islice(chain([1,2], repeat(9)), 5))"));
        }

        [Fact]
        public void TakeWhileConsumesFirstFalse()
        {
            // The element that fails the predicate is consumed from the shared source (lost).
            string body = "it = iter([1, 2, 5, 3])\n"
                + "tw = list(takewhile(lambda x: x < 3, it))\n"
                + "r = (tw, list(it))\n";
            Assert.Equal("([1, 2], [3])", Body(body));
        }
    }
}
