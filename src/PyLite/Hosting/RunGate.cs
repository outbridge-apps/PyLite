using System;
using System.Threading;

namespace Outbridge.PyLite.Hosting
{
    // A live-run slot handed out by RunGate; carries the memory reservation and a double-release guard.
    internal sealed class RunTicket
    {
        internal long MemoryBudget;
        internal int Released;   // Interlocked guard against a double finally
    }

    // Concurrency semaphore + global memory ceiling + leaked-thread circuit
    // breaker. One per ScriptEngine. No blocking waits: saturation refuses instantly.
    internal sealed class RunGate
    {
        private readonly SemaphoreSlim _slots;
        private long _totalMemoryBudget;                 // Σ MaxAllocBytes of live runs (Interlocked)
        private readonly long _globalMemoryCeiling;
        private int _leakedThreads;                      // Interlocked
        private readonly int _leakedThreshold;

        public event Action<int> CircuitStateChanged;    // host telemetry only; never reaches scripts

        public RunGate(int maxConcurrentRuns, long globalMemoryCeiling, int leakedThreadThreshold)
        {
            if (maxConcurrentRuns < 1 || maxConcurrentRuns > EngineHardLimits.MaxConcurrentRuns)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentRuns));
            }
            if (globalMemoryCeiling <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(globalMemoryCeiling));
            }
            if (leakedThreadThreshold < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(leakedThreadThreshold));
            }
            _slots = new SemaphoreSlim(maxConcurrentRuns, maxConcurrentRuns);
            _globalMemoryCeiling = globalMemoryCeiling;
            _leakedThreshold = leakedThreadThreshold;
        }

        public bool IsCircuitOpen { get { return Volatile.Read(ref _leakedThreads) >= _leakedThreshold; } }
        public int LeakedThreads { get { return Volatile.Read(ref _leakedThreads); } }

        public RunTicket TryAcquire(ResourceLimits limits, out ScriptError refusal)
        {
            refusal = null;
            if (IsCircuitOpen)
            {
                refusal = Refusal(ScriptErrorKind.HostError, "CircuitBreaker",
                    "engine unavailable: leaked thread limit reached");
                return null;
            }
            if (!_slots.Wait(0))
            {
                refusal = Refusal(ScriptErrorKind.BudgetExceeded, "MaxConcurrentRuns",
                    "engine saturated: too many concurrent runs");
                return null;
            }
            long newTotal = Interlocked.Add(ref _totalMemoryBudget, limits.MaxAllocBytes);
            if (newTotal > _globalMemoryCeiling)
            {
                Interlocked.Add(ref _totalMemoryBudget, -limits.MaxAllocBytes);
                _slots.Release();
                refusal = Refusal(ScriptErrorKind.BudgetExceeded, "GlobalMemoryCeiling",
                    "engine saturated: global memory budget exceeded");
                return null;
            }
            return new RunTicket { MemoryBudget = limits.MaxAllocBytes };
        }

        public void Release(RunTicket ticket)
        {
            if (ticket == null)
            {
                return;
            }
            if (Interlocked.Exchange(ref ticket.Released, 1) == 1)
            {
                return;   // idempotent against a double finally
            }
            Interlocked.Add(ref _totalMemoryBudget, -ticket.MemoryBudget);
            _slots.Release();
        }

        // Called by the host after WaitCompleted==false. Counts each thread at most once.
        public void RegisterLeak(RunnerThread t)
        {
            if (Interlocked.Exchange(ref t.LeakCounted, 1) == 1)
            {
                return;
            }
            int n = Interlocked.Increment(ref _leakedThreads);
            if (n == _leakedThreshold)
            {
                FireCircuit(n);   // closed -> open
            }
        }

        // Called by a leaked thread's ThreadMain finally on late completion.
        public void UnregisterLeak(RunnerThread t)
        {
            if (Interlocked.Exchange(ref t.LeakCounted, 2) != 1)
            {
                return;   // was never counted as a leak
            }
            int n = Interlocked.Decrement(ref _leakedThreads);
            if (n == _leakedThreshold - 1)
            {
                FireCircuit(n);   // open -> closed
            }
        }

        private void FireCircuit(int n)
        {
            Action<int> h = CircuitStateChanged;
            if (h == null)
            {
                return;
            }
            try
            {
                h(n);
            }
            catch (Exception)
            {
                // A host handler must never break the leak protocol.
            }
        }

        private static ScriptError Refusal(ScriptErrorKind kind, string limitName, string message)
        {
            return new ScriptError(kind, null, message, 0, 0, limitName, null);
        }
    }
}
