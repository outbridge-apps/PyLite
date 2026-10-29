using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // v2 B6: random.choices (3.6) and random.randbytes (3.9).
    [Trait("Category", "Compatibility")]
    public class RandomModernTests
    {
        private const string P = "import random\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- choices: shape and determinism ----
        [InlineData("random.seed(1)\nr = len(random.choices([1, 2, 3], k=5))", "5")]
        [InlineData("random.seed(1)\nr = len(random.choices([1, 2, 3]))", "1")]
        [InlineData("random.seed(1)\nr = all(c in (1, 2, 3) for c in random.choices([1, 2, 3], k=50))", "True")]
        [InlineData("random.seed(7)\na = random.choices('AB', k=10)\nrandom.seed(7)\nb = random.choices('AB', k=10)\nr = (a == b)", "True")]
        [InlineData("r = random.choices([1, 2], k=0)", "[]")]
        // ---- weights: a zero-weight element is never picked ----
        [InlineData("random.seed(1)\nr = all(c == 'a' for c in random.choices('ab', weights=[1, 0], k=30))", "True")]
        [InlineData("random.seed(1)\nr = all(c == 'b' for c in random.choices('ab', cum_weights=[0, 1], k=30))", "True")]
        [InlineData("random.seed(1)\nr = all(c in 'ab' for c in random.choices('ab', weights=[0.5, 0.5], k=20))", "True")]
        // ---- randbytes ----
        [InlineData("random.seed(1)\nr = len(random.randbytes(16))", "16")]
        [InlineData("r = random.randbytes(0)", "b''")]
        [InlineData("random.seed(3)\na = random.randbytes(8)\nrandom.seed(3)\nb = random.randbytes(8)\nr = (a == b)", "True")]
        public void Modern(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("x = random.choices([1, 2], weights=[1, 2], cum_weights=[1, 3], k=1)", "TypeError")]
        [InlineData("x = random.choices([1, 2], weights=[1], k=1)", "ValueError")]
        [InlineData("x = random.choices([1, 2], weights=[0, 0], k=1)", "ValueError")]
        [InlineData("x = random.choices([], k=1)", "IndexError")]
        [InlineData("x = random.randbytes(-1)", "ValueError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }
    }
}
