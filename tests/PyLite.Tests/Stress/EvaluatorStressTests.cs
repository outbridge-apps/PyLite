using System;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Stress
{
    // A8 vectors that require the evaluator (impl/05 T-EVL-19). Every scenario proves the process
    // survives and the failure is a bounded budget abort. Tests 5/6/7 pin EngineAbort uncatchability.
    [Collection("HeapSensitive")]   // ManyRunsDoNotLeak measures absolute process-heap; run non-parallel
    [Trait("Category", "ResourceLimits")]
    [Trait("Category", "MachineSensitive")]
    public class EvaluatorStressTests
    {
        private static ResourceLimits Steps(long n)
        {
            return ResourceLimits.Default.With(maxSteps: n);
        }

        private static EngineAbort AbortOf(string src, ResourceLimits lim = null)
        {
            return Assert.Throws<EngineAbort>(() => Ev.Run(src, lim));
        }

        [Fact]  // A8-#1 deep recursion
        public void DeepRecursionAbortsOnCallDepth()
        {
            var ab = AbortOf("def f(n):\n    return f(n + 1)\nf(0)\n");
            Assert.Equal(EngineAbortKind.Recursion, ab.Kind);
            Assert.Equal("MaxCallDepth", ab.LimitName);
        }

        [Fact]  // A8-#1 mutual recursion
        public void MutualRecursionAborts()
        {
            var ab = AbortOf("def a():\n    return b()\ndef b():\n    return a()\na()\n");
            Assert.Equal(EngineAbortKind.Recursion, ab.Kind);
        }

        [Fact]  // A8-#11 infinite loop
        public void InfiniteLoopAbortsOnSteps()
        {
            var ab = AbortOf("while True:\n    pass\n", Steps(100_000));
            Assert.Equal(EngineAbortKind.Steps, ab.Kind);
        }

        [Fact]  // A8-#13 KEY: bare except does not catch EngineAbort
        public void EngineAbortFliesThroughBareExcept()
        {
            AbortOf("while True:\n    try:\n        pass\n    except:\n        pass\n", Steps(100_000));
        }

        [Fact]  // A8-#13 KEY: BaseException does not catch EngineAbort
        public void EngineAbortFliesThroughBaseException()
        {
            AbortOf("while True:\n    try:\n        pass\n    except BaseException:\n        pass\n", Steps(100_000));
        }

        [Fact]  // A8-#14 KEY: finally bomb -> HardStop, process survives
        public void FinallyBombHardStops()
        {
            AbortOf("try:\n    while True:\n        pass\nfinally:\n    while True:\n        pass\n", Steps(100_000));
        }

        [Fact]  // A8-#24 raise+catch loop under a budget (no runaway)
        public void RaiseCatchLoopAbortsWithoutRunaway()
        {
            var ab = AbortOf("while True:\n    try:\n        raise ValueError\n    except:\n        pass\n", Steps(200_000));
            Assert.Equal(EngineAbortKind.Steps, ab.Kind);
        }

        [Fact]  // large finite loop hits the step budget
        public void BigForLoopAbortsOnSteps()
        {
            var ab = AbortOf("s = 0\nfor i in range(1000000):\n    s += i\n", Steps(100_000));
            Assert.Equal(EngineAbortKind.Steps, ab.Kind);
        }

        [Fact]  // A8-#1 infinite-ish genexp materialization
        public void GenexpMaterializationAborts()
        {
            AbortOf("x = list(v for v in range(100000000))\n", Steps(200_000));
        }

        [Fact]  // A8-#11 giant f-string field
        public void GiantFStringFieldAbortsOnMemory()
        {
            var ab = AbortOf("x = f'{\"x\" * 1000000000}'\n");
            Assert.Equal(EngineAbortKind.Memory, ab.Kind);
        }

        [Fact]  // ctx/env/function lifetime: many runs, no managed leak
        public void ManyRunsDoNotLeak()
        {
            long before = GC.GetTotalMemory(true);
            for (int i = 0; i < 500; i++)
            {
                Ev.Run("def f(a, b):\n    return a + b\nr = f(1, 2)\n");
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long after = GC.GetTotalMemory(true);
            Assert.True(after - before < 20_000_000, "no per-run managed leak");
        }
    }
}
