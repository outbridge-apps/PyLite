using System;
using System.Collections.Generic;

namespace Outbridge.PyLite.Hosting
{
    // (concurrency + telemetry fields live at the bottom of the class)
    // Engine-wide configuration, snapshotted by the ScriptEngine constructor (later mutation
    // does not affect a built engine). PythonModules and PreludeModules extend the module set.
    public sealed class EngineOptions
    {
        // The server ceiling used for compilation and as the default per-run limit (compile uses this
        // only). A per-run RunRequest.Limits is clamped downward onto this.
        public ResourceLimits DefaultLimits { get; set; } = ResourceLimits.Default;

        // Deterministic UTC clock by default (the host overrides per company); no-cost sleep cap.
        public IScriptClock Clock { get; set; } = new SystemClock(TimeSpan.Zero, "UTC");
        public ISleepPolicy SleepPolicy { get; set; } = new DefaultSleepPolicy();

        // Built-in CompiledScript cache size: 0 disables; overflow triggers a full clear.
        public int CompiledScriptCacheEntries { get; set; } = 256;

        // The str/bytes hash seed. Fixed by default, so hash('a') is the same number in every run and
        // after every host restart — the engine is deterministic everywhere else (no addresses in repr,
        // id() is a counter, gzip mtime is 0) and a hash that moved under the host would be the odd one
        // out. Set to null for a fresh cryptographic seed per engine: that trades the reproducibility
        // for hash-flooding resistance, which matters when dict keys come from outside (json.loads of a
        // partner payload) — though a flood there burns the step budget and aborts the run rather than
        // hanging the host, because every table probe charges a step.
        public uint? StrHashSeed { get; set; } = 0x9E3779B9;

        // C#-implemented host modules (native capabilities live here — reviewed as engine code).
        public IReadOnlyList<IModuleProvider> ExtraModules { get; set; }

        // Python-source host libraries, module name -> dialect source. Compiled and dialect-checked
        // at engine build (a syntax error throws ArgumentException); executed per-run under the run budget.
        public IReadOnlyDictionary<string, string> PythonModules { get; set; }

        // names (of PythonModules OR ExtraModules) auto-loaded before the main script; their public
        // top-level names (not starting with '_') are injected into the script globals (implicit import *).
        public IReadOnlyList<string> PreludeModules { get; set; }

        // member-level stdlib patching, module name -> (member name -> delegate
        // a NULL delegate removes the member). Applied when a module materializes in a run; overriding
        // delegates are HostCallGuard-fenced. An unknown module name fails the engine build (host bug).
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, Outbridge.PyLite.Runtime.Values.BuiltinDelegate>> ModuleOverrides { get; set; }

        // opt-in post-run RAM-return knobs; null => both off.
        public MemoryHygieneOptions MemoryHygiene { get; set; }

        // Concurrency + protection. Each run executes on a dedicated 32 MB thread (stack safety on the
        // host's 1 MB stack); the gate admits at most MaxConcurrentRuns and Σ(MaxAllocBytes) ≤ the ceiling.
        public int MaxConcurrentRuns { get; set; } = 8;                             // clamped to hard cap 32
        public long GlobalMemoryCeilingBytes { get; set; } = 1L << 30;              // 1 GB
        public int CircuitBreakerLeakedThreads { get; set; } = 4;                   // open the breaker at N leaks

        // Telemetry sink (leaked/circuit/fault/completed), invoked in try/catch — never affects a run.
        public Action<EngineEvent> EventSink { get; set; }

        public EngineOptions() { }
    }
}
