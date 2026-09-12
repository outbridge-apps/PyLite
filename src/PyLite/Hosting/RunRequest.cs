using System.Collections.Generic;
using System.Threading;

namespace Outbridge.PyLite.Hosting
{
    // One run's inputs. Copied into an immutable snapshot on entry to ScriptEngine.Run
    // later mutation does not affect the running script.
    public sealed class RunRequest
    {
        // Host inputs coerced (null => empty).
        public IReadOnlyDictionary<string, string> ScalarsIn { get; set; }

        // Host inputs parsed as JSON (null => empty).
        public IReadOnlyDictionary<string, string> JsonIn { get; set; }

        // Declared BEFORE the run: marshaled on the run thread under the budget.
        public IReadOnlyList<string> ScalarOutputNames { get; set; }
        public IReadOnlyList<string> JsonOutputNames { get; set; }

        // The builtins table for this run; null => the engine's default builtins. The engine clones + freezes
        // it so a script cannot see the table change under it.
        public HostFunctionTable Builtins { get; set; }

        // Per-line print callback, each line WITHOUT its trailing newline; null => the internal buffer
        // (RunResult.PrintOutput). When set, RunResult.PrintOutput is null (the host already received output).
        public PrintDelegate PrintSink { get; set; }

        // Per-run limits, clamped downward onto EngineOptions.DefaultLimits; null => DefaultLimits as-is.
        public ResourceLimits Limits { get; set; }

        // Host cancellation, polled by the Budget.
        public CancellationToken CancellationToken { get; set; }

        public RunRequest() { }
    }
}
