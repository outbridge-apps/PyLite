namespace Outbridge.PyLite.Hosting
{
    /// <summary>
    /// Engine telemetry categories, delivered to <see cref="EngineOptions.EventSink"/> inside a try/catch so a
    /// telemetry handler cannot fail a run; it runs synchronously, so a slow one delays the run's return.
    /// </summary>
    public enum EngineEventKind
    {
        /// <summary>A run's thread did not stop after its run ended and was left behind; it counts as leaked until it returns.</summary>
        ThreadLeaked,
        /// <summary>Too many leaked threads: the engine refuses new runs until they return.</summary>
        CircuitOpened,
        /// <summary>The leaked threads returned and the engine accepts runs again.</summary>
        CircuitClosed,
        /// <summary>An internal engine error ended a run (a defect, never a script error).</summary>
        EngineFault,
        /// <summary>
        /// A call to <c>Run</c> ended, in any way: a syntax error, a refusal and a left-behind thread included;
        /// <see cref="EngineEvent.Stats"/> holds its statistics.
        /// </summary>
        RunCompleted,
    }

    /// <summary>One telemetry event of an engine.</summary>
    public sealed class EngineEvent
    {
        /// <summary>What happened.</summary>
        public EngineEventKind Kind { get; }

        /// <summary>A description for logs.</summary>
        public string Message { get; }

        /// <summary>The run the event belongs to, numbered per engine.</summary>
        public long RunId { get; }

        /// <summary>The run's statistics; non-null only for <see cref="EngineEventKind.RunCompleted"/>.</summary>
        public RunStatistics Stats { get; }

        internal EngineEvent(EngineEventKind kind, string message, long runId, RunStatistics stats)
        {
            Kind = kind;
            Message = message;
            RunId = runId;
            Stats = stats;
        }
    }
}
