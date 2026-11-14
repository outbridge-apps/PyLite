using System;

namespace Outbridge.PyLite.Hosting
{
    /// <summary>
    /// How <c>time.sleep</c> waits. The engine waits in quanta, checking the run's deadline and cancellation
    /// between them; the run's <see cref="ResourceLimits.SleepTotalMs"/> bounds the total either way.
    /// </summary>
    public interface ISleepPolicy
    {
        /// <summary>The wait to take for one call.</summary>
        /// <param name="requested">What the script asked for; not negative.</param>
        /// <param name="sleptSoFar">What this run has slept already.</param>
        TimeSpan Clamp(TimeSpan requested, TimeSpan sleptSoFar);

        /// <summary>The wait quantum, 1 ms to 1 s (any other ends the run as a host error); a deadline or a cancellation is noticed once per quantum.</summary>
        TimeSpan Quantum { get; }
    }

    /// <summary>Waits as requested, in 25 ms quanta.</summary>
    public sealed class DefaultSleepPolicy : ISleepPolicy
    {
        private static readonly TimeSpan Q = TimeSpan.FromMilliseconds(25);

        /// <inheritdoc/>
        public TimeSpan Quantum { get { return Q; } }

        /// <inheritdoc/>
        public TimeSpan Clamp(TimeSpan requested, TimeSpan sleptSoFar) { return requested; }
    }

    /// <summary>Every sleep returns at once (it is still charged a step): for hosts where scripts must not wait.</summary>
    public sealed class NoOpSleepPolicy : ISleepPolicy
    {
        private static readonly TimeSpan Q = TimeSpan.FromMilliseconds(25);

        /// <inheritdoc/>
        public TimeSpan Quantum { get { return Q; } }

        /// <inheritdoc/>
        public TimeSpan Clamp(TimeSpan requested, TimeSpan sleptSoFar) { return TimeSpan.Zero; }
    }
}
