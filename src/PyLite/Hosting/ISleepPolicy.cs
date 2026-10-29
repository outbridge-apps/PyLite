using System;

namespace Outbridge.PyLite.Hosting
{
    // Cooperative sleep policy. time.sleep waits in quanta; this bounds the total.
    public interface ISleepPolicy
    {
        // Effective wait for this call. requested >= 0; sleptSoFar = total already slept this run.
        TimeSpan Clamp(TimeSpan requested, TimeSpan sleptSoFar);
        TimeSpan Quantum { get; }   // wait quantum, 25..50 ms
    }

    // Real waits as requested; the run's ResourceLimits.SleepTotalMs bounds the total. Quantum 25 ms.
    public sealed class DefaultSleepPolicy : ISleepPolicy
    {
        private static readonly TimeSpan Q = TimeSpan.FromMilliseconds(25);

        public TimeSpan Quantum { get { return Q; } }

        public TimeSpan Clamp(TimeSpan requested, TimeSpan sleptSoFar) { return requested; }
    }

    // A production host may install this: every sleep returns instantly (but is still charged a Step).
    public sealed class NoOpSleepPolicy : ISleepPolicy
    {
        private static readonly TimeSpan Q = TimeSpan.FromMilliseconds(25);
        public TimeSpan Quantum { get { return Q; } }
        public TimeSpan Clamp(TimeSpan requested, TimeSpan sleptSoFar) { return TimeSpan.Zero; }
    }
}
