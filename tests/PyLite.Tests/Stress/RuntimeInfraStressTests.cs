using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Xunit;

namespace Outbridge.PyLite.Tests.Stress
{
    // Executable A8 catalog at the infrastructure level (impl/04 T-RUN-15): every scenario asserts the
    // process survives and the failure is a budget abort with the right limit, without runaway memory.
    [Collection("HeapSensitive")]   // measures absolute process-heap deltas; must not run in parallel
    [Trait("Category", "ResourceLimits")]
    [Trait("Category", "MachineSensitive")]
    public class RuntimeInfraStressTests
    {
        private static void AssertAbort(Action work, EngineAbortKind kind, string limitName)
        {
            var ex = Assert.Throws<EngineAbort>(work);
            Assert.Equal(kind, ex.Kind);
            Assert.Equal(limitName, ex.LimitName);
        }

        [Fact]  // A8-#11
        public void DeadlineHaltsBusyLoop()
        {
            var lim = ResourceLimits.Default.With(deadlineMs: 100, maxSteps: 2_000_000_000);
            var sw = Stopwatch.StartNew();
            var budget = new Budget(lim, sw, CancellationToken.None);
            AssertAbort(() =>
            {
                while (true)
                {
                    budget.Step(1);
                }
            }, EngineAbortKind.Deadline, "DeadlineMs");
            Assert.True(sw.ElapsedMilliseconds < 100 + 400, "overshoot bounded");
        }

        [Fact]  // A8-#15
        public void AllocationCounterOverflowIsCheckedNotWrapped()
        {
            var lim = ResourceLimits.Default.With(maxAllocBytes: long.MaxValue);
            var budget = new Budget(lim, Stopwatch.StartNew(), CancellationToken.None);
            long half = long.MaxValue / 2 + 1;
            budget.ChargeAllocation(half);
            Assert.Throws<OverflowException>(() => budget.ChargeAllocation(half));   // no silent wrap
        }

        [Fact]  // A8-#14
        public void TerminatingGraceThenHardStop()
        {
            var lim = ResourceLimits.Default.With(maxSteps: 100);
            var budget = new Budget(lim, Stopwatch.StartNew(), CancellationToken.None);
            EngineAbort first = null;
            try
            {
                for (int i = 0; i < 1000; i++)
                {
                    budget.Step(1);
                }
            }
            catch (EngineAbort e)
            {
                first = e;
            }
            Assert.NotNull(first);
            Assert.True(budget.IsTerminating);

            long extra = 0;
            EngineAbort second = null;
            try
            {
                while (true)
                {
                    budget.Step(1);
                    extra++;
                }
            }
            catch (EngineAbort e)
            {
                second = e;
            }
            Assert.Same(first, second);   // the same instance dominates
            Assert.True(budget.HardStopRequested);
            Assert.True(extra <= lim.TerminatingGraceSteps + 8, "overshoot within grace");
        }

        [Fact]  // A8-#9/10
        public void HugeSizeAbortsBeforeAllocation()
        {
            var ctx = Rt.Ctx();
            long before = GC.GetTotalMemory(true);
            AssertAbort(() => ctx.Values.EnsureIntBits(100_000_000), EngineAbortKind.Memory, "MaxIntBits");
            AssertAbort(() => ctx.Values.EnsureStrLen(2_000_000_000), EngineAbortKind.Memory, "MaxStrChars");
            AssertAbort(() => ctx.Values.EnsureCollectionSize(50_000_000), EngineAbortKind.Memory, "MaxCollectionItems");
            long after = GC.GetTotalMemory(true);
            Assert.True(after - before < 1_000_000, "no large allocation happened");
        }

        [Fact]  // A8-#21
        public void ConcurrentAcquireRefusesBeyondMax()
        {
            var gate = new RunGate(8, 100_000L * 1024 * 1024, 4);
            int acquired = 0;
            int refused = 0;
            Parallel.For(0, 100, i =>
            {
                ScriptError r;
                var t = gate.TryAcquire(ResourceLimits.Default, out r);
                if (t != null)
                {
                    Interlocked.Increment(ref acquired);
                }
                else
                {
                    Interlocked.Increment(ref refused);
                }
            });
            Assert.True(acquired <= 8, "never more than max concurrent");
            Assert.True(refused >= 92, "the rest are refused instantly");
        }

        [Fact]  // A8-#23
        public void HugeErrorMessageTruncatedBeforeAlloc()
        {
            var ctx = Rt.Ctx();
            var se = Raise.Make(ctx, PyExceptionTypes.ValueError, new string('x', 10_000_000));
            var arg = (Outbridge.PyLite.Runtime.Values.StrValue)se.Value.Args[0];
            Assert.Equal(EngineHardLimits.MaxErrorMessageChars, arg.Value.Length);
        }

        [Fact]  // A8-#19
        public void RegexTimeBudgetTrips()
        {
            var lim = ResourceLimits.Default.With(deadlineMs: 20_000);
            var budget = new Budget(lim, Stopwatch.StartNew(), CancellationToken.None);
            EngineAbort ex = null;
            int n = 0;
            try
            {
                for (; n < 200; n++)
                {
                    budget.ChargeRegexTime(TimeSpan.FromMilliseconds(250));
                }
            }
            catch (EngineAbort e)
            {
                ex = e;
            }
            Assert.NotNull(ex);
            Assert.Equal(EngineAbortKind.RegexTimeout, ex.Kind);
            Assert.InRange(n, 79, 81);   // ~81st charge crosses 20000 ms
        }

        [Fact]  // ctx/thread lifetime: no managed growth over many runs
        public void ManyRunsDoNotLeak()
        {
            long before = GC.GetTotalMemory(true);
            for (int i = 0; i < 500; i++)
            {
                var rt = new RunnerThread(() => RunResult.Success(null, null, null, new RunStatistics()), () => null);
                rt.Start();
                Assert.True(rt.WaitCompleted(5000));
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long after = GC.GetTotalMemory(true);
            // A genuine per-run ctx/thread leak over 500 runs would be hundreds of MB; 20 MB tolerates
            // JIT/test-runner noise. Reliable now that the class runs in the non-parallel HeapSensitive
            // collection (no concurrent allocators corrupt the before/after samples).
            Assert.True(after - before < 20_000_000, "no per-run managed leak");
        }
    }
}
