using System.Collections.Generic;
using System.Threading;

namespace Outbridge.PyLite.Hosting
{
    /// <summary>
    /// One run's inputs. <c>ScriptEngine.Run</c> copies it on entry, so a later change does not reach the running
    /// script.
    /// </summary>
    public sealed class RunRequest
    {
        /// <summary>Input variables from text: an int, a float or a str by what the text spells. Null: none.</summary>
        public IReadOnlyDictionary<string, string> ScalarsIn { get; set; }

        /// <summary>Input variables parsed from JSON documents. Null: none.</summary>
        public IReadOnlyDictionary<string, string> JsonIn { get; set; }

        /// <summary>
        /// The variables to return as text in <see cref="RunResult.ScalarsOut"/>, named before the run: they are
        /// converted on the run's thread, under its budget.
        /// </summary>
        public IReadOnlyList<string> ScalarOutputNames { get; set; }

        /// <summary>The variables to return as JSON in <see cref="RunResult.JsonOut"/>, named before the run.</summary>
        public IReadOnlyList<string> JsonOutputNames { get; set; }

        /// <summary>
        /// The builtins for this run; null: the engine's defaults. The engine takes a frozen copy, so a script never
        /// sees the table change under it.
        /// </summary>
        public HostFunctionTable Builtins { get; set; }

        /// <summary>
        /// Receives each printed line, without its trailing newline. Null: the output is collected into
        /// <see cref="RunResult.PrintOutput"/>, which is null when this is set.
        /// </summary>
        public PrintDelegate PrintSink { get; set; }

        /// <summary>
        /// This run's limits. They are only ever lowered to the engine's <see cref="EngineOptions.DefaultLimits"/>,
        /// never raised past them. Null: the engine's limits as they are.
        /// </summary>
        public ResourceLimits Limits { get; set; }

        /// <summary>Ends the run when cancelled; the budget polls it.</summary>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>An empty request: no inputs, no outputs, the engine's limits.</summary>
        public RunRequest() { }

        // The copy a run reads (the header's promise): the host's dictionaries and lists are copied, the rest
        // is immutable or a delegate. A concurrent change of a source collection throws InvalidOperationException.
        internal RunRequest Snapshot()
        {
            return new RunRequest
            {
                ScalarsIn = Copy(ScalarsIn),
                JsonIn = Copy(JsonIn),
                ScalarOutputNames = ScalarOutputNames == null ? null : new List<string>(ScalarOutputNames).ToArray(),
                JsonOutputNames = JsonOutputNames == null ? null : new List<string>(JsonOutputNames).ToArray(),
                Builtins = FrozenCopy(Builtins),   // here, on the caller's thread: a host changing it meanwhile faulted runs
                PrintSink = PrintSink,
                Limits = Limits,
                CancellationToken = CancellationToken,
            };
        }

        private static HostFunctionTable FrozenCopy(HostFunctionTable source)
        {
            if (source == null)
                return null;
            HostFunctionTable copy;
            try
            {
                copy = source.Clone();
            }
            catch (System.Exception ex) when (!(ex is System.InvalidOperationException))
            {
                // a copy torn by a concurrent writer can fail in other ways than the version check
                throw new System.InvalidOperationException("the builtins table was modified during the copy", ex);
            }
            copy.Freeze();
            return copy;
        }

        private static IReadOnlyDictionary<string, string> Copy(IReadOnlyDictionary<string, string> source)
        {
            if (source == null)
                return null;
            var copy = new Dictionary<string, string>(source.Count, System.StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kv in source)
                copy[kv.Key] = kv.Value;
            return copy;
        }
    }
}
