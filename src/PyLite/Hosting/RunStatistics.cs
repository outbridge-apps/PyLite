using System;

namespace Outbridge.PyLite.Hosting
{
    // Read-only per-run telemetry snapshot. Filled by the engine; never null in
    // any run outcome. The script has no access to it (a limit-probing channel is denied).
    public sealed class RunStatistics
    {
        public long Steps { get; internal set; }              // Budget steps used at exit
        public long AllocatedBytes { get; internal set; }     // Budget alloc bytes used
        public long OutputBytes { get; internal set; }        // Budget output bytes used
        public TimeSpan Elapsed { get; internal set; }        // run Stopwatch (marshaling included)
        public TimeSpan CompileTime { get; internal set; }    // 0 on a compiled-script cache hit
        public TimeSpan HostCallTime { get; internal set; }   // Σ host-delegate durations
        public TimeSpan RegexTime { get; internal set; }      // Σ regex time consumed
        public long SleepMs { get; internal set; }            // Σ sleep ms
        public int PeakCallDepth { get; internal set; }       // max CallDepth over the run
        public int ModulesImported { get; internal set; }     // ctx.ModuleInstances.Count at exit
        public int RegexOps { get; internal set; }            // count of regex operations
        public bool TerminatingEntered { get; internal set; } // Budget.IsTerminating at exit
        public bool TracebackTruncated { get; internal set; }
        public int LeakedThreadsAtStart { get; internal set; }// RunGate leak snapshot when the ticket issued
    }
}
