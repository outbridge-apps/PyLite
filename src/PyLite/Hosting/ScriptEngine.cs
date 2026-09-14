using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;

namespace Outbridge.PyLite.Hosting
{
    // The engine's public entry point. Immutable after construction: it owns the
    // shared frozen ModuleRegistry (built-in + host C# + Python modules), the CompiledScript cache
    // the concurrency gate and the run pipeline. Every Run executes on a dedicated 32 MB RunnerThread
    // (parser+evaluator off the host's 1 MB stack) admitted through RunGate (semaphore + memory ceiling + leaked-
    // thread circuit breaker). Compilation runs on the calling thread (bounded recursion via StackGuard).
    public sealed class ScriptEngine
    {
        private const int LeakJoinSlackMs = 5000;   // extra Join wait past deadline+grace before declaring a leak
        private static readonly string[] EmptyStrings = new string[0];

        private readonly ResourceLimits _defaultLimits;
        private readonly IScriptClock _clock;
        private readonly ISleepPolicy _sleepPolicy;
        private readonly uint _strHashSeed;   // fixed at build (EngineOptions contract: per ENGINE, not per run)
        private readonly ModuleRegistry _modules;         // shared, frozen
        private readonly IReadOnlyList<string> _prelude;

        private readonly RunGate _gate;
        private readonly int _breakerThreshold;
        private readonly Action<EngineEvent> _eventSink;
        private long _runIdCounter;

        private readonly int _cacheCap;
        private readonly object _cacheLock = new object();
        private readonly Dictionary<string, CompiledScript> _cache;   // SHA-256 hex -> compiled; null if disabled
        private readonly MemoryHygieneOptions _hygiene;

        public ScriptEngine(EngineOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            _defaultLimits = options.DefaultLimits ?? ResourceLimits.Default;
            _clock = options.Clock ?? new SystemClock(TimeSpan.Zero, "UTC");
            _sleepPolicy = options.SleepPolicy ?? new DefaultSleepPolicy();
            _strHashSeed = options.StrHashSeed ?? RandomSeed();
            _prelude = options.PreludeModules ?? EmptyStrings;
            _cacheCap = options.CompiledScriptCacheEntries;
            _cache = _cacheCap > 0 ? new Dictionary<string, CompiledScript>(StringComparer.Ordinal) : null;
            _eventSink = options.EventSink;
            _breakerThreshold = options.CircuitBreakerLeakedThreads;
            _hygiene = options.MemoryHygiene;   // null => off

            _gate = new RunGate(options.MaxConcurrentRuns, options.GlobalMemoryCeilingBytes, _breakerThreshold);
            _gate.CircuitStateChanged += OnCircuitStateChanged;

            _modules = BuildRegistry(options, _prelude);
        }

        private void OnCircuitStateChanged(int leakedCount)
        {
            RaiseEvent(new EngineEvent(
                leakedCount >= _breakerThreshold ? EngineEventKind.CircuitOpened : EngineEventKind.CircuitClosed,
                "leaked runner threads: " + leakedCount, 0, null));
        }

        private void RaiseEvent(EngineEvent ev)
        {
            Action<EngineEvent> sink = _eventSink;
            if (sink == null)
                return;
            try
            {
                sink(ev);
            }
            catch (Exception)
            {
                // Telemetry must never affect a run.
            }
        }

        // The server ceiling per-run limits are clamped down onto (consumed by ScriptRunner.BuildLimits).
        internal ResourceLimits DefaultLimits { get { return _defaultLimits; } }

        // --- registry assembly (built-ins + host C# ExtraModules + PythonModules) ----

        private static ModuleRegistry BuildRegistry(EngineOptions options, IReadOnlyList<string> prelude)
        {
            var registry = new ModuleRegistry();
            DefaultModules.RegisterBuiltins(registry);

            // Host sources replace earlier registrations (whole-module last-wins policy
            // builtins < ExtraModules < PythonModules); duplicates WITHIN one source stay an error.
            if (options.ExtraModules != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (IModuleProvider provider in options.ExtraModules)
                {
                    if (provider == null || string.IsNullOrEmpty(provider.Name))
                        throw new ArgumentException("EngineOptions.ExtraModules contains a null provider or one with an empty Name");
                    if (!seen.Add(provider.Name))
                        throw new ArgumentException("EngineOptions.ExtraModules registers module '" + provider.Name + "' twice");
                    IModuleProvider captured = provider;
                    registry.Replace(provider.Name, ctx => captured.CreateInstance(ctx));
                }
            }

            if (options.PythonModules != null)
            {
                foreach (KeyValuePair<string, string> kv in options.PythonModules)
                {
                    CompiledScript compiled = CompilePreludeModule(kv.Key, kv.Value);
                    string name = kv.Key;
                    registry.Replace(name, ctx => LoadPythonModule(name, compiled, ctx));
                }
            }

            ApplyModuleOverrides(registry, options);

            foreach (string name in prelude)
            {
                if (!registry.IsRegistered(name))
                    throw new ArgumentException("EngineOptions.PreludeModules references an unregistered module '" + name + "'");
            }

            registry.Freeze();
            return registry;
        }

        // wrap each overridden module's factory so replace/add/remove happens
        // when the module materializes in a run. Overriding delegates are registered as host-provided
        // builtins, so their calls are HostCallGuard-fenced (arbitrary exception -> HostError).
        private static void ApplyModuleOverrides(ModuleRegistry registry, EngineOptions options)
        {
            if (options.ModuleOverrides == null)
                return;
            foreach (KeyValuePair<string, IReadOnlyDictionary<string, BuiltinDelegate>> entry in options.ModuleOverrides)
            {
                if (!registry.IsRegistered(entry.Key))
                    throw new ArgumentException("EngineOptions.ModuleOverrides references an unregistered module '" + entry.Key + "'");
                if (entry.Value == null)
                    throw new ArgumentException("EngineOptions.ModuleOverrides['" + entry.Key + "'] is null");
                Func<EvalContext, ModuleValue> baseFactory = registry.GetFactory(entry.Key);
                string modName = entry.Key;
                IReadOnlyDictionary<string, BuiltinDelegate> overrides = entry.Value;
                registry.Replace(modName, ctx => OverrideMembers(modName, baseFactory(ctx), overrides, ctx));
            }
        }

        private static ModuleValue OverrideMembers(string name, ModuleValue baseModule,
            IReadOnlyDictionary<string, BuiltinDelegate> overrides, EvalContext ctx)
        {
            var members = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, ScriptValue> m in baseModule.Members)
                members[m.Key] = m.Value;
            foreach (KeyValuePair<string, BuiltinDelegate> o in overrides)
            {
                if (o.Value == null)
                    members.Remove(o.Key);   // null delegate = remove the member
                else
                    members[o.Key] = BuiltinFunctionValue.MakeHost(name + "." + o.Key, o.Value);
            }
            return ctx.Values.Module(name, members);
        }

        private static CompiledScript CompilePreludeModule(string name, string source)
        {
            if (source == null)
                throw new ArgumentException("EngineOptions.PythonModules['" + name + "'] source is null");
            try
            {
                return Compiler.Compile(source, name, SyntaxLimits.Default);
            }
            catch (PySyntaxErrorException ex)
            {
                throw new ArgumentException("Python module '" + name + "' has a " + ex.PythonType +
                    " at line " + ex.Line + ", column " + ex.Col + ": " + ex.Message);
            }
        }

        // A Python-source module runs its body under the CURRENT run's budget into a fresh per-run globals
        // dict; every top-level name becomes a module attribute (prelude injection later hides '_'-names).
        private static ModuleValue LoadPythonModule(string name, CompiledScript compiled, EvalContext ctx)
        {
            DictValue mglobals = ctx.Values.Dict(8);
            Outbridge.PyLite.Runtime.Environment menv = Outbridge.PyLite.Runtime.Environment.CreateModule(
                ctx, mglobals, ctx.Builtins, compiled.Program.ModuleInfo);
            new Outbridge.PyLite.Runtime.Evaluator.Evaluator(compiled.Program).RunModule(menv, ctx);

            var members = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            OrderedTable table = mglobals.Table;
            int used = table.EntriesUsed;
            for (int pos = 0; pos < used; pos++)
            {
                long h;
                ScriptValue k, v;
                if (!table.TryGetEntryAt(pos, out h, out k, out v))
                    continue;
                StrValue sk = k as StrValue;
                if (sk != null)
                    members[sk.Value] = v;
            }
            return ctx.Values.Module(name, members);
        }

        // --- compilation + cache ----

        // Cache-aware compile (get-or-compile semantics): returns a cached CompiledScript on a source hash hit,
        // otherwise parses+resolves and inserts. Throws PyLiteSyntaxException on a syntax error.
        public CompiledScript Compile(string source, string scriptName = "<script>")
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            string key = _cache != null ? Sha256Hex(source) : null;
            if (key != null)
            {
                lock (_cacheLock)
                {
                    CompiledScript hit;
                    if (_cache.TryGetValue(key, out hit))
                        return hit;
                }
            }

            CompiledScript compiled;
            try
            {
                compiled = Compiler.Compile(source, scriptName ?? "<script>", SyntaxLimits.Default);
            }
            catch (PySyntaxErrorException ex)
            {
                throw new PyLiteSyntaxException(new ScriptError(
                    ScriptErrorKind.SyntaxError, ex.PythonType, ex.Message, ex.Line, ex.Col, null, null));
            }
            catch (Exception)
            {
                // A compiler defect must never leak a raw CLR exception (or its internals) to the host
                // thread: the host boundary contract is "one typed exception from Compile, a RunResult
                // from Run". PySyntaxErrorException stays the syntax channel; anything else is a fault.
                throw new PyLiteSyntaxException(new ScriptError(
                    ScriptErrorKind.EngineFault, null, "internal engine error", 0, 0, null, null));
            }

            if (key != null)
            {
                lock (_cacheLock)
                {
                    if (_cache.Count >= _cacheCap)
                        _cache.Clear();   // full clear on overflow (deterministic, like CPython _MAXCACHE)
                    _cache[key] = compiled;
                }
            }
            return compiled;
        }

        // The non-throwing variant of Compile: a syntax error is returned via `error`, not thrown.
        public bool TryCompile(string source, string scriptName, out CompiledScript compiled, out ScriptError error)
        {
            try
            {
                compiled = Compile(source, scriptName);
                error = null;
                return true;
            }
            catch (PyLiteSyntaxException ex)
            {
                compiled = null;
                error = ex.Error;
                return false;
            }
        }

        // Explicit get-or-compile (Compile already caches; this name states the intent at the call site).
        public CompiledScript GetOrCompile(string source, string scriptName = "<script>")
        {
            return Compile(source, scriptName);
        }

        // ---- run ----

        public RunResult Run(string source, RunRequest request)
        {
            CompiledScript compiled;
            try
            {
                compiled = Compile(source, "<script>");
            }
            catch (PyLiteSyntaxException ex)
            {
                return RunResult.Failure(ex.Error, "", new RunStatistics());
            }
            return Run(compiled, request);
        }

        public RunResult Run(CompiledScript compiled, RunRequest request)
        {
            if (compiled == null)
                throw new ArgumentNullException(nameof(compiled));
            request = request ?? new RunRequest();

            string nameError;
            if (!RunIO.ValidateNames(request, out nameError))
                return RunResult.Failure(new ScriptError(ScriptErrorKind.HostError, null, nameError, 0, 0, null, null),
                    null, new RunStatistics());

            ResourceLimits limits = request.Limits != null ? request.Limits.ClampTo(_defaultLimits) : _defaultLimits;

            ScriptError refusal;
            RunTicket ticket = _gate.TryAcquire(limits, out refusal);
            if (ticket == null)
                return RunResult.Failure(refusal, null, new RunStatistics());   // saturation / ceiling / breaker

            long runId = Interlocked.Increment(ref _runIdCounter);
            var ctxHolder = new EvalContext[1];
            RunResult Work() => ExecuteRun(compiled, request, limits, ctxHolder);
            var thread = new RunnerThread(Work, () => ctxHolder[0], _gate, ticket, _hygiene);
            thread.Start();

            if (thread.WaitCompleted(SafeJoinMs(limits)))
            {
                RunResult r = thread.TakeResult();
                RaiseEvent(new EngineEvent(EngineEventKind.RunCompleted, null, runId, r.Stats));
                if (thread.EngineFault != null)
                    RaiseEvent(new EngineEvent(EngineEventKind.EngineFault, thread.EngineFault.ToString(), runId, null));
                return r;
            }

            // Outlived deadline+grace+slack: the thread is leaked; its slot/memory stay held until it exits.
            _gate.RegisterLeak(thread);
            RaiseEvent(new EngineEvent(EngineEventKind.ThreadLeaked,
                "run " + runId + " leaked its runner thread", runId, null));
            return RunResult.Failure(new ScriptError(ScriptErrorKind.HostError, null,
                "run did not finish within deadline+grace; its runner thread is leaked (a host function is likely blocking)",
                0, 0, null, null), null, new RunStatistics());
        }

        private static int SafeJoinMs(ResourceLimits limits)
        {
            long ms = (long)limits.DeadlineMs + limits.TerminatingGraceMs + LeakJoinSlackMs;
            return ms > int.MaxValue ? int.MaxValue : (int)ms;
        }

        // The full per-run pipeline, executed ON the runner thread. Always returns a RunResult (never throws),
        // so the RunnerThread's own catch is only a safety net and the gate ticket releases in its finally.
        private RunResult ExecuteRun(CompiledScript compiled, RunRequest request, ResourceLimits limits, EvalContext[] ctxHolder)
        {
            var stopwatch = Stopwatch.StartNew();
            var stats = new RunStatistics();
            var budget = new Budget(limits, stopwatch, request.CancellationToken);
            uint seed = _strHashSeed;
            HostFunctionTable builtins = FreezeBuiltins(request.Builtins);

            var ctx = new EvalContext(budget, limits, seed, _modules, builtins,
                _clock, _sleepPolicy, stats);
            ctx.Format = PyFormatServices.Instance;
            ctxHolder[0] = ctx;
            var sink = new OutputSink(request.PrintSink, ctx);
            ctx.Out = sink;

            try
            {
                DictValue globals = ctx.Values.Dict(16);
                globals.SetItem(ctx.Values.Str("__name__"), ctx.Values.Str("__main__"), ctx);   // the script is the main module
                Outbridge.PyLite.Runtime.Environment moduleEnv = Outbridge.PyLite.Runtime.Environment.CreateModule(
                    ctx, globals, builtins, compiled.Program.ModuleInfo);

                InjectPreludes(moduleEnv, ctx);
                RunIO.PopulateScalarInputs(moduleEnv, request, ctx);
                RunIO.PopulateJsonInputs(moduleEnv, request, ctx);

                new Outbridge.PyLite.Runtime.Evaluator.Evaluator(compiled.Program).RunModule(moduleEnv, ctx);
                sink.FlushFinal();   // finalize the last (newline-less) print line; may throw HostCallFailure

                IReadOnlyDictionary<string, string> scalarsOut =
                    RunIO.CollectScalarOutputs(globals, request.ScalarOutputNames, ctx);
                IReadOnlyDictionary<string, string> jsonOut =
                    RunIO.CollectJsonOutputs(globals, request.JsonOutputNames, ctx);
                FillStats(stats, budget, ctx, stopwatch);
                return RunResult.Success(scalarsOut, jsonOut, sink.GetBufferedOrNull(), stats);
            }
            catch (HostMarshalFailure hmf)
            {
                TryFlushQuietly(sink);   // idempotent: a no-op after a successful FlushFinal
                FillStats(stats, budget, ctx, stopwatch);
                return RunResult.Failure(new ScriptError(ScriptErrorKind.HostError, null, hmf.Message, 0, 0, null, null),
                    sink.GetBufferedOrNull(), stats);
            }
            catch (HostCallFailure hcf)
            {
                FillStats(stats, budget, ctx, stopwatch);
                string msg = hcf.FunctionName == "print"
                    ? "host print callback failed: " + InnerText(hcf)
                    : "host function '" + hcf.FunctionName + "' failed: " + InnerText(hcf);
                return RunResult.Failure(new ScriptError(ScriptErrorKind.HostError, null, msg, 0, 0, null, null),
                    sink.GetBufferedOrNull(), stats);
            }
            catch (Exception ex)
            {
                TryFlushQuietly(sink);   // preserve print output on a script/budget error
                ScriptError err = ScriptErrorBuilder.Build(ex, ctx);
                FillStats(stats, budget, ctx, stopwatch);
                return RunResult.Failure(err, sink.GetBufferedOrNull(), stats);
            }
        }

        private static void TryFlushQuietly(OutputSink sink)
        {
            try
            {
                sink.FlushFinal();
            }
            catch (Exception)
            {
                // A callback/budget failure during the final flush of an already-failed run is swallowed.
            }
        }

        private static string InnerText(HostCallFailure hcf)
        {
            Exception inner = hcf.InnerException;
            return inner == null ? "unknown error" : inner.GetType().Name + ": " + inner.Message;
        }

        // ---- prelude injection ----

        private void InjectPreludes(Outbridge.PyLite.Runtime.Environment moduleEnv, EvalContext ctx)
        {
            for (int i = 0; i < _prelude.Count; i++)
            {
                ModuleValue module = ctx.Modules.Import(_prelude[i], ctx);
                foreach (KeyValuePair<string, ScriptValue> member in module.Members)
                {
                    if (member.Key.Length > 0 && member.Key[0] == '_')
                        continue;   // implicit `import *` hides private names
                    moduleEnv.SetGlobal(member.Key, member.Value);
                }
            }
        }

        // ---- helpers ----

        private static HostFunctionTable FreezeBuiltins(HostFunctionTable requested)
        {
            HostFunctionTable table = requested != null ? requested.Clone() : HostFunctionTable.CreateDefault();
            table.Freeze();
            return table;
        }

        private static void FillStats(RunStatistics stats, Budget budget, EvalContext ctx, Stopwatch stopwatch)
        {
            stats.Steps = budget.StepsUsed;
            stats.AllocatedBytes = budget.AllocBytesUsed;
            stats.OutputBytes = budget.OutputBytesUsed;
            stats.SleepMs = budget.SleepMsUsed;
            stats.Elapsed = stopwatch.Elapsed;
            stats.ModulesImported = ctx.ModuleInstances.Count;
            stats.TerminatingEntered = budget.IsTerminating;
        }

        private static uint RandomSeed()
        {
            var buf = new byte[4];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(buf);
            return BitConverter.ToUInt32(buf, 0);
        }

        private static string Sha256Hex(string source)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
