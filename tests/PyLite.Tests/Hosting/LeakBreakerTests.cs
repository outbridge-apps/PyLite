using System.Threading;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    [Trait("Category", "Sandbox")]
    public class LeakBreakerTests
    {
        private static LeakBreaker Breaker()
        {
            return new LeakBreaker(4);
        }

        private static RunnerThread T()
        {
            return new RunnerThread(() => RunResult.Success(null, null, null, new RunStatistics()), () => null);
        }

        [Fact]
        public void ThreeLeaksDoNotOpen()
        {
            var b = Breaker();
            b.RegisterLeak(T());
            b.RegisterLeak(T());
            b.RegisterLeak(T());
            Assert.False(b.IsCircuitOpen);
        }

        [Fact]
        public void FourthLeakOpensWithEvent()
        {
            var b = Breaker();
            int last = -1;
            int count = 0;
            b.CircuitStateChanged += n => { last = n; count++; };
            for (int i = 0; i < 4; i++)
                b.RegisterLeak(T());
            Assert.True(b.IsCircuitOpen);
            Assert.Equal(4, last);
            Assert.Equal(1, count);
        }

        [Fact]
        public void UnregisterClosesWithEvent()
        {
            var b = Breaker();
            int lastClose = -1;
            b.CircuitStateChanged += n => lastClose = n;
            var threads = new RunnerThread[4];
            for (int i = 0; i < 4; i++)
            {
                threads[i] = T();
                b.RegisterLeak(threads[i]);
            }
            Assert.True(b.IsCircuitOpen);
            b.UnregisterLeak(threads[0]);
            Assert.False(b.IsCircuitOpen);
            Assert.Equal(3, lastClose);
        }

        [Fact]
        public void DoubleRegisterCountedOnce()
        {
            var b = Breaker();
            var t = T();
            b.RegisterLeak(t);
            b.RegisterLeak(t);
            Assert.Equal(1, b.LeakedThreads);
        }

        [Fact]
        public void UnregisterWithoutRegisterIsNoOp()
        {
            var b = Breaker();
            b.UnregisterLeak(T());
            Assert.Equal(0, b.LeakedThreads);
        }

        [Fact]
        public void EventHandlerThrowingDoesNotBreakProtocol()
        {
            var b = Breaker();
            b.CircuitStateChanged += n => throw new System.InvalidOperationException("boom");
            for (int i = 0; i < 4; i++)
                b.RegisterLeak(T());
            Assert.True(b.IsCircuitOpen);   // no exception escaped RegisterLeak
        }

        // A runner that finished just after the host's wait gave up was counted as leaked, and no finally was
        // left to uncount it: phantoms piled up until the breaker refused every run (96 racing runs left 10).
        [Fact]
        public void AThreadThatAlreadyFinishedIsNotCountedAsLeaked()
        {
            var b = Breaker();
            for (int i = 0; i < 5; i++)
            {
                RunnerThread t = T();
                b.UnregisterLeak(t);   // what its finally does when it ends before the host registers it
                Assert.False(b.RegisterLeak(t));
            }
            Assert.Equal(0, b.LeakedThreads);
            Assert.False(b.IsCircuitOpen);
            RunnerThread live = T();
            Assert.True(b.RegisterLeak(live));
            Assert.False(b.RegisterLeak(live));   // once
            b.UnregisterLeak(live);
            Assert.Equal(0, b.LeakedThreads);
        }

        [Fact]
        public void HangingThreadsOpenThenCloseBreaker()
        {
            var b = Breaker();
            int events = 0;
            b.CircuitStateChanged += n => Interlocked.Increment(ref events);
            var gates = new ManualResetEventSlim[4];
            var threads = new RunnerThread[4];
            for (int i = 0; i < 4; i++)
            {
                gates[i] = new ManualResetEventSlim(false);
                var g = gates[i];
                threads[i] = new RunnerThread(
                    () => { g.Wait(); return RunResult.Success(null, null, null, new RunStatistics()); },
                    () => null, b);
                threads[i].Start();
                Assert.False(threads[i].WaitCompleted(100));
                b.RegisterLeak(threads[i]);
            }
            Assert.True(b.IsCircuitOpen);

            for (int i = 0; i < 4; i++)
                gates[i].Set();
            for (int i = 0; i < 4; i++)
                Assert.True(threads[i].WaitCompleted(5000));
            Assert.False(b.IsCircuitOpen);   // finally -> UnregisterLeak ran before _done
            Assert.Equal(0, b.LeakedThreads);
            Assert.Equal(2, events);            // one open + one close
        }
    }
}
