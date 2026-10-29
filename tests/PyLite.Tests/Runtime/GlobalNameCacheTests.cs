using System.Text;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // The versioned global-name cache (lever E) must be invisible: every structural transition of the
    // module dict — first insert, delete, re-create, builtin shadow/unshadow, table resize — has to
    // give exactly the uncached answers. Value rebinding keeps the cached position (that is the point).
    [Trait("Category", "Compatibility")]
    public class GlobalNameCacheTests
    {
        [Fact]
        public void RebindLoopReadsFreshValues()
        {
            Assert.Equal("100", Ev.Run("g = 0\nfor i in range(100):\n    g = g + 1\nr = g\n").Repr("r"));
        }

        [Fact]
        public void FunctionSeesModuleRebindBetweenCalls()
        {
            var r = Ev.Run("g = 1\ndef f():\n    return g\na = f()\ng = 2\nb = f()\nr = (a, b)\n");
            Assert.Equal("(1, 2)", r.Repr("r"));
        }

        [Fact]
        public void GlobalStatementWriteInvalidatesNothingWrong()
        {
            var r = Ev.Run("g = 1\ndef f():\n    global g\n    g = 5\nx = g\nf()\ny = g\nr = (x, y)\n");
            Assert.Equal("(1, 5)", r.Repr("r"));
        }

        [Fact]
        public void DeleteThenReadRaisesNameError()
        {
            var r = Ev.Run(
                "g = 1\n"
                + "x = g\n"                       // primes the cache
                + "del g\n"
                + "try:\n"
                + "    y = g\n"
                + "except NameError:\n"
                + "    y = 'gone'\n"
                + "r = (x, y)\n");
            Assert.Equal("(1, 'gone')", r.Repr("r"));
        }

        [Fact]
        public void DeleteThenRecreateReadsTheNewBinding()
        {
            var r = Ev.Run("g = 1\nx = g\ndel g\ng = 2\ny = g\nr = (x, y)\n");
            Assert.Equal("(1, 2)", r.Repr("r"));
        }

        // A name that is assigned SOMEWHERE in the module is Global-kind everywhere in it, so reads
        // before the assignment resolve through the builtins fallback; the insert must flip the cache
        // to the global, the delete must flip it back.
        [Fact]
        public void BuiltinShadowFlipsBothWays()
        {
            var r = Ev.Run(
                "a = len('ab')\n"
                + "len = 99\n"
                + "b = len\n"
                + "del len\n"
                + "c = len('abc')\n"
                + "r = (a, b, c)\n");
            Assert.Equal("(2, 99, 3)", r.Repr("r"));
        }

        [Fact]
        public void UnboundReadThenBindThenRead()
        {
            var r = Ev.Run(
                "try:\n"
                + "    x = q\n"
                + "except NameError:\n"
                + "    x = 'nb'\n"
                + "q = 3\n"
                + "y = q\n"
                + "r = (x, y)\n");
            Assert.Equal("('nb', 3)", r.Repr("r"));
        }

        // 200 inserts force index resizes and entry-array growth mid-run; earlier cached positions
        // must survive (growth keeps positions) or re-resolve (rebuild bumps the version) — either
        // way every name still reads its own value.
        [Fact]
        public void TableResizeKeepsEveryNameCorrect()
        {
            var sb = new StringBuilder("g0 = 0\nprobe = g0\n");   // prime one early cache entry
            for (int i = 1; i < 200; i++)
                sb.Append("g").Append(i).Append(" = ").Append(i).Append('\n');
            sb.Append("r = (probe, g0, g1, g99, g199)\n");
            Assert.Equal("(0, 0, 1, 99, 199)", Ev.Run(sb.ToString()).Repr("r"));
        }

        [Fact]
        public void AugAssignOnGlobalGoesThroughTheCache()
        {
            Assert.Equal("10", Ev.Run("n = 0\nfor i in range(5):\n    n += 2\nr = n\n").Repr("r"));
        }

        [Fact]
        public void ForLoopTargetIsAGlobalAndStaysFresh()
        {
            Assert.Equal("4", Ev.Run("for i in range(5):\n    pass\nr = i\n").Repr("r"));
        }

        [Fact]
        public void DelOfNeverBoundGlobalStillRaises()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("x = 1\nz = x\ndel nope\n"));
            Assert.Equal("NameError", ex.Value.ExcType.Name);
        }

        // Structural-mutation storm: reads/writes interleaved with del/re-insert and builtin
        // shadow/unshadow so the cache invalidates constantly; hits == iterations proves no stale
        // read ever slipped through a version transition.
        [Fact]
        public void MutationChurnNeverServesStaleReads()
        {
            var r = Ev.Run(
                "hits = 0\n"
                + "g = 0\n"
                + "for i in range(2000):\n"
                + "    g = g + 1\n"
                + "    x = g\n"
                + "    if i % 3 == 0:\n"
                + "        del g\n"
                + "        g = x\n"
                + "    if i % 5 == 0:\n"
                + "        len = i\n"
                + "        y = len\n"
                + "        del len\n"
                + "    hits = hits + (1 if x == g else 0)\n"
                + "r = (g, hits)\n");
            Assert.Equal("(2000, 2000)", r.Repr("r"));
        }
    }
}
