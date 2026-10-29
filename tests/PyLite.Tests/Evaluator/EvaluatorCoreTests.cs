using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Xunit;

namespace Outbridge.PyLite.Tests.Evaluator
{
    [Trait("Category", "Compatibility")]
    public class EvaluatorCoreTests
    {
        // ---------- expressions (repr of the result) ----------

        [Theory]
        [InlineData("3 + 4", "7")]
        [InlineData("-7 // 2", "-4")]
        [InlineData("-7 % 2", "1")]
        [InlineData("2 ** 10", "1024")]
        [InlineData("~5", "-6")]
        [InlineData("-3", "-3")]
        [InlineData("1 and 2", "2")]
        [InlineData("0 and 2", "0")]
        [InlineData("0 or 'x'", "'x'")]
        [InlineData("1 or crash", "1")]
        [InlineData("not 0", "True")]
        [InlineData("not [1]", "False")]
        [InlineData("5 if True else 0", "5")]
        [InlineData("9 if 0 else 8", "8")]
        [InlineData("1 < 2 < 3", "True")]
        [InlineData("1 < 3 < 2", "False")]
        [InlineData("1 == 1 == 1", "True")]
        [InlineData("1 == 'a'", "False")]
        [InlineData("2 == 2.0", "True")]
        [InlineData("True < 2", "True")]
        [InlineData("3 in [1, 2, 3]", "True")]
        [InlineData("4 not in [1, 2, 3]", "True")]
        [InlineData("[1, 2, 3]", "[1, 2, 3]")]
        [InlineData("(1, 2)", "(1, 2)")]
        [InlineData("{1, 2}", "{1, 2}")]
        [InlineData("{'a': 1}", "{'a': 1}")]
        [InlineData("[1, 2, 3][1]", "2")]
        [InlineData("[1, 2, 3, 4, 5][1:3]", "[2, 3]")]
        [InlineData("'cat'[1]", "'a'")]
        public void Expression(string expr, string expectedRepr)
        {
            Assert.Equal(expectedRepr, Ev.ReprOf(expr));
        }

        // ---------- errors ----------

        [Fact]
        public void ZeroDivisionRaises()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.ReprOf("1 / 0"));
            Assert.Same(PyExceptionTypes.ZeroDivisionError, ex.Value.ExcType);
        }

        [Fact]
        public void FloatZeroDivisionRaises()
        {
            Assert.Throws<ScriptException>(() => Ev.ReprOf("1.0 / 0.0"));
        }

        [Fact]
        public void UnorderableTypesRaisesTypeError()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.ReprOf("3 < 'a'"));
            Assert.Same(PyExceptionTypes.TypeError, ex.Value.ExcType);
        }

        [Fact]
        public void AddStrIntTypeError()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.ReprOf("'a' + 1"));
            Assert.Same(PyExceptionTypes.TypeError, ex.Value.ExcType);
        }

        [Fact]
        public void UndefinedNameRaisesNameError()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("y = nope\n"));
            Assert.Same(PyExceptionTypes.NameError, ex.Value.ExcType);
        }

        // ---------- statements / control flow ----------

        [Fact]
        public void IfElifElse()
        {
            var r = Ev.Run("if False:\n    x = 1\nelif True:\n    x = 2\nelse:\n    x = 3\n");
            Assert.Equal("2", r.Repr("x"));
        }

        [Fact]
        public void WhileLoop()
        {
            var r = Ev.Run("i = 0\nwhile i < 3:\n    i += 1\n");
            Assert.Equal("3", r.Repr("i"));
        }

        [Fact]
        public void ForSum()
        {
            var r = Ev.Run("s = 0\nfor x in [1, 2, 3]:\n    s += x\n");
            Assert.Equal("6", r.Repr("s"));
        }

        [Fact]
        public void ForElseOnEmpty()
        {
            var r = Ev.Run("s = 0\nfor x in []:\n    s = 1\nelse:\n    s = 2\n");
            Assert.Equal("2", r.Repr("s"));
        }

        [Fact]
        public void BreakSuppressesElse()
        {
            var r = Ev.Run("s = 0\nfor x in [1, 2]:\n    if x == 1:\n        break\nelse:\n    s = 9\n");
            Assert.Equal("0", r.Repr("s"));
        }

        [Fact]
        public void ContinueSkips()
        {
            var r = Ev.Run("s = 0\nfor x in [1, 2, 3]:\n    if x == 2:\n        continue\n    s += x\n");
            Assert.Equal("4", r.Repr("s"));
        }

        [Fact]
        public void ForTupleUnpack()
        {
            var r = Ev.Run("s = 0\nfor a, b in [(1, 2), (3, 4)]:\n    s += a + b\n");
            Assert.Equal("10", r.Repr("s"));
        }

        [Fact]
        public void ChainedAssign()
        {
            var r = Ev.Run("a = b = 5\n");
            Assert.Equal("5", r.Repr("a"));
            Assert.Equal("5", r.Repr("b"));
        }

        [Fact]
        public void SubscriptReadWrite()
        {
            var r = Ev.Run("x = [1, 2, 3]\nx[0] = 9\ny = x[1]\n");
            Assert.Equal("[9, 2, 3]", r.Repr("x"));
            Assert.Equal("2", r.Repr("y"));
        }

        [Fact]
        public void DictSubscriptWrite()
        {
            var r = Ev.Run("d = {}\nd['a'] = 1\nd['b'] = 2\n");
            Assert.Equal("{'a': 1, 'b': 2}", r.Repr("d"));
        }

        [Fact]
        public void DelListItem()
        {
            var r = Ev.Run("x = [1, 2, 3]\ndel x[1]\n");
            Assert.Equal("[1, 3]", r.Repr("x"));
        }

        [Fact]
        public void AssertPasses()
        {
            Ev.Run("assert 1 == 1\n");
        }

        [Fact]
        public void AssertFailsWithoutMessage()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("assert 0\n"));
            Assert.Same(PyExceptionTypes.AssertionError, ex.Value.ExcType);
        }

        [Fact]
        public void AssertFailsWithMessage()
        {
            var r0 = Assert.Throws<ScriptException>(() => Ev.Run("assert 0, 'boom'\n"));
            Assert.Equal("boom", r0.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void IsNone()
        {
            var r = Ev.Run("x = None\nr = x is None\n");
            Assert.Equal("True", r.Repr("r"));
        }

        [Fact]
        public void InfiniteLoopHitsStepBudget()
        {
            var lim = ResourceLimits.Default.With(maxSteps: 100_000);
            Assert.Throws<EngineAbort>(() => Ev.Run("while True:\n    pass\n", lim));
        }
    }
}
