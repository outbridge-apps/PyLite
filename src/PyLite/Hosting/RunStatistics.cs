using System;

namespace Outbridge.PyLite.Hosting
{
    /// <summary>
    /// What one run used, filled by the engine; never null, whatever the outcome. Scripts cannot read it, so it is
    /// no channel for probing the limits.
    /// </summary>
    public sealed class RunStatistics
    {
        /// <summary>The steps charged (see <see cref="ResourceLimits.MaxSteps"/>).</summary>
        public long Steps { get; internal set; }

        /// <summary>The bytes charged against <see cref="ResourceLimits.MaxAllocBytes"/>.</summary>
        public long AllocatedBytes { get; internal set; }

        /// <summary>The printed bytes charged against <see cref="ResourceLimits.MaxOutputBytes"/>.</summary>
        public long OutputBytes { get; internal set; }

        /// <summary>The run's time, the conversion of inputs and outputs included.</summary>
        public TimeSpan Elapsed { get; internal set; }

        /// <summary>The compile <c>Run(string, …)</c> did; zero on a cache hit and for a precompiled script.</summary>
        public TimeSpan CompileTime { get; internal set; }

        /// <summary>The time spent in host functions the script called.</summary>
        public TimeSpan HostCallTime { get; internal set; }

        /// <summary>The time regular expressions took.</summary>
        public TimeSpan RegexTime { get; internal set; }

        /// <summary>The milliseconds <c>time.sleep</c> waited.</summary>
        public long SleepMs { get; internal set; }

        /// <summary>The deepest call nesting the run reached.</summary>
        public int PeakCallDepth { get; internal set; }

        /// <summary>The modules the run imported.</summary>
        public int ModulesImported { get; internal set; }

        /// <summary>The regular expression operations the run made.</summary>
        public int RegexOps { get; internal set; }

        /// <summary>A limit was reached and the run was in its grace period when it ended.</summary>
        public bool TerminatingEntered { get; internal set; }

        /// <summary>The script traceback was cut to its maximum frames.</summary>
        public bool TracebackTruncated { get; internal set; }

        /// <summary>The engine's left-behind threads when the run was admitted.</summary>
        public int LeakedThreadsAtStart { get; internal set; }

        // The exit-time fields; the running totals (host/regex time, depth) accumulate during the run.
        internal void Snapshot(Runtime.EvalContext ctx)
        {
            Runtime.Budget budget = ctx.Budget;
            Steps = budget.StepsUsed;
            AllocatedBytes = budget.AllocBytesUsed;
            OutputBytes = budget.OutputBytesUsed;
            SleepMs = budget.SleepMsUsed;
            RegexTime = TimeSpan.FromTicks(budget.RegexTicksUsed);
            RegexOps = budget.RegexOps;
            Elapsed = budget.Elapsed;
            ModulesImported = ctx.ModuleInstances.Count;
            TerminatingEntered = budget.IsTerminating;
        }
    }
}
