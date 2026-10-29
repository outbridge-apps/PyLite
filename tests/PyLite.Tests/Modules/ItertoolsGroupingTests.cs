using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-05/06: groupby(+_grouper), starmap, zip_longest.
    [Trait("Category", "Compatibility")]
    public class ItertoolsGroupingTests
    {
        private const string P = "from itertools import groupby, starmap, zip_longest, count, islice\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- groupby ----
        [InlineData("r = [(k, list(g)) for k, g in groupby('AAAABBBCCDAABBB')]",
            "[('A', ['A', 'A', 'A', 'A']), ('B', ['B', 'B', 'B']), ('C', ['C', 'C']), ('D', ['D']), ('A', ['A', 'A']), ('B', ['B', 'B', 'B'])]")]
        [InlineData("r = [k for k, g in groupby([1,1,2,2,3])]", "[1, 2, 3]")]
        [InlineData("r = [len(list(g)) for k, g in groupby([1,1,1,2])]", "[3, 1]")]
        [InlineData("r = [(k, list(g)) for k, g in groupby([('a',1),('a',2),('b',3)], lambda x: x[0])]",
            "[('a', [('a', 1), ('a', 2)]), ('b', [('b', 3)])]")]
        [InlineData("r = list(groupby([]))", "[]")]
        [InlineData("r = [k for k, g in groupby('AAABBAAA')]", "['A', 'B', 'A']")]
        // ---- starmap ----
        [InlineData("r = list(starmap(pow, [(2,5),(3,2),(10,3)]))", "[32, 9, 1000]")]
        // ---- zip_longest ----
        [InlineData("r = list(zip_longest('AB','xyz'))", "[('A', 'x'), ('B', 'y'), (None, 'z')]")]
        [InlineData("r = list(zip_longest('AB','xyz',fillvalue='-'))", "[('A', 'x'), ('B', 'y'), ('-', 'z')]")]
        [InlineData("r = list(zip_longest())", "[]")]
        [InlineData("r = list(zip_longest('AB'))", "[('A',), ('B',)]")]
        public void Iterators(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Fact]
        public void SavedGroupersAreEmpty()
        {
            // Parity: keeping the (k, g) pairs and reading g later yields empty groups (underlying moved on).
            string body = "groups = [(k, g) for k, g in groupby([1,1,2])]\n"
                + "r = [list(g) for k, g in groups]\n";
            Assert.Equal("[[], []]", Body(body));
        }

        [Theory]
        [InlineData("x = list(starmap(pow, 5))", "TypeError")]
        [InlineData("x = list(starmap(pow, [5]))", "TypeError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void StarmapNonIterableMessage()
        {
            string src = P + "msg = ''\ntry:\n    list(starmap(pow, 5))\nexcept TypeError as e:\n    msg = str(e)\n";
            Assert.Equal("\"'int' object is not iterable\"", Ev.Run(src).Repr("msg"));
        }
    }
}
