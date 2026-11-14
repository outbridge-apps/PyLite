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
        internal Errors.ExceptionValue CaughtStop;   // the StopIteration of a callback that just ended an iterator
        internal int CaughtStopDepth;
        internal object ReversedNeedle;   // PySearch: the needle of the last backward search and its table
        internal int[] ReversedFail;
        public ModuleRegistry Modules;            // shared, frozen factory registry
        public HostFunctionTable Builtins;        // frozen at run start
        public IScriptClock Clock;
        public ISleepPolicy SleepPolicy;
        public RunStatistics Stats;
        public uint StrHashSeed;                  // RNGCryptoServiceProvider, 4 bytes
        public System.Diagnostics.Stopwatch MonotonicClock;   // per-run, started at ctx creation
        public System.TimeSpan SleptTotal;        // accumulated time.sleep across the run
        public DecimalContext DecimalCtx = new DecimalContext();   // per-run decimal rounding
        internal ScriptValue DecimalView;   // the decimal.Context object for DecimalCtx, made on first getcontext()
        public CsvState Csv = new CsvState();               // per-run csv dialect registry + field limit
        public RandomState Random;                // per-run PRNG, lazily created on first random-module use

        internal Dictionary<string, string> XmlUriToPrefix;   // xml.register_namespace table, per-run

        // --- mutable per-run fields (RunnerThread only) ---
        public int CallDepth;              // ++/-- only in Evaluator.Call
        internal long[] StarCopyPaid;      // CallMachinery.PayStarCopy: the largest f(*seq) copy paid at each call depth
        internal int StarCopyFreeDepth = int.MaxValue;   // the shallowest depth a copy was made at: its copies are free
        internal long XmlTreeVersion;      // ElementValue.Changed: changes to element children lists so far
        internal long XmlShared;           // ElementValue.Placing: list slots past each element's first (0: none is in two)
        internal Values.ElementXPath.ParentMapCache XmlParentMaps;   // the run's last few ElementPath parent maps
        internal Values.ElementXPath.PathCache XmlPaths;   // the run's parsed ElementPath paths
        public int IterProbeCounter;       // MoveNext counter: probe cadence for nested lazy iterators
        internal System.Collections.Generic.HashSet<ScriptValue> LeafReprOpen;   // deque/ChainMap being printed (repr cycle marker)
        internal System.Collections.Generic.HashSet<ScriptValue> ReprOpen;       // containers open in the repr being built
        public int NodeCounter;            // AST-node counter for NodeBatch charging
        public int CurrentLine;            // updated in each stmt prologue (for ScriptError)
        public string CurrentFunctionName; // for traceback frames
        internal Errors.ExceptionValue CurrentHandledException;   // the active except's exception (bare raise / implicit Context)

        // Reentrant call hook so builtins (map/filter/sorted key, min/max key) invoke a script callable
        // through the single Evaluator.Call point. Set by Evaluator.RunModule.
        internal Func<ScriptValue, ScriptValue[], KwArgs, ScriptValue> CallHook;

        // Single-positional-arg twin: script functions ride the arrayless direct path, every
        // other callee falls back to CallHook with a one-slot array. Same prologue and semantics.
        internal Func<ScriptValue, ScriptValue, ScriptValue> CallHook1;

        // super().__init__(...) reaching the __init__ @dataclass generated: (class, instance, args, kw).
        internal Func<RecordClassValue, RecordInstanceValue, ScriptValue[], KwArgs, ScriptValue> DataclassInitHook;

        // IntEnum/StrEnum members: the int/str object a member is, by identity -> the member (null until an enum has one).
        internal Dictionary<object, EnumMemberValue> EnumValues;
        internal int ClassAttrVersion;   // bumped on every class-attribute assignment (the evaluator's method sites)
        internal char[] DigitScratch;   // IntValue.DecimalDigits: one buffer per run (the run is one thread)

        // The format engine (str.format / format() / f-string specs / % operator). Installed by the host
        // (PyFormatServices) before the run; the Evaluator's f-string path calls through it.
        internal IFormatServices Format;

        // Tests only: every fast tier (compiled closures, fused longs, flat comprehension drain, range
        // loop) steps aside, so one corpus run per mode proves the tiers add speed, never semantics.
        internal bool InterpretOnly;

        // The run's evaluator per compiled unit (main script, Python modules, preludes): Evaluator.For.
        internal Dictionary<Syntax.ResolvedProgram, Evaluator.Evaluator> Evaluators;

        // Per-run intern table for global-name keys (Environment.Key): one charged StrValue per distinct
        // name, its seeded hash cached inside — repeat global reads allocate nothing. Per-run because
        // StrValue caches its hash under this run's StrHashSeed.
        internal Dictionary<string, StrValue> GlobalNameKeys;

        // Call sites whose inline cache this run set to a type it created (a namedtuple class), each with the
        // last entry it wrote there. The AST outlives the run in the engine's compiled-script cache, so
        // ReleaseRunCaches clears them when the run ends.
        internal Dictionary<Syntax.Ast.CallNode, object> RunTypedCallSites;

        internal void ReleaseRunCaches()
        {
            Dictionary<Syntax.Ast.CallNode, object> sites = RunTypedCallSites;
            if (sites == null)
                return;
            RunTypedCallSites = null;
            foreach (KeyValuePair<Syntax.Ast.CallNode, object> kv in sites)
                System.Threading.Interlocked.CompareExchange(ref kv.Key.CachedCallSlot, null, kv.Value);   // a newer entry is its run's to clear
        }

        // -- per-run module state (no mutable module state in statics) ---
        internal Dictionary<string, ModuleValue> ModuleInstances;   // per-run Import cache (Ordinal)
        internal HashSet<string> ModulesInitializing;               // cyclic-init guard
        internal Dictionary<string, object> ModuleState;            // mutable module state keyed by owner

        // --- output ---
        internal StringBuilder PrintBuffer;   // lazily created when print has no host sink AND no OutputSink
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
