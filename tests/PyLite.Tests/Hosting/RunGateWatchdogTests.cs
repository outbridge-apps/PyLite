using System.Threading;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    [Trait("Category", "Sandbox")]
    public class RunGateWatchdogTests
    {
        private const long BigCeiling = 100_000L * 1024L * 1024L;

        private static RunGate Gate()
        {
            return new RunGate(8, BigCeiling, 4);
        }

        private static RunnerThread T()
        {
            return RunGateTests.MakeThread();
        }

        [Fact]
        public void ThreeLeaksDoNotOpen()
        {
            var gate = Gate();
            gate.RegisterLeak(T());
            gate.RegisterLeak(T());
            gate.RegisterLeak(T());
            Assert.False(gate.IsCircuitOpen);
        }

        [Fact]
        public void FourthLeakOpensWithEvent()
        {
            var gate = Gate();
            int last = -1;
            int count = 0;
            gate.CircuitStateChanged += n => { last = n; count++; };
            for (int i = 0; i < 4; i++)
            {
                gate.RegisterLeak(T());
            }
            Assert.True(gate.IsCircuitOpen);
            Assert.Equal(4, last);
            Assert.Equal(1, count);
        }

        [Fact]
        public void UnregisterClosesWithEvent()
        {
            var gate = Gate();
            int lastClose = -1;
            gate.CircuitStateChanged += n => lastClose = n;
            var threads = new RunnerThread[4];
            for (int i = 0; i < 4; i++)
            {
                threads[i] = T();
                gate.RegisterLeak(threads[i]);
            }
            Assert.True(gate.IsCircuitOpen);
            gate.UnregisterLeak(threads[0]);
            Assert.False(gate.IsCircuitOpen);
            Assert.Equal(3, lastClose);
        }

        [Fact]
        public void DoubleRegisterCountedOnce()
        {
            var gate = Gate();
            var t = T();
            gate.RegisterLeak(t);
            gate.RegisterLeak(t);
            Assert.Equal(1, gate.LeakedThreads);
        }

        [Fact]
        public void UnregisterWithoutRegisterIsNoOp()
        {
            var gate = Gate();
            gate.UnregisterLeak(T());
            Assert.Equal(0, gate.LeakedThreads);
        }

        [Fact]
        public void EventHandlerThrowingDoesNotBreakProtocol()
        {
            var gate = Gate();
            gate.CircuitStateChanged += n => throw new System.InvalidOperationException("boom");
            for (int i = 0; i < 4; i++)
            {
                gate.RegisterLeak(T());
            }
            Assert.True(gate.IsCircuitOpen);   // no exception escaped RegisterLeak
        }

        [Fact]
        public void HangingThreadsOpenThenCloseBreaker()
        {
            var gate = new RunGate(8, BigCeiling, 4);
            int events = 0;
            gate.CircuitStateChanged += n => Interlocked.Increment(ref events);
            var gates = new ManualResetEventSlim[4];
            var threads = new RunnerThread[4];
            for (int i = 0; i < 4; i++)
            {
                gates[i] = new ManualResetEventSlim(false);
                var g = gates[i];
                ScriptError r;
                var ticket = gate.TryAcquire(ResourceLimits.Default, out r);
                threads[i] = new RunnerThread(
                    () => { g.Wait(); return RunResult.Success(null, null, null, new RunStatistics()); },
                    () => null, gate, ticket);
                threads[i].Start();
                Assert.False(threads[i].WaitCompleted(100));
                gate.RegisterLeak(threads[i]);
            }
            Assert.True(gate.IsCircuitOpen);

            for (int i = 0; i < 4; i++)
            {
                gates[i].Set();
            }
            for (int i = 0; i < 4; i++)
            {
                Assert.True(threads[i].WaitCompleted(5000));
            }
            Assert.False(gate.IsCircuitOpen);   // finally -> UnregisterLeak ran before _done
            Assert.Equal(0, gate.LeakedThreads);
            Assert.Equal(2, events);            // one open + one close
        }
    }
}
