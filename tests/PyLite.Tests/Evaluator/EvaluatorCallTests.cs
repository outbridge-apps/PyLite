using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Xunit;

namespace Outbridge.PyLite.Tests.Evaluator
{
    [Trait("Category", "Compatibility")]
    public class EvaluatorCallTests
    {
        // ---------- basic calls & returns ----------

        [Fact]
        public void SimpleCall()
        {
            var r = Ev.Run("def f(a, b):\n    return a + b\nr = f(2, 3)\n");
            Assert.Equal("5", r.Repr("r"));
        }

        [Fact]
        public void NoReturnGivesNone()
        {
            var r = Ev.Run("def f():\n    pass\nr = f()\n");
            Assert.Equal("None", r.Repr("r"));
        }

        [Fact]
        public void NonCallableTypeError()
        {
            Assert.Equal("'int' object is not callable", Ev.Err("r = (5)()\n"));
        }

        // ---------- argument binding + CPython-parity error texts ----------

        [Fact]
        public void TooManyPositional()
        {
            Assert.Equal("f() takes 2 positional arguments but 3 were given",
                Ev.Err("def f(a, b):\n    return a\nr = f(1, 2, 3)\n"));
        }

        [Fact]
        public void TakesZeroPositional()
        {
            Assert.Equal("f() takes 0 positional arguments but 1 was given",
                Ev.Err("def f():\n    return 1\nr = f(1)\n"));
        }

        [Fact]
        public void MissingOnePositional()
        {
            Assert.Equal("f() missing 1 required positional argument: 'b'",
                Ev.Err("def f(a, b):\n    return a\nr = f(1)\n"));
        }

        [Fact]
        public void MissingTwoPositional()
        {
            Assert.Equal("f() missing 2 required positional arguments: 'a' and 'b'",
                Ev.Err("def f(a, b):\n    return a\nr = f()\n"));
        }

        [Fact]
        public void MultipleValues()
        {
            Assert.Equal("f() got multiple values for argument 'a'",
                Ev.Err("def f(a, b):\n    return a\nr = f(1, a=2)\n"));
        }

        // A name may collide across the call site's keyword sources in any order: explicit-then-**,
        // **-then-explicit, **-then-**. All are the same CPython TypeError (the parser only rejects
        // explicit-explicit); a **kw sink or a kw-only param must not silently take the last value.
        [Theory]
        [InlineData("def f(**kw):\n    return kw\nr = f(a=1, **{'a': 2})\n")]
        [InlineData("def f(**kw):\n    return kw\nr = f(**{'a': 1}, a=2)\n")]
        [InlineData("def f(**kw):\n    return kw\nr = f(**{'a': 1}, **{'a': 2})\n")]
        [InlineData("def f(*, a):\n    return a\nr = f(**{'a': 1}, a=2)\n")]
        public void DuplicateKeywordAcrossSourcesRejected(string src)
        {
            Assert.Equal("f() got multiple values for keyword argument 'a'", Ev.Err(src));
        }

        [Fact]
        public void NonOverlappingKeywordMergeBinds()
        {
            var r = Ev.Run("def f(**kw):\n    return sorted(kw.items())\nr = f(a=1, **{'b': 2}, c=3)\n");
            Assert.Equal("[('a', 1), ('b', 2), ('c', 3)]", r.Repr("r"));
        }

        // Wide ** merge: dup detection is a seen-set, not a scan over prior names (used to be O(n^2)).
        [Fact]
        public void WideDoubleStarMergeBinds()
        {
            var r = Ev.Run("def f(**kw):\n    return len(kw)\n"
                + "d = {'k' + str(i): i for i in range(2000)}\nr = f(**d)\n");
            Assert.Equal("2000", r.Repr("r"));
        }

        [Fact]
        public void UnexpectedKeyword()
        {
            Assert.Equal("f() got an unexpected keyword argument 'x'",
                Ev.Err("def f(a):\n    return a\nr = f(x=1)\n"));
        }

        [Fact]
        public void MissingKeywordOnly()
        {
            Assert.Equal("f() missing 1 required keyword-only argument: 'b'",
                Ev.Err("def f(a, *, b):\n    return b\nr = f(1)\n"));
        }

        [Theory]
        [InlineData("def f(*args):\n    return args\nr = f(1, 2, 3)\n", "(1, 2, 3)")]
        [InlineData("def f(**kw):\n    return kw\nr = f(a=1, b=2)\n", "{'a': 1, 'b': 2}")]
        [InlineData("def f(a, b=10):\n    return a + b\nr = f(5)\n", "15")]
        [InlineData("def f(a, *, b):\n    return a - b\nr = f(1, b=2)\n", "-1")]
        [InlineData("def f(a, *args, b=9):\n    return (a, args, b)\nr = f(1, 2, 3)\n", "(1, (2, 3), 9)")]
        [InlineData("def f(a, b, **kw):\n    return (a, b, kw)\nr = f(1, 2, x=3, y=4)\n", "(1, 2, {'x': 3, 'y': 4})")]
        [InlineData("def f(a, b=1, c=2):\n    return (a, b, c)\nr = f(9, c=8)\n", "(9, 1, 8)")]
        [InlineData("r = (lambda x, y=2: x + y)(3)\n", "5")]
        [InlineData("r = (lambda a, b=3: a * b)(4)\n", "12")]
        [InlineData("r = (lambda: 7)()\n", "7")]
        public void ArgBinding(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void MutableDefaultShared()
        {
            var r = Ev.Run("def f(a=[]):\n    return a\nr1 = f()\nr2 = f()\nsame = r1 is r2\n");
            Assert.Equal("True", r.Repr("same"));
        }

        [Fact]
        public void DefaultEvaluatedAtDef()
        {
            var r = Ev.Run("n = 10\ndef f(x=n):\n    return x\nn = 20\nr = f()\n");
            Assert.Equal("10", r.Repr("r"));
        }

        // ---------- closures, globals, recursion ----------

        [Fact]
        public void LateBindingClosure()
        {
            var r = Ev.Run("def outer():\n    x = 1\n    def inner():\n        return x\n    x = 2\n    return inner()\nr = outer()\n");
            Assert.Equal("2", r.Repr("r"));
        }

        [Fact]
        public void SharedCellMutation()
        {
            var r = Ev.Run("def make():\n    c = 0\n    def inc():\n        nonlocal c\n        c = c + 1\n        return c\n    inc()\n    return inc()\nr = make()\n");
            Assert.Equal("2", r.Repr("r"));
        }

        [Fact]
        public void GlobalMutation()
        {
            var r = Ev.Run("cnt = 0\ndef inc():\n    global cnt\n    cnt = cnt + 1\ninc()\ninc()\n");
            Assert.Equal("2", r.Repr("cnt"));
        }

        [Fact]
        public void RecursionViaGlobal()
        {
            var r = Ev.Run("def fib(n):\n    return n if n < 2 else fib(n - 1) + fib(n - 2)\nr = fib(10)\n");
            Assert.Equal("55", r.Repr("r"));
        }

        [Fact]
        public void LambdaRepr()
        {
            Assert.Equal("<function <lambda>>", Ev.ReprOf("lambda: 1"));
        }

        [Fact]
        public void DefRepr()
        {
            var r = Ev.Run("def f():\n    pass\ng = f\n");
            Assert.Equal("<function f>", r.Repr("g"));
        }

        [Fact]
        public void InfiniteRecursionAbortsOnDepth()
        {
            Assert.Throws<EngineAbort>(() => Ev.Run("def f():\n    return f()\nr = f()\n"));
        }
    }
}
