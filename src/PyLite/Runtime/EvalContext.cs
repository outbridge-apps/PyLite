using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Runtime
{
    // Per-run context; created exactly once by ScriptEngine. All mutable fields are
    // touched only from the run thread. Owns all per-run state so it is fully unrooted at run end.
    public sealed class EvalContext
    {
        // --- immutable part ---
        public Budget Budget;
        public ResourceLimits Limits;
        public ValueFactory Values;
        public ModuleRegistry Modules;            // shared, frozen factory registry
        public HostFunctionTable Builtins;        // frozen at run start
        public IScriptClock Clock;
        public ISleepPolicy SleepPolicy;
        public RunStatistics Stats;
        public uint StrHashSeed;                  // RNGCryptoServiceProvider, 4 bytes
        public System.Diagnostics.Stopwatch MonotonicClock;   // per-run, started at ctx creation
        public System.TimeSpan SleptTotal;        // accumulated time.sleep across the run
        public DecimalContext DecimalCtx = new DecimalContext();   // per-run decimal rounding
        public CsvState Csv = new CsvState();               // per-run csv dialect registry + field limit
        public RandomState Random;                // per-run PRNG, lazily created on first random-module use
        internal System.Numerics.BigInteger UuidLastTimestamp = System.Numerics.BigInteger.MinusOne;  // uuid1 collision counter
        internal System.Numerics.BigInteger UuidNode = System.Numerics.BigInteger.MinusOne;           // uuid1 cached random node
        internal Dictionary<string, string> XmlUriToPrefix;   // xml.register_namespace table, per-run

        // --- mutable per-run fields (RunnerThread only) ---
        public int CallDepth;              // ++/-- only in Evaluator.Call
        public int ExprProbeCounter;       // EvalExpr nesting counter for the probe cadence
        public int IterProbeCounter;       // MoveNext counter: probe cadence for nested lazy iterators
        public int NodeCounter;            // AST-node counter for NodeBatch charging
        public int CurrentLine;            // updated in each stmt prologue (for ScriptError)
        public string CurrentFunctionName; // for traceback frames
        internal Errors.ExceptionValue CurrentHandledException;   // the active except's exception (bare raise / implicit Context)

        // Reentrant call hook so builtins (map/filter/sorted key, min/max key) invoke a script callable
        // through the single Evaluator.Call point. Set by Evaluator.RunModule.
        internal Func<ScriptValue, ScriptValue[], KwArgs, ScriptValue> CallHook;

        // Single-positional-arg twin (T2.2): script functions ride the arrayless direct path, every
        // other callee falls back to CallHook with a one-slot array. Same prologue and semantics.
        internal Func<ScriptValue, ScriptValue, ScriptValue> CallHook1;

        // The format engine (str.format / format() / f-string specs / % operator). Installed by the host
        // (PyFormatServices) before the run; the Evaluator's f-string path calls through it.
        internal IFormatServices Format;

        // Per-run intern table for global-name keys (Environment.Key): one charged StrValue per distinct
        // name, its seeded hash cached inside — repeat global reads allocate nothing. Per-run because
        // StrValue caches its hash under this run's StrHashSeed.
        internal Dictionary<string, StrValue> GlobalNameKeys;

        // -- per-run module state (no mutable module state in statics) ---
        internal Dictionary<string, ModuleValue> ModuleInstances;   // per-run Import cache (Ordinal)
        internal HashSet<string> ModulesInitializing;               // cyclic-init guard
        internal Dictionary<string, object> ModuleState;            // mutable module state keyed by owner

        // --- output ---
        internal StringBuilder PrintBuffer;   // lazily created when print has no host sink AND no OutputSink
        internal PrintDelegate PrintSink;     // host print callback, or null
        internal OutputSink Out;              // installed by ScriptEngine.Run; when set, print routes here

        internal EvalContext(Budget budget, ResourceLimits limits, uint strHashSeed,
            ModuleRegistry modules, HostFunctionTable builtins, IScriptClock clock,
            ISleepPolicy sleepPolicy, RunStatistics stats)
        {
            Budget = budget;
            Limits = limits;
            StrHashSeed = strHashSeed;
            Modules = modules;
            Builtins = builtins;
            Clock = clock;
            SleepPolicy = sleepPolicy;
            Stats = stats;
            CurrentFunctionName = "<module>";
            GlobalNameKeys = new Dictionary<string, StrValue>(StringComparer.Ordinal);
            ModuleInstances = new Dictionary<string, ModuleValue>(StringComparer.Ordinal);
            ModulesInitializing = new HashSet<string>(StringComparer.Ordinal);
            ModuleState = new Dictionary<string, object>(StringComparer.Ordinal);
            MonotonicClock = System.Diagnostics.Stopwatch.StartNew();
            Values = new ValueFactory(budget, limits);
            Values.OwnerCtx = this;
        }
    }
}
