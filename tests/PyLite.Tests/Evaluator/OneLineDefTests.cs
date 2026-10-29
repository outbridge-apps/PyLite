using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Evaluator
{
    // P6 (1.1.0): `def f(x): return <expr>` runs the expression through the closure compiler, as a lambda
    // body does. What must not change: tracebacks, the call-depth limit, and the bodies it does not fit.
    [Trait("Category", "Compatibility")]
    public class OneLineDefTests
    {
        private static RunResult Run(string src, ResourceLimits limits = null)
        {
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = limits ?? ResourceLimits.Default });
            return engine.Run(src, new RunRequest { ScalarOutputNames = new[] { "r" } });
        }

        [Fact]
        public void AnErrorInsideNamesTheReturnLineAndTheFunction()
        {
            RunResult r = Run("x = 1\n\ndef f(n):\n    return 1 // n\n\nr = f(0)\n");
            Assert.Equal("ZeroDivisionError", r.Error.PythonType);
            Assert.Equal(4, r.Error.Line);
            Assert.Equal("Traceback (most recent call last):\n"
                + "  File \"<script>\", line 6, in <module>\n"
                + "  File \"<script>\", line 4, in f\n"
                + "ZeroDivisionError: integer division or modulo by zero", r.Error.ScriptTraceback);
        }

        [Fact]
        public void ADocstringBeforeTheReturnIsTheSameShape()
        {
            RunResult r = Run("def f(n):\n    \"\"\"Halve.\"\"\"\n    return n // 0\n\nr = f(4)\n");
            Assert.Equal(3, r.Error.Line);
            Assert.Contains("line 3, in f", r.Error.ScriptTraceback);
        }

        [Fact]
        public void RecursionStillStopsOnTheCallDepth()
        {
            RunResult r = Run("def down(n):\n    return down(n + 1)\nr = down(0)\n", ResourceLimits.Default.With(maxCallDepth: 100));
            Assert.Equal("MaxCallDepth", r.Error.LimitName);
        }

        [Theory]
        [InlineData("def f(**kw):\n    return dict(**kw)\nr = f(a=1)['a']\n", "1")]               // outside the whitelist
        [InlineData("def f(x):\n    return\nr = f(1)\n", "None")]                               // a bare return
        [InlineData("def f(x):\n    return x if x > 0 else -x\nr = f(-3)\n", "3")]
        [InlineData("def f(x, y=10):\n    return x + y\nr = (f(1), f(1, 2))\n", "(11, 3)")]
        [InlineData("def f(*a):\n    return len(a)\nr = f(1, 2, 3)\n", "3")]
        [InlineData("def outer():\n    k = 5\n    def inner(x):\n        return x * k\n    return inner(2)\nr = outer()\n", "10")]
        [InlineData("def f(x):\n    'doc'\n    return [i * x for i in range(3)]\nr = f(2)\n", "[0, 2, 4]")]
        [InlineData("class C:\n    def __init__(self, v):\n        self.v = v\n    def get(self):\n        return self.v\nr = C(7).get()\n", "7")]
        public void EveryShapeStillWorks(string src, string expected)
        {
            RunResult r = Run(src);
            Assert.True(r.Succeeded, r.Error?.Message);
            Assert.Equal(expected, r.ScalarsOut["r"]);
        }

        [Fact]
        public void AStepLimitStillStopsALoopOfCalls()
        {
            RunResult r = Run("def f(x):\n    return x + 1\nn = 0\nwhile True:\n    n = f(n)\n", ResourceLimits.Default.With(maxSteps: 200_000));
            Assert.Equal("MaxSteps", r.Error.LimitName);
        }
    }
}
