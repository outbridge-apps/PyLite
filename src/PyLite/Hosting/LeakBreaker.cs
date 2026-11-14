using System;
using System.Threading;

namespace Outbridge.PyLite.Hosting
{
    // The leaked-thread circuit breaker, one per ScriptEngine. A hung host call leaves its runner thread behind while
    // the caller gets an error and may retry: at the threshold new runs are refused until those threads return, or
    // they would pile up until the process dies. It is the engine's only admission check: how many runs execute at
    // once is the host's, whose own threads call Run.
    internal sealed class LeakBreaker
    {
        private int _leakedThreads;   // Interlocked
        private readonly int _threshold;

        public event Action<int> CircuitStateChanged;    // host telemetry only; never reaches scripts

        public LeakBreaker(int leakedThreadThreshold)
        {
            if (leakedThreadThreshold < 1)
                throw new ArgumentOutOfRangeException(nameof(leakedThreadThreshold));
            _threshold = leakedThreadThreshold;
        }

        public bool IsCircuitOpen { get { return Volatile.Read(ref _leakedThreads) >= _threshold; } }
        public int LeakedThreads { get { return Volatile.Read(ref _leakedThreads); } }

        // Called by the host after WaitCompleted==false. Counts a thread only while it still runs (state 0),
        // once; false when it finished in between (state 2): such a thread is not leaked, and counting it
        // would hold the breaker open forever, since no finally is left to uncount it.
        public bool RegisterLeak(RunnerThread t)
        {
            if (Interlocked.CompareExchange(ref t.LeakCounted, 1, 0) != 0)
                return false;
            int n = Interlocked.Increment(ref _leakedThreads);
            if (n == _threshold)
                FireCircuit(n);   // closed -> open
            return true;
        }

        // Called by a leaked thread's ThreadMain finally on late completion.
        public void UnregisterLeak(RunnerThread t)
        {
            if (Interlocked.Exchange(ref t.LeakCounted, 2) != 1)
                return;   // was never counted as a leak
            int n = Interlocked.Decrement(ref _leakedThreads);
            if (n == _threshold - 1)
                FireCircuit(n);   // open -> closed
        }

        private void FireCircuit(int n)
        {
            Action<int> h = CircuitStateChanged;
            if (h == null)
                return;
            try
            {
                h(n);
            }
            catch (Exception)
            {
                // A host handler must never break the leak protocol.
            }
        }

        public static ScriptError Refusal()
        {
            return new ScriptError(ScriptErrorKind.HostError, null,   // 1.1.0's name: hosts match on it
                "engine unavailable: leaked thread limit reached", 0, 0, "CircuitBreaker", null);
        }
    }
}
