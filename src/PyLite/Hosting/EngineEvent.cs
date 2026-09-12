namespace Outbridge.PyLite.Hosting
{
    // Engine telemetry categories. Delivered to EngineOptions.EventSink, always in a
    // try/catch so a telemetry handler can never affect a run.
    public enum EngineEventKind
    {
        ThreadLeaked,
        CircuitOpened,
        CircuitClosed,
        EngineFault,
        RunCompleted,
    }

    public sealed class EngineEvent
    {
        public EngineEventKind Kind { get; }
        public string Message { get; }
        public long RunId { get; }
        public RunStatistics Stats { get; }   // non-null only for RunCompleted

        internal EngineEvent(EngineEventKind kind, string message, long runId, RunStatistics stats)
        {
            Kind = kind;
            Message = message;
            RunId = runId;
            Stats = stats;
        }
    }
}
