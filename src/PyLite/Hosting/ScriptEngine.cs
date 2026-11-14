using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;

namespace Outbridge.PyLite.Hosting
{
    /// <summary>
    /// The entry point: compiles and runs scripts. Thread-safe and immutable once built; it holds the modules and
    /// the compiled-script cache. Every run executes on a thread of its own with a 32 MB stack; a run is refused
    /// before it starts by the left-behind-thread breaker, when free memory is below what it may need, or when its
    /// thread cannot start (a HostError naming the refusal). A source not in the cache compiles off the calling thread:
    /// <c>Run</c> on the run's thread, <c>Compile</c>, <c>TryCompile</c> and <c>PythonModules</c> on a 32 MB thread of their own. A run never
    /// throws to the host: whatever happens comes back in its <see cref="RunResult"/>.
    /// </summary>
    public sealed class ScriptEngine
    {
        private const int LeakJoinSlackMs = 5000;   // extra Join wait past deadline+grace before declaring a leak
        private const int MaxStarvedWaitMs = 120000;   // however starved: past this a run counts as leaked
        private const int StarvationMsPerRun = 500;    // per run sharing a core: about 3 Windows Server time slices
        private static readonly ScriptError NullSource = new ScriptError(ScriptErrorKind.HostError, null, "the script is null", 0, 0, null, null);
        private static readonly string[] EmptyStrings = new string[0];
        private static readonly ScriptError CompilerFault = new ScriptError(ScriptErrorKind.EngineFault, null, "internal engine error", 0, 0, null, null);
        private static readonly ScriptError CompileThreadRefusal = new ScriptError(ScriptErrorKind.HostError, null,
            "engine unavailable: the compile thread could not start", 0, 0, "ThreadStart", null);
        private static readonly ScriptError CompileFreeMemoryRefusal = new ScriptError(ScriptErrorKind.HostError, null,
            "engine unavailable: not enough free memory to compile", 0, 0, "FreeMemory", null);
        private static readonly ScriptError RunFreeMemoryRefusal = new ScriptError(ScriptErrorKind.HostError, null,
            "engine unavailable: not enough free memory for a run", 0, 0, "FreeMemory", null);
        private static readonly ScriptError RunThreadRefusal = new ScriptError(ScriptErrorKind.HostError, null,
            "engine unavailable: the run's thread could not start", 0, 0, "ThreadStart", null);
        private const int CompileStackBytes = RunnerThread.StackSizeBytes;   // as a run's: Compile and Run parse alike

        private readonly ResourceLimits _defaultLimits;
        private readonly IScriptClock _clock;
        private readonly ISleepPolicy _sleepPolicy;
        private readonly uint _strHashSeed;   // fixed at build (EngineOptions contract: per ENGINE, not per run)
        private readonly bool _interpretOnly;
        private readonly ModuleRegistry _modules;         // shared, frozen
        private readonly IReadOnlyList<string> _prelude;

        private readonly LeakBreaker _breaker;
        private readonly int _breakerThreshold;
        private readonly long _freeCommitReserveBytes;
        private readonly Action<EngineEvent> _eventSink;
        private long _runIdCounter;
        private static long _unstartedStackBytes;   // RunnerThread stacks admitted by any engine and not yet started
        internal static long UnstartedStackBytes { get { return Interlocked.Read(ref _unstartedStackBytes); } }

        private readonly int _cacheCap;
        // The cache also holds at most this much source: an entry cap alone let 256 large scripts pin gigabytes.
        // A script weighs its characters, or 8/3 of its tokens when that is more: a program keeps up to ~200 bytes
        // a token once its expressions are compiled, so the cache stays near 150 MB whatever the source is made
        // of. By characters alone four scripts of one-character tokens held 330 MB (review 12, row 16).
        private const long CacheMaxSourceChars = 2000000;
        private long _cacheChars;

        internal static long CacheWeight(long sourceChars, long tokens)
        {
            return Math.Max(sourceChars, tokens * 8 / 3);
        }

        private readonly object _cacheLock = new object();

        internal long CachedSourceChars
        {
            get
            {
                lock (_cacheLock)
                    return _cacheChars;
            }
        }
        // The source text itself (ordinal: a lossy UTF-8 hash mapped lone surrogates and U+FFFD to one entry,
        // so run B executed run A's program) -> compiled; a hit must also carry the requested script name.
        private readonly Dictionary<string, CompiledScript> _cache;   // null if disabled
        private readonly MemoryHygieneOptions _hygiene;
        private readonly HostFunctionTable _defaultBuiltins;   // frozen: shared by every run that brings no table of its own

        /// <summary>Builds an engine from a copy of <paramref name="options"/>.</summary>
        /// <exception cref="ArgumentException">A Python module does not compile, or an override names an unknown module.</exception>
        public ScriptEngine(EngineOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            ResourceLimits ceiling = options.DefaultLimits ?? ResourceLimits.Default;
            _defaultLimits = ceiling.ClampTo(ceiling);   // the ceiling itself obeys the engine hard caps
            _clock = new FencedClock(options.Clock ?? new SystemClock(TimeSpan.Zero, "UTC"));
            _sleepPolicy = new FencedSleepPolicy(options.SleepPolicy ?? new DefaultSleepPolicy());
            _strHashSeed = options.StrHashSeed ?? RandomSeed();
            _interpretOnly = options.InterpretOnly;
            // copies, as documented: the host's own list and override tables were read by every later run
            _prelude = options.PreludeModules == null ? EmptyStrings : new List<string>(options.PreludeModules).ToArray();
            _cacheCap = options.CompiledScriptCacheEntries;
            _cache = _cacheCap > 0 ? new Dictionary<string, CompiledScript>(StringComparer.Ordinal) : null;
            _eventSink = options.EventSink;
            _breakerThreshold = options.CircuitBreakerLeakedThreads;
            _freeCommitReserveBytes = options.FreeCommitReserveBytes;
            MemoryHygieneOptions hygiene = options.MemoryHygiene;   // null => off; a copy, like every other option
            _hygiene = hygiene == null ? null : new MemoryHygieneOptions
            {
                PostRunGcHintThresholdBytes = hygiene.PostRunGcHintThresholdBytes,
                LohCompactionEveryNHints = hygiene.LohCompactionEveryNHints,
            };

            _breaker = new LeakBreaker(_breakerThreshold);
            _breaker.CircuitStateChanged += OnCircuitStateChanged;

            _modules = BuildRegistry(options, _prelude);
            // Built here, not in the first runs: the template build (100-170 ms cold) ran inside their deadlines.
            _defaultBuiltins = HostFunctionTable.CreateDefault();
            _defaultBuiltins.Freeze();
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
                    registry.Replace(provider.Name, ctx => HostCallGuard.CreateModule(captured, ctx));
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
                var overrides = new Dictionary<string, BuiltinDelegate>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, BuiltinDelegate> o in entry.Value)
                    overrides[o.Key] = o.Value;
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

        // A module's frames name its file in a traceback (the module name, dots as folders, plus .py).
        internal static CompiledScript AsModule(CompiledScript script, string moduleName)
        {
            script.Program.ModuleFile = moduleName.Replace('.', '/') + ".py";
            return script;
        }

        private static CompiledScript CompilePreludeModule(string name, string source)
        {
            if (source == null)
                throw new ArgumentException("EngineOptions.PythonModules['" + name + "'] source is null");
            // On a thread of its own, as every compile: an engine built deep in the host's stack got a false SyntaxError
            // for a module, or ended the process.
            CompiledScript compiled = null;
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    compiled = AsModule(Compiler.Compile(source, name, SyntaxLimits.Default), name);
                }
                catch (Exception ex)
                {
                    error = ex;   // nothing may escape a thread of ours
                }
            }, CompileStackBytes);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();
            PySyntaxErrorException syntax = error as PySyntaxErrorException;
            if (syntax != null)
                throw new ArgumentException("Python module '" + name + "' has a " + syntax.PythonType +
                    " at line " + syntax.Line + ", column " + syntax.Col + ": " + syntax.Message);
            if (error != null)
                throw new InvalidOperationException("Python module '" + name + "' could not be compiled", error);
            return compiled;
        }

        // A Python-source module runs its body under the CURRENT run's budget into a fresh per-run globals
        // dict; every top-level name becomes a module attribute (prelude injection later hides '_'-names).
        internal static ModuleValue LoadPythonModule(string name, CompiledScript compiled, EvalContext ctx)
        {
            DictValue mglobals = ctx.Values.Dict(8);
            Outbridge.PyLite.Runtime.Environment menv = Outbridge.PyLite.Runtime.Environment.CreateModule(
                ctx, mglobals, ctx.Builtins, compiled.Program.ModuleInfo);
            Outbridge.PyLite.Runtime.Evaluator.Evaluator.For(compiled.Program, ctx).RunModule(menv, ctx);

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
                if (sk != null && !(sk.Value.Length > 0 && sk.Value[0] == '$'))   // `with` lowering's hidden names
                    members[sk.Value] = v;
            }
            return ctx.Values.Module(name, members);
        }

        // --- compilation + cache ----

        /// <summary>
        /// Compiles a script, or answers the cached compile of the same source. A compiled script can be run any
        /// number of times, from any thread.
        /// </summary>
        /// <param name="source">The script.</param>
        /// <param name="scriptName">A name kept on the compiled script and part of the cache key; tracebacks always show <c>&lt;script&gt;</c>.</param>
        /// <exception cref="PyLiteSyntaxException">The script does not compile.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
        public CompiledScript Compile(string source, string scriptName = "<script>")
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            ScriptError error;
            CompiledScript compiled = CompileOffCaller(source, scriptName ?? "<script>", out error);
            if (compiled == null)
                throw new PyLiteSyntaxException(error);
            return compiled;
        }

        /// <summary><see cref="Compile(string, string)"/> without the exception: a syntax error comes back in <paramref name="error"/>.</summary>
        public bool TryCompile(string source, string scriptName, out CompiledScript compiled, out ScriptError error)
        {
            if (source == null)
            {
                compiled = null;
                error = NullSource;
                return false;
            }
            compiled = CompileOffCaller(source, scriptName ?? "<script>", out error);
            return compiled != null;
        }

        private CompiledScript CachedCompile(string source, string name)
        {
            if (_cache == null)
                return null;
            lock (_cacheLock)
            {
                CompiledScript hit;
                return _cache.TryGetValue(source, out hit) && string.Equals(hit.ScriptName, name, StringComparison.Ordinal)
                    ? hit : null;
            }
        }

        // A cache miss compiles on a thread of its own: the parser's stack probe refuses past half of the caller's
        // reserve (.NET Framework), and that refusal, thrown on an almost full host stack, ended the process.
        private CompiledScript CompileOffCaller(string source, string name, out ScriptError error)
        {
            CompiledScript hit = CachedCompile(source, name);
            if (hit != null)
            {
                error = null;
                return hit;
            }
            CompiledScript result = null;
            ScriptError failure = null;
            Thread thread = null;
            ScriptError refusal = AdmitThread(CompileBytes(null, source), CompileFreeMemoryRefusal, () =>
            {
                try
                {
                    // The compiler reads no culture, so the thread sets none.
                    thread = new Thread(() => result = CompileAndCache(source, name, out failure), CompileStackBytes);
                    thread.IsBackground = true;
                    thread.Start();
                    return null;
                }
                catch (Exception ex) when (ex is OutOfMemoryException || ex is ThreadStartException)
                {
                    return CompileThreadRefusal;
                }
            });
            if (refusal != null)
            {
                error = refusal;
                return null;
            }
            thread.Join();
            error = failure;
            return result;
        }

        // A 32 MB thread of ours (a run's or a compile's) starts only when Windows can still promise its stack, needBytes and
        // the reserve. A stack admitted and not started yet is not in the free figure, so it is counted, per process, until
        // start() returns: callers arriving together all passed a bare check. null when start() started the thread; the
        // refusal comes back after the release.
        private ScriptError AdmitThread(long needBytes, ScriptError freeMemoryRefusal, Func<ScriptError> start)
        {
            bool counted = false;
            try
            {
                long unstarted;
                try
                {
                }
                finally
                {
                    // in a finally, so a Thread.Abort cannot land between the add and the flag that releases it
                    unstarted = Interlocked.Add(ref _unstartedStackBytes, RunnerThread.StackSizeBytes);
                    counted = true;
                }
                if (FreeCommit.IsBelow(unstarted + needBytes + _freeCommitReserveBytes))
                    return freeMemoryRefusal;
                return start();
            }
            finally
            {
                if (counted)
                    Interlocked.Add(ref _unstartedStackBytes, -RunnerThread.StackSizeBytes);
            }
        }

        // Never throws, on whatever thread: a syntax error or a compiler fault comes back in error. A compiler defect
        // must not reach the host as a raw CLR exception (or its internals), and an exception escaping a thread of
        // ours ends the process.
        private CompiledScript CompileAndCache(string source, string name, out ScriptError error)
        {
            try
            {
                CompiledScript compiled;
                try
                {
                    compiled = Compiler.Compile(source, name, SyntaxLimits.Default);
                }
                catch (PySyntaxErrorException ex)
                {
                    error = new ScriptError(ScriptErrorKind.SyntaxError, ex.PythonType, ex.Message, ex.Line, ex.Col, null, null);
                    return null;
                }

                long weight = CacheWeight(source.Length, compiled.TokenCount);
                if (_cache != null && weight <= CacheMaxSourceChars)
                {
                    lock (_cacheLock)
                    {
                        bool present = _cache.ContainsKey(source);   // the same text under another script name
                        if (!present && (_cache.Count >= _cacheCap || _cacheChars + weight > CacheMaxSourceChars))
                        {
                            _cache.Clear();   // full clear on overflow (deterministic, like CPython _MAXCACHE)
                            _cacheChars = 0;
                        }
                        if (!present)
                            _cacheChars += weight;
                        _cache[source] = compiled;
                    }
                }
                error = null;
                return compiled;
            }
            catch (Exception ex) when (ScriptErrorBuilder.IsShortage(ex))
            {
                error = ScriptErrorBuilder.OutOfMemory;   // a shortage, not a compiler defect
                return null;
            }
            catch (Exception)
            {
                error = CompilerFault;
                return null;
            }
        }

        /// <summary><see cref="Compile(string, string)"/> under a name that says it may answer from the cache.</summary>
        public CompiledScript GetOrCompile(string source, string scriptName = "<script>")
        {
            return Compile(source, scriptName);
        }

        // ---- run ----

        /// <summary>
        /// Compiles (or takes from the cache) and runs a script. Never throws: a syntax error, a limit, an exception
        /// the script did not handle and a failing host function all come back in the result.
        /// </summary>
        /// <param name="source">The script.</param>
        /// <param name="request">The run's inputs, outputs and limits; null runs with none.</param>
        public RunResult Run(string source, RunRequest request)
        {
            if (source == null)
                return NotStarted(NullSource, request, new RunStatistics());   // Run never throws, a null either
            return Run(CachedCompile(source, "<script>"), source, request);   // a miss compiles on the run's thread
        }

        /// <summary>Runs a compiled script; never throws, as <see cref="Run(string, RunRequest)"/>.</summary>
        /// <param name="compiled">A script this engine compiled.</param>
        /// <param name="request">The run's inputs, outputs and limits; null runs with none.</param>
        public RunResult Run(CompiledScript compiled, RunRequest request)
        {
            return Run(compiled, null, request);
        }

        // A run that compiles holds its syntax tree outside MaxAllocBytes: measured up to 86 bytes per source character
        // kept and 256 allocated. A source past the syntax limit is refused by the lexer at once, so it costs no more.
        private const long CompileBytesPerChar = 128;

        internal static long CompileBytes(CompiledScript compiled, string source)
        {
            return compiled != null ? 0 : Math.Min(source.Length, SyntaxLimits.Default.MaxSourceChars) * CompileBytesPerChar;
        }

        // With compiled null and source set, the runner compiles source first.
        private RunResult Run(CompiledScript compiled, string source, RunRequest request)
        {
            if (compiled == null && source == null)
                return NotStarted(NullSource, request, new RunStatistics());
            if (request == null)
                request = new RunRequest();
            else
            {
                // the run reads this copy: a host changing its dictionaries or lists meanwhile was seen inside it
                try
                {
                    request = request.Snapshot();
                }
                catch (InvalidOperationException)
                {
                    return NotStarted(new ScriptError(ScriptErrorKind.HostError, null,
                        "the RunRequest was modified while the run was starting", 0, 0, null, null), request, new RunStatistics());
                }
                catch (ArgumentNullException)   // a null input name, which the name check below reports so
                {
                    return NotStarted(new ScriptError(ScriptErrorKind.HostError, null,
                        "invalid variable name ''", 0, 0, null, null), request, new RunStatistics());
                }
                catch (Exception ex)   // a host collection whose enumerator throws: Run never throws
                {
                    return NotStarted(new ScriptError(ScriptErrorKind.HostError, null,
                        "the RunRequest could not be read: " + HostText.Of(ex), 0, 0, null, null), request, new RunStatistics());
                }
            }

            string nameError;
            if (!RunIO.ValidateNames(request, out nameError))
                return NotStarted(new ScriptError(ScriptErrorKind.HostError, null, nameError, 0, 0, null, null),
                    request, new RunStatistics());

            ResourceLimits limits = request.Limits != null ? request.Limits.ClampTo(_defaultLimits) : _defaultLimits;

            int leakedAtStart = _breaker.LeakedThreads;
            if (_breaker.IsCircuitOpen)
                return NotStarted(LeakBreaker.Refusal(), request, new RunStatistics { LeakedThreadsAtStart = leakedAtStart });
            if (request.CancellationToken.IsCancellationRequested)   // nothing of it runs, on an idle engine or a busy one
                return NotStarted(new ScriptError(ScriptErrorKind.BudgetExceeded, null, "run cancelled before it started", 0, 0, "CancellationToken", null),
                    request, new RunStatistics { LeakedThreadsAtStart = leakedAtStart });

            var ctxHolder = new EvalContext[1];
            var faultHolder = new Exception[1];   // an engine fault the run caught itself, for the event
            RunResult Work()
            {
                if (compiled != null)
                    return ExecuteRun(compiled, request, limits, ctxHolder, faultHolder, TimeSpan.Zero, leakedAtStart);
                // Before the run's clock starts: compiling is no part of the deadline.
                var watch = Stopwatch.StartNew();
                ScriptError error;
                CompiledScript script = CompileAndCache(source, "<script>", out error);
                if (script == null)
                    return RunResult.Failure(error, request.PrintSink != null ? null : "",
                        new RunStatistics { CompileTime = watch.Elapsed, LeakedThreadsAtStart = leakedAtStart });
                return ExecuteRun(script, request, limits, ctxHolder, faultHolder, watch.Elapsed, leakedAtStart);
            }

            long runId = 0;
            RunnerThread thread = null;
            ScriptError refusal = AdmitThread(limits.MaxAllocBytes + CompileBytes(compiled, source), RunFreeMemoryRefusal, () =>
            {
                runId = Interlocked.Increment(ref _runIdCounter);
                try
                {
                    thread = new RunnerThread(Work, () => ctxHolder[0], _breaker, _hygiene);   // allocates too
                    thread.Start();   // .NET Framework commits the whole stack before Start returns
                    return null;
                }
                catch (Exception ex) when (ex is OutOfMemoryException || ex is ThreadStartException)
                {
                    return RunThreadRefusal;
                }
            });
            if (refusal != null)   // after the release: the event sink must not hold a stack nobody uses
                return NotStarted(refusal, request, new RunStatistics { LeakedThreadsAtStart = leakedAtStart });

            bool finished = WaitForRun(thread, ctxHolder, limits);
            if (!finished && !_breaker.RegisterLeak(thread))
                finished = thread.WaitCompleted(LeakJoinSlackMs);   // it finished as the wait gave up: not leaked
            if (finished)
            {
                RunResult r = thread.TakeResult();
                RaiseEvent(new EngineEvent(EngineEventKind.RunCompleted, null, runId, r.Stats));
                Exception fault = thread.EngineFault ?? Volatile.Read(ref faultHolder[0]);
                if (fault != null)
                    RaiseEvent(new EngineEvent(EngineEventKind.EngineFault, HostText.Full(fault), runId, null));
                return r;
            }

            // Outlived deadline+grace+slack: the thread is leaked, counted by the breaker until it exits.
            RaiseEvent(new EngineEvent(EngineEventKind.ThreadLeaked,
                "run " + runId + " leaked its runner thread", runId, null));
            RunResult leaked = RunResult.Failure(new ScriptError(ScriptErrorKind.HostError, null,
                "run did not finish within deadline+grace; its runner thread is leaked (a host function is likely blocking)",
                0, 0, null, null), null, new RunStatistics { LeakedThreadsAtStart = leakedAtStart });
            RaiseEvent(new EngineEvent(EngineEventKind.RunCompleted, null, runId, leaked.Stats));
            return leaked;
        }

        // False when the run is hung: past its own deadline + grace, inside a host call, and without progress for a whole
        // window. A run in the engine's own code is not, however long it waits for CPU: that code always ends (runs that hit
        // their deadlines together on 4 cores showed no progress for over 16 s), and a "leak" would lose its result.
        internal static bool WaitForRun(RunnerThread thread, EvalContext[] ctxHolder, ResourceLimits limits)
        {
            return WaitForRun(thread, ctxHolder, limits, MaxStarvedWaitMs, LeakJoinSlackMs);
        }

        // giveUpMs and minWindowMs: the tests' handles on the 2-minute give-up and the 5 s window.
        internal static bool WaitForRun(RunnerThread thread, EvalContext[] ctxHolder, ResourceLimits limits, int giveUpMs, int minWindowMs)
        {
            int terminationMs = TerminationMs(limits);
            if (thread.WaitCompleted(terminationMs))
                return true;
            long giveUpAt = Stopwatch.GetTimestamp() + giveUpMs * Stopwatch.Frequency / 1000;
            while (true)
            {
                EvalContext ctx = Volatile.Read(ref ctxHolder[0]);
                long mark = ctx == null ? -1 : ctx.Budget.ProgressMark;
                // a window may be up to 2 minutes itself: the give-up caps it, so the wait ends on time
                long leftMs = (giveUpAt - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency;
                int window = StarvationWindowMs(RunnerThread.Runnable, AvailableCores(), minWindowMs);
                if (thread.WaitCompleted((int)Math.Max(1, Math.Min(window, leftMs))))
                    return true;
                if (Stopwatch.GetTimestamp() > giveUpAt)
                    return false;
                ctx = Volatile.Read(ref ctxHolder[0]);
                bool clockYoung = ctx == null || ctx.Budget.Elapsed.TotalMilliseconds < terminationMs;
                if (!clockYoung && ctx.Budget.ProgressMark == mark && thread.InHostCode)
                    return false;
            }
        }

        // How long a run may show no progress before it counts as hung. A ready thread waits about one time slice per
        // runnable thread on its core; from about 22 runs per core that passed 5 s, and healthy runs were leaked.
        internal static int StarvationWindowMs(int runnableRuns, int cores)
        {
            return StarvationWindowMs(runnableRuns, cores, LeakJoinSlackMs);
        }

        private static int StarvationWindowMs(int runnableRuns, int cores, int minWindowMs)
        {
            long perCore = ((long)runnableRuns + cores - 1) / cores;
            return (int)Math.Min(MaxStarvedWaitMs, Math.Max(minWindowMs, perCore * StarvationMsPerRun));
        }

        // The cores this process may run on (its affinity), not the machine's.
        private static int AvailableCores()
        {
            try
            {
                using (Process p = Process.GetCurrentProcess())
                {
                    long mask = p.ProcessorAffinity.ToInt64();
                    int n = 0;
                    for (; mask != 0; mask &= mask - 1)
                        n++;
                    return n > 0 ? n : System.Environment.ProcessorCount;
                }
            }
            catch (Exception)
            {
                return System.Environment.ProcessorCount;
            }
        }

        // A run that ends before it starts is numbered and reported like any other; it printed nothing.
        private RunResult NotStarted(ScriptError error, RunRequest request, RunStatistics stats)
        {
            RunResult r = RunResult.Failure(error, request != null && request.PrintSink != null ? null : "", stats);
            RaiseEvent(new EngineEvent(EngineEventKind.RunCompleted, null, Interlocked.Increment(ref _runIdCounter), stats));
            return r;
        }

        private static int TerminationMs(ResourceLimits limits)
        {
            long ms = (long)limits.DeadlineMs + limits.TerminatingGraceMs;
            return ms > int.MaxValue ? int.MaxValue : (int)ms;
        }


        // The full per-run pipeline, executed ON the runner thread. Always returns a RunResult (never throws),
        // so the RunnerThread's own catch is only a safety net.
        private RunResult ExecuteRun(CompiledScript compiled, RunRequest request, ResourceLimits limits, EvalContext[] ctxHolder,
            Exception[] faultHolder, TimeSpan compileTime, int leakedAtStart)
        {
            var stopwatch = Stopwatch.StartNew();
            var stats = new RunStatistics { CompileTime = compileTime, LeakedThreadsAtStart = leakedAtStart };
            var budget = new Budget(limits, stopwatch, request.CancellationToken);
            uint seed = _strHashSeed;
            HostFunctionTable builtins = request.Builtins ?? _defaultBuiltins;   // the request's is a frozen copy (Snapshot)

            var ctx = new EvalContext(budget, limits, seed, _modules, builtins,
                _clock, _sleepPolicy, stats);
            ctx.Format = PyFormatServices.Instance;
            ctx.InterpretOnly = _interpretOnly;
            ctxHolder[0] = ctx;
            var sink = new OutputSink(request.PrintSink, ctx);
            ctx.Out = sink;

            try
            {
                DictValue globals = ctx.Values.Dict(16);
                globals.SetItem(ctx.Values.Str("__name__"), ctx.Values.Str("__main__"), ctx);   // the script is the main module
                Outbridge.PyLite.Runtime.Environment moduleEnv = Outbridge.PyLite.Runtime.Environment.CreateModule(
                    ctx, globals, builtins, compiled.Program.ModuleInfo);
                // Callbacks into script code bind to the main script's evaluator, preludes run after.
                var main = Outbridge.PyLite.Runtime.Evaluator.Evaluator.For(compiled.Program, ctx);
                main.InstallHooks(ctx);

                // Parsed first: invalid JSON ends the run before any prelude or provider code runs.
                IReadOnlyList<KeyValuePair<string, ScriptValue>> jsonIn = RunIO.ParseJsonInputs(request, ctx);
                InjectPreludes(moduleEnv, ctx);
                RunIO.PopulateScalarInputs(moduleEnv, request, ctx);
                for (int i = 0; i < jsonIn.Count; i++)
                    moduleEnv.SetGlobal(jsonIn[i].Key, jsonIn[i].Value);

                main.RunModule(moduleEnv, ctx);
                sink.FlushFinal();   // finalize the last (newline-less) print line; may throw HostCallFailure

                IReadOnlyDictionary<string, string> scalarsOut =
                    RunIO.CollectScalarOutputs(globals, request.ScalarOutputNames, ctx);
                IReadOnlyDictionary<string, string> jsonOut =
                    RunIO.CollectJsonOutputs(globals, request.JsonOutputNames, ctx);
                stats.Snapshot(ctx);
                return RunResult.Success(scalarsOut, jsonOut, sink.GetBufferedOrNull(), stats);
            }
            catch (HostMarshalFailure hmf)
            {
                TryFlushQuietly(sink);   // idempotent: a no-op after a successful FlushFinal
                stats.Snapshot(ctx);
                return RunResult.Failure(new ScriptError(ScriptErrorKind.HostError, null, hmf.Message, 0, 0, null, null),
                    sink.GetBufferedOrNull(), stats);
            }
            catch (HostCallFailure hcf)
            {
                stats.Snapshot(ctx);
                return RunResult.Failure(HostError(hcf), sink.GetBufferedOrNull(), stats);
            }
            catch (Exception ex)
            {
                TryFlushQuietly(sink);   // preserve print output on a script/budget error
                ScriptError err;
                try
                {
                    err = ScriptErrorBuilder.Build(ex, ctx);
                }
                catch (HostCallFailure hcf)   // the message's __str__ called the host: still the host's failure
                {
                    err = HostError(hcf);
                }
                catch (HostMarshalFailure hmf)
                {
                    err = new ScriptError(ScriptErrorKind.HostError, null, hmf.Message, 0, 0, null, null);
                }
                if (err.Kind == ScriptErrorKind.EngineFault || ScriptErrorBuilder.IsShortage(ex))   // a shortage is logged too
                    Volatile.Write(ref faultHolder[0], ex);   // the event fired only for a fault that escaped this catch
                stats.Snapshot(ctx);
                return RunResult.Failure(err, sink.GetBufferedOrNull(), stats);
            }
            finally
            {
                ctx.ReleaseRunCaches();   // the cached AST must not keep this run's namedtuple types alive
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

        private static ScriptError HostError(HostCallFailure hcf)
        {
            string inner = HostText.Of(hcf.InnerException);
            string msg = hcf.FunctionName == "print"
                ? "host print callback failed: " + inner
                : "host function '" + hcf.FunctionName + "' failed: " + inner;
            return new ScriptError(ScriptErrorKind.HostError, null, msg, 0, 0, null, null);
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

        private static uint RandomSeed()
        {
            var buf = new byte[4];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(buf);
            return BitConverter.ToUInt32(buf, 0);
        }
    }
}
