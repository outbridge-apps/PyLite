using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-09: permutations, combinations, combinations_with_replacement.
    [Trait("Category", "Compatibility")]
    public class ItertoolsCombinatoricsTests
    {
        private const string P = "from itertools import permutations, combinations, combinations_with_replacement, permutations as perm\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- permutations ----
        [InlineData("r = list(permutations('AB'))", "[('A', 'B'), ('B', 'A')]")]
        [InlineData("r = list(permutations('ABC',2))",
            "[('A', 'B'), ('A', 'C'), ('B', 'A'), ('B', 'C'), ('C', 'A'), ('C', 'B')]")]
        [InlineData("r = list(permutations([1,1]))", "[(1, 1), (1, 1)]")]
        [InlineData("r = list(permutations('AB',3))", "[]")]
        [InlineData("r = list(permutations([],0))", "[()]")]
        [InlineData("r = len(list(permutations(range(4))))", "24")]
        // ---- combinations ----
        [InlineData("r = list(combinations('ABCD',2))",
            "[('A', 'B'), ('A', 'C'), ('A', 'D'), ('B', 'C'), ('B', 'D'), ('C', 'D')]")]
        [InlineData("r = list(combinations('AB',0))", "[()]")]
        [InlineData("r = list(combinations('AB',3))", "[]")]
        // ---- combinations_with_replacement ----
        [InlineData("r = list(combinations_with_replacement('AB',2))", "[('A', 'A'), ('A', 'B'), ('B', 'B')]")]
        [InlineData("r = list(combinations_with_replacement('ABC',2))",
            "[('A', 'A'), ('A', 'B'), ('A', 'C'), ('B', 'B'), ('B', 'C'), ('C', 'C')]")]
        [InlineData("r = list(combinations_with_replacement([],1))", "[]")]
        [InlineData("r = list(combinations_with_replacement([],0))", "[()]")]
        public void Iterators(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("x = list(combinations('AB',-1))", "ValueError")]
        [InlineData("x = list(permutations('AB',-1))", "ValueError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void HugePermutationsAbortsViaSteps()
        {
            var lim = ResourceLimits.Default.With(maxSteps: 2_000_000);
            Assert.Throws<EngineAbort>(() => Ev.Run(P + "x = sum(1 for _ in permutations(range(12)))\n", lim));
        }
    }
}
