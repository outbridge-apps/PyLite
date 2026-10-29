using System;
using System.Diagnostics;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    [Trait("Category", "ResourceLimits")]
    public class BudgetTests
    {
        private static Budget Make(ResourceLimits limits, CancellationToken ct = default)
            => new Budget(limits, Stopwatch.StartNew(), ct);

        [Fact]
        public void StepsExhaustionAborts()
        {
            var b = Make(ResourceLimits.Default.With(maxSteps: 100));
            for (int i = 0; i < 100; i++)
                b.Step();
            var ex = Assert.Throws<EngineAbort>(() => b.Step());
            Assert.Equal(EngineAbortKind.Steps, ex.Kind);
            Assert.Equal("MaxSteps", ex.LimitName);
        }

        [Fact]
        public void StepActualValueReported()
        {
            var b = Make(ResourceLimits.Default.With(maxSteps: 100));
            b.Step(50);
            var ex = Assert.Throws<EngineAbort>(() => b.Step(51));
            Assert.Equal(101, ex.ActualValue);
        }

        [Fact]
        public void DeadlineAbortsAfterExpiry()
        {
            var b = Make(ResourceLimits.Default.With(deadlineMs: 50));
            Thread.Sleep(70);
            var ex = Assert.Throws<EngineAbort>(() => { for (int i = 0; i < 1100; i++)
                b.Step(); });
            Assert.Equal(EngineAbortKind.Deadline, ex.Kind);
        }

        [Fact]
        public void CancelledOnAmortizedCheck()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            var b = Make(ResourceLimits.Default, cts.Token);
            var ex = Assert.Throws<EngineAbort>(() => { for (int i = 0; i < 1100; i++)
                b.Step(); });
            Assert.Equal(EngineAbortKind.Cancelled, ex.Kind);
        }

        [Fact]
        public void CancelledImmediateCheck()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            var b = Make(ResourceLimits.Default, cts.Token);
            Assert.Throws<EngineAbort>(() => b.CheckDeadlineNow());
        }

        [Fact]
        public void AllocationExhaustionAborts()
        {
            var b = Make(ResourceLimits.Default.With(maxAllocBytes: 1000));
            b.ChargeAllocation(600);
            var ex = Assert.Throws<EngineAbort>(() => b.ChargeAllocation(500));
            Assert.Equal(EngineAbortKind.Memory, ex.Kind);
            Assert.Equal(1100, ex.ActualValue);
        }

        [Fact]
        public void GraceExhaustionRethrowsSameInstance()
        {
            var b = Make(ResourceLimits.Default.With(maxSteps: 100));
            EngineAbort first = null;
            try
            {
                for (int i = 0; i < 200; i++)
                    b.Step();
            }
            catch (EngineAbort ea)
            {
                first = ea;
            }
            Assert.NotNull(first);
            Assert.True(b.IsTerminating);
            var second = Assert.Throws<EngineAbort>(() => b.Step(200_000));
            Assert.Same(first, second);
            Assert.True(b.HardStopRequested);
        }

        [Fact]
        public void TerminatingMemoryReserve()
        {
            var b = Make(ResourceLimits.Default.With(maxAllocBytes: 1000));
            EngineAbort first = null;
            b.ChargeAllocation(600);
            try
            {
                b.ChargeAllocation(500);
            }
            catch (EngineAbort ea)
            {
                first = ea;
            }
            Assert.NotNull(first);
            b.ChargeAllocation(512 * 1024);          // within the 1 MB Terminating reserve
            var second = Assert.Throws<EngineAbort>(() => b.ChargeAllocation(600 * 1024));
            Assert.Same(first, second);
        }

        [Fact]
        public void HardStopRethrowOnStepZero()
        {
            var b = Make(ResourceLimits.Default.With(maxSteps: 1));
            EngineAbort first = null;
            try
            {
                b.Step();
                b.Step();
            }
            catch (EngineAbort ea)
            {
                first = ea;
            }
            try
            {
                b.Step(200_000);
            }
            catch (EngineAbort)
            {
            }   // exhaust grace -> HardStop
            var again = Assert.Throws<EngineAbort>(() => b.Step(0));
            Assert.Same(first, again);
        }

        [Fact]
        public void RegexTotalTimeAborts()
        {
            var b = Make(ResourceLimits.Default);   // DeadlineMs 20000
            var ex = Assert.Throws<EngineAbort>(() =>
            {
                for (int i = 0; i < 100; i++)
                    b.ChargeRegexTime(TimeSpan.FromMilliseconds(250));
            });
            Assert.Equal(EngineAbortKind.RegexTimeout, ex.Kind);
        }

        [Fact]
        public void OutputCapAborts()
        {
            var b = Make(ResourceLimits.Default);   // 16 MB
            b.ChargeOutput(10 * 1024 * 1024);
            var ex = Assert.Throws<EngineAbort>(() => b.ChargeOutput(10 * 1024 * 1024));
            Assert.Equal(EngineAbortKind.OutputCap, ex.Kind);
        }

        [Fact]
        public void SleepCapAborts()
        {
            var b = Make(ResourceLimits.Default.With(sleepTotalMs: 5000));
            b.ChargeSleep(3000);
            var ex = Assert.Throws<EngineAbort>(() => b.ChargeSleep(2001));
            Assert.Equal(EngineAbortKind.Deadline, ex.Kind);
            Assert.Equal("SleepTotalMs", ex.LimitName);
        }

        [Fact]
        public void NegativeAllocationIsEngineBug()
        {
            var b = Make(ResourceLimits.Default);
            Assert.Throws<InvalidOperationException>(() => b.ChargeAllocation(-1));
        }

        [Fact]
        public void StepZeroAllowed()
        {
            var b = Make(ResourceLimits.Default);
            b.Step(0);   // no throw
        }
    }
}
