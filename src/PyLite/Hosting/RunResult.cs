using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Outbridge.PyLite.Hosting
{
    /// <summary>The outcome of one run. Immutable; the outputs are empty, never null, when the run failed.</summary>
    public sealed class RunResult
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyDict =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

        /// <summary>The run ended without an error.</summary>
        public bool Succeeded { get { return Error == null; } }

        /// <summary>Why the run failed, or null when it succeeded.</summary>
        public ScriptError Error { get; }

        /// <summary>The variables named in <see cref="RunRequest.ScalarOutputNames"/>, as text.</summary>
        public IReadOnlyDictionary<string, string> ScalarsOut { get; }

        /// <summary>The variables named in <see cref="RunRequest.JsonOutputNames"/>, as JSON documents.</summary>
        public IReadOnlyDictionary<string, string> JsonOut { get; }

        /// <summary>
        /// What the script printed, also on a failed run; "" when nothing was, a run that never started included.
        /// Null when <see cref="RunRequest.PrintSink"/> was set or the run's thread was left behind.
        /// </summary>
        public string PrintOutput { get; }

        /// <summary>What the run used: steps, memory, time.</summary>
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
