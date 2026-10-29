using Outbridge.PyLite.Runtime.Errors;
using Xunit;

namespace Outbridge.PyLite.Tests.Evaluator
{
    [Trait("Category", "Compatibility")]
    public class EvaluatorComprehensionTests
    {
        [Theory]
        [InlineData("[x for x in [1, 2, 3]]", "[1, 2, 3]")]
        [InlineData("[x for x in []]", "[]")]
        [InlineData("[x * 2 for x in range(3)]", "[0, 2, 4]")]
        [InlineData("[x for x in range(5) if x % 2 == 0]", "[0, 2, 4]")]
        [InlineData("[x for x in range(4) if x > 0 if x < 3]", "[1, 2]")]
        [InlineData("[x * y for x in [1, 2] for y in [10, 20]]", "[10, 20, 20, 40]")]
        [InlineData("[y for x in [[1, 2], [3]] for y in x]", "[1, 2, 3]")]
        [InlineData("[a + b for a, b in [(1, 2), (3, 4)]]", "[3, 7]")]
        [InlineData("[x for x in [1, 2] for y in []]", "[]")]
        [InlineData("[x for x in [1, 2, 3] for y in [x]]", "[1, 2, 3]")]
        [InlineData("{x for x in [1, 1, 2]}", "{1, 2}")]
        [InlineData("{x * x for x in range(4)}", "{0, 1, 4, 9}")]
        [InlineData("{k: v for k, v in [('a', 1), ('b', 2)]}", "{'a': 1, 'b': 2}")]
        [InlineData("{k: v for k, v in [('a', 1), ('a', 2)]}", "{'a': 2}")]
        [InlineData("list(x for x in range(3))", "[0, 1, 2]")]
        [InlineData("sum(x for x in range(5))", "10")]
        public void Comprehension(string expr, string expectedRepr)
        {
            Assert.Equal(expectedRepr, Ev.ReprOf(expr));
        }

        [Fact]
        public void GenexpRepr()
        {
            Assert.Equal("<generator object>", Ev.ReprOf("x for x in [1]"));
        }

        [Fact]
        public void ComprehensionScopeDoesNotLeak()
        {
            var r = Ev.Run("x = 99\ny = [x for x in [1, 2]]\n");
            Assert.Equal("99", r.Repr("x"));
        }

        [Fact]
        public void FirstIterableErrorIsImmediate()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("y = [i for i in undefined]\n"));
            Assert.Same(PyExceptionTypes.NameError, ex.Value.ExcType);
        }

        [Fact]
        public void GenexpPartialConsumption()
        {
            var r = Ev.Run("g = (x for x in [1, 2, 3])\nfirst = next(g)\nrest = list(g)\n");
            Assert.Equal("1", r.Repr("first"));
            Assert.Equal("[2, 3]", r.Repr("rest"));
        }

        [Fact]
        public void GenexpSingleUse()
        {
            var r = Ev.Run("g = (x for x in [1, 2])\na = list(g)\nb = list(g)\n");
            Assert.Equal("[1, 2]", r.Repr("a"));
            Assert.Equal("[]", r.Repr("b"));
        }

        [Fact]
        public void MaterializationPropagatesError()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("y = [1 / 0 for x in [1]]\n"));
            Assert.Same(PyExceptionTypes.ZeroDivisionError, ex.Value.ExcType);
        }
    }
}
