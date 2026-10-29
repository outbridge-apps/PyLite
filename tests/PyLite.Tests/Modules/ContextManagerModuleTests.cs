using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // contextlib and decimal.localcontext (1.1.0): what only the engine can get wrong - a resource limit
    // is never suppressed, and a context change never outlives its block or its run.
    [Trait("Category", "Compatibility")]
    public class ContextManagerModuleTests
    {
        private static RunResult Run(ScriptEngine engine, string src)
        {
            return engine.Run(src, new RunRequest { ScalarOutputNames = new[] { "r" } });
        }

        [Theory]
        [InlineData("from contextlib import suppress\nwith suppress(Exception):\n    while True:\n        pass\n", "MaxSteps")]
        [InlineData("from contextlib import suppress\nwith suppress(BaseException):\n    s = 'x'\n    while True:\n        s = s + s\n", "MaxStrChars")]
        [InlineData("from contextlib import suppress\ndef f(n):\n    with suppress(RecursionError, Exception):\n        return f(n + 1)\nf(0)\n", "MaxCallDepth")]
        public void SuppressCannotSwallowAResourceLimit(string src, string limit)
        {
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = ResourceLimits.Default.With(maxSteps: 2_000_000, maxStrChars: 100_000) });
            RunResult r = Run(engine, src);
            Assert.False(r.Succeeded);
            Assert.Equal(limit, r.Error.LimitName);
        }

        [Fact]
        public void APrecisionChangeDoesNotLeakIntoTheNextRun()
        {
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = ResourceLimits.Default.With(maxSteps: 1_000_000) });
            RunResult inBlock = Run(engine, "from decimal import localcontext, setcontext, Context\n"
                + "with localcontext() as c:\n    c.prec = 3\n    c.rounding = 'ROUND_DOWN'\n    while True:\n        pass\n");
            Assert.Equal("MaxSteps", inBlock.Error.LimitName);   // the block never exits: the run does
            RunResult set = Run(engine, "from decimal import setcontext, Context, getcontext\nsetcontext(Context(prec=2))\nr = getcontext().prec\n");
            Assert.Equal("2", set.ScalarsOut["r"]);
            RunResult next = Run(engine, "from decimal import Decimal, getcontext\n"
                + "r = (getcontext().prec, getcontext().rounding, str(Decimal(2) / Decimal(3)))\n");
            Assert.True(next.Succeeded, next.Error?.Message);
            Assert.Equal("(28, 'ROUND_HALF_EVEN', '0.6666666666666666666666666667')", next.ScalarsOut["r"]);
        }

        [Fact]
        public void ClosingClosesAnEngineObjectAndNullcontextIsNotAFile()
        {
            Assert.Equal("(True, 'abc', 'x')", Ev.Run("import io\nfrom contextlib import closing, nullcontext\n"
                + "buf = io.StringIO('abc')\nwith closing(buf) as f:\n    got = f.read()\n"
                + "with nullcontext('x') as v:\n    pass\nr = (buf.closed, got, v)\n").Repr("r"));
        }

        [Fact]
        public void SuppressWithANonClassFailsOnlyWhenThereIsAnException()
        {
            Assert.Equal("('ok', 'TypeError')", Ev.Run("from contextlib import suppress\n"
                + "with suppress(1):\n    a = 'ok'\n"
                + "try:\n    with suppress(1):\n        1 / 0\nexcept TypeError as e:\n    b = type(e).__name__\nr = (a, b)\n").Repr("r"));
        }
    }
}
