using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-01..02/06(islice): count, repeat, cycle, islice.
    [Trait("Category", "Compatibility")]
    public class ItertoolsTests
    {
        private const string P = "from itertools import count, repeat, cycle, islice\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- count / islice ----
        [InlineData("r = list(islice(count(10), 3))", "[10, 11, 12]")]
        [InlineData("r = list(islice(count(2, 3), 4))", "[2, 5, 8, 11]")]
        [InlineData("r = list(islice(count(0, 0.1), 3))", "[0, 0.1, 0.2]")]
        [InlineData("c = count(5)\nr = [next(c), next(c), next(c)]", "[5, 6, 7]")]
        // ---- repeat ----
        [InlineData("r = list(repeat(7, 3))", "[7, 7, 7]")]
        [InlineData("r = list(repeat(7, 0))", "[]")]
        [InlineData("r = list(repeat(7, -2))", "[]")]
        [InlineData("r = list(islice(repeat(9), 2))", "[9, 9]")]
        // ---- cycle ----
        [InlineData("r = list(islice(cycle('AB'), 5))", "['A', 'B', 'A', 'B', 'A']")]
        [InlineData("r = list(cycle([]))", "[]")]
        [InlineData("c = cycle('X')\nr = [next(c), next(c), next(c)]", "['X', 'X', 'X']")]
        // ---- islice forms ----
        [InlineData("r = list(islice('ABCDEFG', 2))", "['A', 'B']")]
        [InlineData("r = list(islice('ABCDEFG', 2, 4))", "['C', 'D']")]
        [InlineData("r = list(islice('ABCDEFG', 2, None))", "['C', 'D', 'E', 'F', 'G']")]
        [InlineData("r = list(islice('ABCDEFG', 0, None, 2))", "['A', 'C', 'E', 'G']")]
        [InlineData("r = list(islice(count(), 5, 7))", "[5, 6]")]
        [InlineData("r = list(islice('ABC', 0))", "[]")]
        public void Iterators(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("r = repr(count(1))", "'count(1)'")]
        [InlineData("r = repr(count(5, 2))", "'count(5, 2)'")]
        [InlineData("r = (iter(count()) is count())", "False")]  // distinct objects
        public void ReprAndIdentity(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Fact]
        public void IterOfIteratorIsSelf()
        {
            Assert.Equal("True", Body("c = count(1)\nr = (iter(c) is c)"));
        }

        [Theory]
        [InlineData("x = repeat('x', 1.5)", "TypeError")]
        [InlineData("x = list(islice('ABC', -1))", "ValueError")]
        [InlineData("x = list(islice('ABC', 0, 3, 0))", "ValueError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void ImportMissingNameRaises()
        {
            ScriptException ex = Assert.Throws<ScriptException>(
                () => Ev.Run("from itertools import no_such_iterator\n"));
            Assert.Equal("ImportError", ex.Value.ExcType.Name);
        }
    }
}
