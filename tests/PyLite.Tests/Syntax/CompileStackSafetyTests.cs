using System;
using System.Text;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    // Compile runs on the CALLING thread (R-14 cache), so every compile-phase walker must convert a
    // near-exhausted stack into a SyntaxError. An elif chain is the adversarial shape: it parses
    // ITERATIVELY (no suite nesting, so MaxStmtDepth/MaxExprDepth never trip) yet synthesizes IfNode
    // nesting as deep as the chain is long, which every later walker descends recursively. The
    // resolver/folder probed their walks, but ResolverValidate did not — a window just below the
    // resolver's probe threshold (5.5-6.4k elifs on a 1MB thread) overflowed INSIDE the validator and
    // killed the host process. These tests sweep the whole ladder on a minimal 256KB thread: every
    // depth must end in a clean outcome (compiled or PyLiteSyntaxException) — a crash kills the runner.
    [Trait("Category", "ResourceLimits")]
    public class CompileStackSafetyTests
    {
        private static string ElifChain(int depth)
        {
            var sb = new StringBuilder("x = 9\nif x == 0:\n 0\n");
            for (int i = 0; i < depth; i++)
                sb.Append("elif x:\n 0\n");
            sb.Append("else:\n 0\n");
            return sb.ToString();
        }

        // One engine + one small thread for the whole ladder keeps the test fast; each depth compiles
        // on a FRESH thread so earlier frames can't mask a hole.
        [Fact]
        public void ElifChainLadderNeverCrashesASmallCompileThread()
        {
            var engine = new ScriptEngine(new EngineOptions());
            for (int depth = 500; depth <= 12000; depth += 500)
            {
                string src = ElifChain(depth);
                Exception seen = null;
                bool compiled = false;
                var t = new Thread(() =>
                {
                    try
                    {
                        engine.Compile(src, "elif-ladder");
                        compiled = true;
                    }
                    catch (Exception ex)
                    {
                        seen = ex;
                    }
                }, 256 * 1024);   // minimum CLR stack — the tightest honest host
                t.Start();
                t.Join();
                if (!compiled)
                    Assert.True(seen is PyLiteSyntaxException,
                        "depth " + depth + ": expected clean SyntaxError, got " + (seen == null ? "nothing" : seen.GetType().Name));
            }
        }

        // Sane chains must still compile and RUN on the engine's own 32MB runner thread.
        [Fact]
        public void ModerateElifChainStillCompilesAndRuns()
        {
            var engine = new ScriptEngine(new EngineOptions());
            var sb = new StringBuilder("x = 250\nif x == 0:\n r = 0\n");
            for (int i = 1; i <= 500; i++)
                sb.Append("elif x == ").Append(i).Append(":\n r = ").Append(i).Append('\n');
            sb.Append("else:\n r = -1\n");
            RunResult res = engine.Run(sb.ToString(), new RunRequest { ScalarOutputNames = new[] { "r" } });
            Assert.True(res.Succeeded, res.Succeeded ? null : res.Error.Message);
            Assert.Equal("250", res.ScalarsOut["r"]);
        }

        // The oversized chain fails CLEANLY through the public Run(string, ...) path too (no throw,
        // Kind == SyntaxError) — this is what a production host actually observes after the fix.
        [Fact]
        public void HugeElifChainYieldsSyntaxErrorThroughRun()
        {
            var engine = new ScriptEngine(new EngineOptions());
            string src = ElifChain(40000);
            Exception seen = null;
            RunResult res = null;
            var t = new Thread(() =>
            {
                try { res = engine.Run(src, new RunRequest()); }
                catch (Exception ex) { seen = ex; }
            }, 256 * 1024);
            t.Start();
            t.Join();
            Assert.Null(seen);
            Assert.False(res.Succeeded);
            Assert.Equal(ScriptErrorKind.SyntaxError, res.Error.Kind);
        }
    }
}
