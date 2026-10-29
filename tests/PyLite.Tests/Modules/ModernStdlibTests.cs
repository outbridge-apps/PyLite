using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // v2 B4/B5 (RFC-003 §4): str.removeprefix/removesuffix (PEP 616), itertools.pairwise/batched,
    // accumulate(initial=), zip(strict=).
    [Trait("Category", "Compatibility")]
    public class ModernStdlibTests
    {
        private static string Body(string body)
        {
            return Ev.Run(body + "\n").Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(body + "\n"));
        }

        [Theory]
        // ---- str.removeprefix / removesuffix (PEP 616) ----
        [InlineData("r = 'TestHook'.removeprefix('Test')", "'Hook'")]
        [InlineData("r = 'BaseTestCase'.removeprefix('Test')", "'BaseTestCase'")]
        [InlineData("r = 'MiscTests'.removesuffix('Tests')", "'Misc'")]
        [InlineData("r = 'TmpDirMixin'.removesuffix('Tests')", "'TmpDirMixin'")]
        [InlineData("r = 'abc'.removeprefix('')", "'abc'")]
        [InlineData("r = 'abc'.removesuffix('')", "'abc'")]
        [InlineData("r = 'abc'.removeprefix('abcd')", "'abc'")]
        [InlineData("s = 'abc'\nr = (s.removeprefix('x') is s)", "True")]
        [InlineData("r = 'aaa'.removeprefix('a')", "'aa'")]
        // ---- itertools.pairwise (3.10) ----
        [InlineData("from itertools import pairwise\nr = list(pairwise('ABCD'))", "[('A', 'B'), ('B', 'C'), ('C', 'D')]")]
        [InlineData("from itertools import pairwise\nr = list(pairwise([1]))", "[]")]
        [InlineData("from itertools import pairwise\nr = list(pairwise([]))", "[]")]
        [InlineData("from itertools import pairwise\nr = list(pairwise([1, 2]))", "[(1, 2)]")]
        // ---- itertools.batched (3.12/3.13) ----
        [InlineData("from itertools import batched\nr = list(batched('ABCDEFG', 3))", "[('A', 'B', 'C'), ('D', 'E', 'F'), ('G',)]")]
        [InlineData("from itertools import batched\nr = list(batched([], 3))", "[]")]
        [InlineData("from itertools import batched\nr = list(batched([1, 2, 3, 4], 2))", "[(1, 2), (3, 4)]")]
        [InlineData("from itertools import batched\nr = list(batched('ABCD', 2, strict=True))", "[('A', 'B'), ('C', 'D')]")]
        // ---- accumulate(initial=) (3.8) ----
        [InlineData("from itertools import accumulate\nr = list(accumulate([1, 2, 3], initial=100))", "[100, 101, 103, 106]")]
        [InlineData("from itertools import accumulate\nr = list(accumulate([], initial=5))", "[5]")]
        // ---- zip(strict=) (3.10) ----
        [InlineData("r = list(zip('AB', [1, 2], strict=True))", "[('A', 1), ('B', 2)]")]
        [InlineData("r = list(zip(strict=True))", "[]")]
        [InlineData("r = list(zip('AB', [1, 2, 3]))", "[('A', 1), ('B', 2)]")]
        public void Modern(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("x = 'abc'.removeprefix(5)", "TypeError")]
        [InlineData("from itertools import batched\nx = list(batched('ABC', 0))", "ValueError")]
        [InlineData("from itertools import batched\nx = list(batched('ABCDE', 2, strict=True))", "ValueError")]
        [InlineData("x = list(zip('ABC', [1, 2], strict=True))", "ValueError")]
        [InlineData("x = list(zip('AB', [1, 2, 3], strict=True))", "ValueError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void ZipStrictShorterMessage()
        {
            string src = "msg = ''\ntry:\n    list(zip('ABC', [1, 2], strict=True))\nexcept ValueError as e:\n    msg = str(e)\n";
            Assert.Equal("'zip() argument 2 is shorter than argument 1'", Ev.Run(src).Repr("msg"));
        }

        [Fact]
        public void ZipStrictLongerMessage()
        {
            string src = "msg = ''\ntry:\n    list(zip('AB', [1, 2, 3], strict=True))\nexcept ValueError as e:\n    msg = str(e)\n";
            Assert.Equal("'zip() argument 2 is longer than argument 1'", Ev.Run(src).Repr("msg"));
        }

        [Fact]
        public void BatchedIncompleteMessage()
        {
            string src = "from itertools import batched\nmsg = ''\ntry:\n    list(batched('ABCDE', 2, strict=True))\nexcept ValueError as e:\n    msg = str(e)\n";
            Assert.Equal("'batched(): incomplete batch'", Ev.Run(src).Repr("msg"));
        }
    }
}
