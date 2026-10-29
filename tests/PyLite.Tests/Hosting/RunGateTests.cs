using System;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    [Trait("Category", "Sandbox")]
    public class RunGateTests
    {
        private const long Mb = 1024L * 1024L;

        private static RunGate Gate(int max, long ceilingMb = 100_000)
        {
            return new RunGate(max, ceilingMb * Mb, 4);
        }

        private static ResourceLimits Alloc(long mb)
        {
            return ResourceLimits.Default.With(maxAllocBytes: mb * Mb);
        }

        [Fact]
        public void TwoAcquiresWithinLimit()
        {
            var gate = Gate(2);
            ScriptError r;
            Assert.NotNull(gate.TryAcquire(Alloc(1), out r));
            Assert.NotNull(gate.TryAcquire(Alloc(1), out r));
        }

        [Fact]
        public void ThirdAcquireRefused()
        {
            var gate = Gate(2);
            ScriptError r;
            gate.TryAcquire(Alloc(1), out r);
            gate.TryAcquire(Alloc(1), out r);
            var t3 = gate.TryAcquire(Alloc(1), out r);
            Assert.Null(t3);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Kind);
            Assert.Equal("engine saturated: too many concurrent runs", r.Message);
        }

        [Fact]
        public void ReleaseFreesSlot()
        {
            var gate = Gate(1);
            ScriptError r;
            var t1 = gate.TryAcquire(Alloc(1), out r);
            Assert.Null(gate.TryAcquire(Alloc(1), out r));
            gate.Release(t1);
            Assert.NotNull(gate.TryAcquire(Alloc(1), out r));
        }

        [Fact]
        public void DoubleReleaseIsNoOp()
        {
            var gate = Gate(2);
            ScriptError r;
            var t1 = gate.TryAcquire(Alloc(1), out r);
            gate.Release(t1);
            gate.Release(t1);   // idempotent; must not over-release the semaphore
            gate.TryAcquire(Alloc(1), out r);
            gate.TryAcquire(Alloc(1), out r);
            Assert.Null(gate.TryAcquire(Alloc(1), out r));   // count never exceeds max=2
        }

        [Fact]
        public void GlobalMemoryCeilingRefusesAndReturnsSlot()
        {
            var gate = new RunGate(8, 300 * Mb, 4);
            ScriptError r;
            Assert.NotNull(gate.TryAcquire(Alloc(256), out r));   // 256
            var t2 = gate.TryAcquire(Alloc(256), out r);          // 512 > 300 -> refused
            Assert.Null(t2);
            Assert.Equal("engine saturated: global memory budget exceeded", r.Message);
            // slot not lost: a fitting acquire succeeds
            Assert.NotNull(gate.TryAcquire(Alloc(40), out r));    // 256 + 40 = 296 <= 300
        }

        [Fact]
        public void BreakerOpenRefusesWithHostError()
        {
            var gate = Gate(8);
            var t1 = MakeThread();
            var t2 = MakeThread();
            var t3 = MakeThread();
            var t4 = MakeThread();
            gate.RegisterLeak(t1);
            gate.RegisterLeak(t2);
            gate.RegisterLeak(t3);
            gate.RegisterLeak(t4);
            ScriptError r;
            Assert.Null(gate.TryAcquire(Alloc(1), out r));
            Assert.Equal(ScriptErrorKind.HostError, r.Kind);
            Assert.Equal("engine unavailable: leaked thread limit reached", r.Message);
        }

        internal static RunnerThread MakeThread()
        {
            return new RunnerThread(() => RunResult.Success(null, null, null, new RunStatistics()), () => null);
        }
    }
}
