using System;
using System.Text;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    // The engine compiles on a thread of its own (review 6), but the compiler itself must still turn a
    // near-exhausted stack into a SyntaxError on any thread: these call it directly on a small one. An elif chain is the adversarial shape: it parses
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
            for (int depth = 500; depth <= 12000; depth += 500)
            {
                string src = ElifChain(depth);
                Exception seen = null;
                bool compiled = false;
                var t = new Thread(() =>
                {
                    try
                    {
                        Compiler.Compile(src, "elif-ladder", SyntaxLimits.Default);
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
                    Assert.True(seen is PySyntaxErrorException,
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

        // A starred display element parses below ParseTest, so `[*[*[*...]]]` recursed with no depth count
        // and 3,000 levels (9 KB of source) overflowed the caller's stack. Every starred shape now ends in
        // the expression-depth SyntaxError.
        [Theory]
        [InlineData("x = ", "[*", "a", "]")]
        [InlineData("x = ", "(*", "a", ",)")]
        [InlineData("x = ", "{*", "a", "}")]
        [InlineData("for ", "[*", "a", "]")]
        public void NestedStarredElementsAreASyntaxErrorOnASmallThread(string head, string open, string leaf, string close)
        {
            var sb = new StringBuilder(head);
            for (int i = 0; i < 3000; i++)
                sb.Append(open);
            sb.Append(leaf);
            for (int i = 0; i < 3000; i++)
                sb.Append(close);
            if (head == "for ")
                sb.Append(" in []: pass");
            string src = sb.ToString();
            Exception seen = null;
            var t = new Thread(() =>
            {
                try { Compiler.Compile(src, "starred", SyntaxLimits.Default); }
                catch (Exception ex) { seen = ex; }
            }, 256 * 1024);
            t.Start();
            t.Join();
            // the depth limit or, on this small thread, the stack probe before it: both a clean SyntaxError
            Assert.IsType<PySyntaxErrorException>(seen);
        }

        // Through the public Run(string, ...) path the chain compiles on the run's 32 MB thread (review 6, e1-02):
        // no throw, and the same answer from a 256 KB caller as from any other, where it used to be a SyntaxError.
        [Fact]
        public void HugeElifChainRunsTheSameFromASmallThread()
        {
            var engine = new ScriptEngine(new EngineOptions { CompiledScriptCacheEntries = 0 });
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
            RunResult reference = engine.Run(src, new RunRequest());
            Assert.True(res.Succeeded, res.Succeeded ? null : res.Error.Message);
            Assert.Equal(reference.Succeeded, res.Succeeded);
        }
    }
}
