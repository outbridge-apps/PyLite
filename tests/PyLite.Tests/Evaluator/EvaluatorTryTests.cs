using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Xunit;

namespace Outbridge.PyLite.Tests.Evaluator
{
    [Trait("Category", "Compatibility")]
    public class EvaluatorTryTests
    {
        private static ScriptException Raises(string src)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(src));
        }

        [Fact]
        public void ExceptCatchesMatchingType()
        {
            var r = Ev.Run("try:\n    x = 1 / 0\nexcept ZeroDivisionError:\n    x = 9\n");
            Assert.Equal("9", r.Repr("x"));
        }

        [Fact]
        public void NonMatchingExceptPropagates()
        {
            var ex = Raises("try:\n    x = 1 / 0\nexcept ValueError:\n    x = 9\n");
            Assert.Same(PyExceptionTypes.ZeroDivisionError, ex.Value.ExcType);
        }

        [Fact]
        public void BareExceptCatchesAll()
        {
            var r = Ev.Run("try:\n    x = 1 / 0\nexcept:\n    x = 9\n");
            Assert.Equal("9", r.Repr("x"));
        }

        [Fact]
        public void ElseRunsOnNormalBody()
        {
            var r = Ev.Run("try:\n    x = 1\nexcept:\n    x = 2\nelse:\n    x = 3\n");
            Assert.Equal("3", r.Repr("x"));
        }

        [Fact]
        public void NoElseOnException()
        {
            var r = Ev.Run("x = 0\ntry:\n    y = 1 / 0\nexcept:\n    pass\nelse:\n    x = 9\n");
            Assert.Equal("0", r.Repr("x"));
        }

        [Fact]
        public void FinallyAlwaysRuns()
        {
            var r = Ev.Run("try:\n    pass\nfinally:\n    x = 9\n");
            Assert.Equal("9", r.Repr("x"));
        }

        [Fact]
        public void FinallyReturnWins()
        {
            var r = Ev.Run("def f():\n    try:\n        return 1\n    finally:\n        return 2\nr = f()\n");
            Assert.Equal("2", r.Repr("r"));
        }

        [Fact]
        public void FinallyPassPreservesReturn()
        {
            var r = Ev.Run("def f():\n    try:\n        return 1\n    finally:\n        pass\nr = f()\n");
            Assert.Equal("1", r.Repr("r"));
        }

        [Fact]
        public void FinallyReturnSuppressesException()
        {
            var r = Ev.Run("def f():\n    try:\n        raise ValueError\n    finally:\n        return 7\nr = f()\n");
            Assert.Equal("7", r.Repr("r"));
        }

        [Fact]
        public void SubtypeMatchAndAsVariable()
        {
            var r = Ev.Run("try:\n    raise KeyError('k')\nexcept LookupError as e:\n    m = str(e)\n");
            Assert.Equal("'k'", r.Get("m").Str(r.Ctx));
        }

        [Fact]
        public void TupleOfTypes()
        {
            var r = Ev.Run("try:\n    raise ValueError\nexcept (TypeError, ValueError):\n    x = 9\n");
            Assert.Equal("9", r.Repr("x"));
        }

        [Fact]
        public void EngineAbortFliesThroughExcept()
        {
            var lim = ResourceLimits.Default.With(maxSteps: 100_000);
            Assert.Throws<EngineAbort>(() => Ev.Run("while True:\n    try:\n        pass\n    except:\n        pass\n", lim));
        }

        [Fact]
        public void AsVariableClearedAfterExcept()
        {
            var ex = Raises("try:\n    raise ValueError('v')\nexcept ValueError as e:\n    pass\nafter = e\n");
            Assert.Same(PyExceptionTypes.NameError, ex.Value.ExcType);
        }

        [Fact]
        public void BareRaiseOutsideExcept()
        {
            var ex = Raises("raise\n");
            Assert.Same(PyExceptionTypes.RuntimeError, ex.Value.ExcType);
            Assert.Equal("No active exception to re-raise", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void RaiseNonExceptionTypeError()
        {
            var ex = Raises("raise 5\n");
            Assert.Same(PyExceptionTypes.TypeError, ex.Value.ExcType);
            Assert.Equal("exceptions must derive from BaseException", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void ImplicitContextChain()
        {
            var ex = Raises("try:\n    raise ValueError('a')\nexcept ValueError:\n    raise KeyError('b')\n");
            Assert.Same(PyExceptionTypes.KeyError, ex.Value.ExcType);
            Assert.NotNull(ex.Value.Context);
            Assert.Same(PyExceptionTypes.ValueError, ex.Value.Context.ExcType);
        }

        [Fact]
        public void RaiseFromSetsCause()
        {
            var ex = Raises("raise ValueError('x') from KeyError('y')\n");
            Assert.Same(PyExceptionTypes.ValueError, ex.Value.ExcType);
            Assert.NotNull(ex.Value.Cause);
            Assert.Same(PyExceptionTypes.KeyError, ex.Value.Cause.ExcType);
        }

        [Fact]
        public void FinallyRunsThenBreak()
        {
            var r = Ev.Run("s = 0\nfor x in [1, 2, 3]:\n    try:\n        if x == 2:\n            break\n    finally:\n        s += x\n");
            Assert.Equal("3", r.Repr("s"));
        }

        [Fact]
        public void HandlerExceptionPropagates()
        {
            var ex = Raises("try:\n    raise ValueError\nexcept ValueError:\n    raise KeyError\n");
            Assert.Same(PyExceptionTypes.KeyError, ex.Value.ExcType);
        }

        [Fact]
        public void ElseExceptionNotMatchedByOwnHandlers()
        {
            var ex = Raises("try:\n    pass\nexcept ValueError:\n    x = 1\nelse:\n    raise ValueError('from-else')\n");
            Assert.Same(PyExceptionTypes.ValueError, ex.Value.ExcType);
            Assert.Equal("from-else", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void ElseExceptionStillRunsFinally()
        {
            var r = Ev.Run("log = []\ntry:\n    try:\n        pass\n    except ValueError:\n        log.append('own')\n    else:\n        raise ValueError('e')\n    finally:\n        log.append('fin')\nexcept ValueError:\n    log.append('outer')\n");
            Assert.Equal("['fin', 'outer']", r.Repr("log"));
        }

        [Fact]
        public void MatchingErrorStillRunsFinally()
        {
            var r = Ev.Run("log = []\ntry:\n    try:\n        raise ValueError('x')\n    except unknown_name:\n        log.append('never')\n    finally:\n        log.append('fin')\nexcept NameError:\n    log.append('outer')\n");
            Assert.Equal("['fin', 'outer']", r.Repr("log"));
        }

        // The Raised signal must escape every loop flavor (while / iterator-for / range-for): a loop
        // that recognized only Break/Continue/Return would silently swallow it and keep looping.
        [Theory]
        [InlineData("n = 0\ntry:\n    while True:\n        n = n + 1\n        if n == 3:\n            raise ValueError('w')\nexcept ValueError:\n    pass\n")]
        [InlineData("n = 0\ntry:\n    for x in [10, 20, 30]:\n        n = n + 1\n        if x == 30:\n            raise ValueError('f')\nexcept ValueError:\n    pass\n")]
        [InlineData("n = 0\ntry:\n    for x in range(10):\n        n = n + 1\n        if x == 2:\n            raise ValueError('r')\nexcept ValueError:\n    pass\n")]
        public void RaiseEscapesLoopBody(string src)
        {
            var r = Ev.Run(src);
            Assert.Equal("3", r.Repr("n"));
        }

        [Fact]
        public void HandlerBodyExceptionStillRunsFinally()
        {
            var r = Ev.Run("log = []\ntry:\n    try:\n        raise ValueError('x')\n    except ValueError:\n        raise KeyError('k')\n    finally:\n        log.append('fin')\nexcept KeyError:\n    log.append('outer')\n");
            Assert.Equal("['fin', 'outer']", r.Repr("log"));
        }
    }
}
