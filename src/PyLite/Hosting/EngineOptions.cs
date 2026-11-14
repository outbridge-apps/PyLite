using System;
using System.Collections.Generic;

namespace Outbridge.PyLite.Hosting
{
    /// <summary>
    /// An engine's configuration. The <see cref="ScriptEngine"/> constructor copies it, so a later change does not
    /// reach a built engine.
    /// </summary>
    public sealed class EngineOptions
    {
        /// <summary>
        /// The limits of a run that names none, and the ceiling every run's <see cref="RunRequest.Limits"/> is
        /// lowered to: a request cannot raise a limit past this.
        /// </summary>
        public ResourceLimits DefaultLimits { get; set; } = ResourceLimits.Default;

        /// <summary>The time scripts see; by default the real clock with UTC as the local zone.</summary>
        public IScriptClock Clock { get; set; } = new SystemClock(TimeSpan.Zero, "UTC");

        /// <summary>How <c>time.sleep</c> waits; by default as requested, within <see cref="ResourceLimits.SleepTotalMs"/>.</summary>
        public ISleepPolicy SleepPolicy { get; set; } = new DefaultSleepPolicy();

        /// <summary>
        /// The compiled scripts <c>Run(string, …)</c> keeps, keyed by their source; 0 disables the cache, and a full
        /// cache is cleared.
        /// </summary>
        public int CompiledScriptCacheEntries { get; set; } = 256;

        /// <summary>
        /// The str and bytes hash seed. Fixed by default, so <c>hash('a')</c> is the same in every run and after a
        /// restart, as everything else in the engine is deterministic. Null: a fresh random seed per engine, which
        /// trades that for resistance to hash flooding when dict keys come from outside (a flood ends on the step
        /// limit either way, since every table probe is charged).
        /// </summary>
        public uint? StrHashSeed { get; set; } = 0x9E3779B9;

        // Tests only: runs every script on the tree walk alone (EvalContext.InterpretOnly).
        internal bool InterpretOnly { get; set; }

        /// <summary>Modules the host implements in C#: native capabilities belong here, reviewed as engine code.</summary>
        public IReadOnlyList<IModuleProvider> ExtraModules { get; set; }

        /// <summary>
        /// Modules written in the dialect, module name to source. They are compiled when the engine is built (a
        /// syntax error throws <see cref="ArgumentException"/>) and run inside each run's budget.
        /// </summary>
        public IReadOnlyDictionary<string, string> PythonModules { get; set; }

        /// <summary>
        /// Modules (of <see cref="PythonModules"/> or <see cref="ExtraModules"/>) loaded before the main script, their
        /// public top-level names (not starting with '_') put into its globals, as an implicit <c>import *</c>.
        /// </summary>
        public IReadOnlyList<string> PreludeModules { get; set; }

        /// <summary>
        /// Replaced members of built-in modules, module name to member name to function; a null function removes
        /// the member. Applied when a run imports the module; an unknown module name fails the engine build.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, Outbridge.PyLite.Runtime.Values.BuiltinDelegate>> ModuleOverrides { get; set; }

        /// <summary>Giving memory back after big runs; null: off.</summary>
        public MemoryHygieneOptions MemoryHygiene { get; set; }

        /// <summary>Ignored. The engine does not limit how many runs execute at once.</summary>
        [Obsolete("Ignored: the engine does not limit how many runs execute at once. A host that wants a limit keeps it around its own calls to Run.")]
        public int MaxConcurrentRuns { get; set; } = 8;

        /// <summary>Ignored. The engine does not limit the memory of all running runs together.</summary>
        [Obsolete("Ignored: the engine does not limit the memory of all running runs together. Size DefaultLimits.MaxAllocBytes for the runs you start at once.")]
        public long GlobalMemoryCeilingBytes { get; set; }

        // Tests only: what a run must leave free besides its own stack and memory limit.
        internal long FreeCommitReserveBytes { get; set; } = 200L << 20;

        /// <summary>The left-behind threads at which the engine stops admitting runs until they return.</summary>
        public int CircuitBreakerLeakedThreads { get; set; } = 4;

        /// <summary>
        /// Receives the engine's telemetry, inside a try/catch so it cannot fail a run. It is called synchronously:
        /// a slow sink delays the run's return, so hand slow work off.
        /// </summary>
        public Action<EngineEvent> EventSink { get; set; }

        /// <summary>The defaults.</summary>
        public EngineOptions() { }
    }
}
