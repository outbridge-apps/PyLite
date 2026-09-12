using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Outbridge.PyLite.Hosting
{
    // The outcome of one run (built here by the runtime for). Immutable
    // outputs are empty (never null) on failure.
    public sealed class RunResult
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyDict =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

        public bool Succeeded { get { return Error == null; } }
        public ScriptError Error { get; }
        public IReadOnlyDictionary<string, string> ScalarsOut { get; }
        public IReadOnlyDictionary<string, string> JsonOut { get; }
        public string PrintOutput { get; }      // internal buffer if no PrintSink; null for a leaked run
        public RunStatistics Stats { get; }

        internal RunResult(ScriptError error, IReadOnlyDictionary<string, string> scalars,
            IReadOnlyDictionary<string, string> json, string printOutput, RunStatistics stats)
        {
            Error = error;
            ScalarsOut = scalars ?? EmptyDict;
            JsonOut = json ?? EmptyDict;
            PrintOutput = printOutput;
            Stats = stats ?? new RunStatistics();
        }

        internal static RunResult Success(IReadOnlyDictionary<string, string> scalars,
            IReadOnlyDictionary<string, string> json, string printOutput, RunStatistics stats)
        {
            return new RunResult(null, scalars, json, printOutput, stats);
        }

        internal static RunResult Failure(ScriptError err, string printOutput, RunStatistics stats)
        {
            return new RunResult(err, EmptyDict, EmptyDict, printOutput, stats);
        }
    }
}
