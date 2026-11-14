using System;
using System.Collections.Generic;
using System.Threading;

namespace Outbridge.PyLite.Hosting
{
    /// <summary>
    /// A flat facade for hosts that cannot use generics: no generic, task or enumerable type in its surface, no
    /// exception out of <see cref="Run"/>, results through getters. A runner holds one configuration (inputs,
    /// outputs, limits), kept across runs until <see cref="Reset"/>; every runner shares one engine.
    /// </summary>
    public sealed class ScriptRunner
    {
        // Built under a lock, not a Lazy: a Lazy caches the exception, so a host thread aborted (an ASP.NET
        // timeout) while the first runner built the engine made every later ScriptRunner throw until restart.
        private static readonly object SharedLock = new object();
        private static ScriptEngine _sharedEngine;

        private static ScriptEngine SharedEngine
        {
            get
            {
                ScriptEngine e = Volatile.Read(ref _sharedEngine);
                if (e != null)
                    return e;
                lock (SharedLock)
                {
                    if (_sharedEngine == null)
                        Volatile.Write(ref _sharedEngine, new ScriptEngine(new EngineOptions()));
                    return _sharedEngine;
                }
            }
        }

        /// <summary>
        /// Builds the engine every runner shares with these options. It is otherwise built with the defaults on
        /// first use, so a host that needs others (limits, concurrency, clock, sleep) calls this once, before that.
        /// </summary>
        /// <returns>False when the engine is already built (and keeps its options).</returns>
        /// <exception cref="ArgumentException">The options are invalid, as for the engine's constructor.</exception>
        public static bool ConfigureSharedEngine(EngineOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            lock (SharedLock)
            {
                if (_sharedEngine != null)
                    return false;
                Volatile.Write(ref _sharedEngine, new ScriptEngine(options));
                return true;
            }
        }

        private readonly ScriptEngine _engine;

        private readonly Dictionary<string, string> _scalarsIn = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _jsonIn = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _scalarOut = new List<string>();
        private readonly List<string> _jsonOut = new List<string>();
        private PrintDelegate _printSink;
        private int? _timeoutMs;
        private long? _memBytes;
        private int? _recursion;
        private int? _intBits;

        private RunResult _last;
        private ScriptError _lastError;
        private int _lastCode;

        /// <summary>A runner over the shared engine, with nothing configured.</summary>
        public ScriptRunner()
            : this(SharedEngine)
        {
        }

        internal ScriptRunner(ScriptEngine engine)
        {
            _engine = engine;
        }

        // ---- input ----

        /// <summary>
        /// An input variable from text (<see cref="RunRequest.ScalarsIn"/>). A null name is kept as '', which
        /// <see cref="Run"/> reports as an invalid variable name.
        /// </summary>
        public void SetScalar(string name, string value)
        {
            _scalarsIn[name ?? ""] = value;
        }

        /// <summary>An input variable parsed from a JSON document (<see cref="RunRequest.JsonIn"/>).</summary>
        public void SetJson(string name, string json)
        {
            _jsonIn[name ?? ""] = json;
        }

        /// <summary>A variable to read back as text with <see cref="GetScalar"/>.</summary>
        public void DeclareScalarOutput(string name)
        {
            _scalarOut.Add(name);
        }

        /// <summary>A variable to read back as JSON with <see cref="GetJson"/>.</summary>
        public void DeclareJsonOutput(string name)
        {
            _jsonOut.Add(name);
        }

        // ---- limits (downward only from the server defaults; an invalid value is a host coding error) ----

        /// <summary>The run's deadline in milliseconds; only lowers the engine's.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Not positive.</exception>
        public void SetLimitTimeoutMs(int ms)
        {
            if (ms <= 0)
                throw new ArgumentOutOfRangeException(nameof(ms), "timeout must be positive");
            _timeoutMs = ms;
        }

        /// <summary>The bytes the run may allocate; only lowers the engine's limit.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Not positive.</exception>
        public void SetLimitMemoryBytes(long bytes)
        {
            if (bytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(bytes), "memory limit must be positive");
            _memBytes = bytes;
        }

        /// <summary>The deepest call nesting; only lowers the engine's limit.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Not positive.</exception>
        public void SetLimitRecursion(int depth)
        {
            if (depth <= 0)
                throw new ArgumentOutOfRangeException(nameof(depth), "recursion depth must be positive");
            _recursion = depth;
        }

        /// <summary>
        /// The widest int, in bits: the cheapest bound on how long one big-int operation runs; only lowers the
        /// engine's limit.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Not positive.</exception>
        public void SetLimitIntBits(int bits)
        {
            if (bits <= 0)
                throw new ArgumentOutOfRangeException(nameof(bits), "int size limit must be positive");
            _intBits = bits;
        }

        // ---- print ----

        /// <summary>Receives each printed line (<see cref="RunRequest.PrintSink"/>); null collects them for <see cref="GetPrintOutput"/>.</summary>
        public void SetPrintCallback(PrintDelegate print)
        {
            _printSink = print;
        }

        // ---- execution: 0 = OK, otherwise (int)ScriptErrorKind (1..5); NEVER throws ----

        /// <summary>Runs a script with this configuration. Never throws.</summary>
        /// <returns>0 when it succeeded, otherwise the <see cref="ScriptErrorKind"/> as a number (1 to 5).</returns>
        public int Run(string source)
        {
            _last = null;
            _lastError = null;
            try
            {
                var req = new RunRequest
                {
                    ScalarsIn = new Dictionary<string, string>(_scalarsIn, StringComparer.Ordinal),
                    JsonIn = new Dictionary<string, string>(_jsonIn, StringComparer.Ordinal),
                    ScalarOutputNames = _scalarOut.ToArray(),
                    JsonOutputNames = _jsonOut.ToArray(),
                    PrintSink = _printSink,
                    Limits = BuildLimits(),
                    CancellationToken = CancellationToken.None,
                };
                _last = _engine.Run(source, req);
                _lastError = _last.Error;
                _lastCode = _last.Succeeded ? 0 : (int)_last.Error.Kind;
                return _lastCode;
            }
            catch (Exception ex)
            {
                // The top-level guarantee: nothing ever flies out to the host (ScriptEngine.Run itself never throws,
                // so this is a defensive net → EngineFault, code 5).
                _last = null;
                _lastError = new ScriptError(ScriptErrorKind.EngineFault, null,
                    "internal engine error: " + HostText.Of(ex), 0, 0, null, null);
                _lastCode = (int)ScriptErrorKind.EngineFault;
                return _lastCode;
            }
        }

        /// <summary>Clears the configuration and the last result.</summary>
        public void Reset()
        {
            _scalarsIn.Clear();
            _jsonIn.Clear();
            _scalarOut.Clear();
            _jsonOut.Clear();
            _printSink = null;
            _timeoutMs = null;
            _memBytes = null;
            _recursion = null;
            _intBits = null;
            _last = null;
            _lastError = null;
            _lastCode = 0;
        }

        // ---- output ----

        /// <summary>A declared output variable as text, or null (not declared, or the run failed).</summary>
        public string GetScalar(string name)
        {
            string v;
            return _last != null && name != null && _last.ScalarsOut.TryGetValue(name, out v) ? v : null;
        }

        /// <summary>A declared output variable as JSON, or null (not declared, or the run failed).</summary>
        public string GetJson(string name)
        {
            string v;
            return _last != null && name != null && _last.JsonOut.TryGetValue(name, out v) ? v : null;
        }

        /// <summary>What the last run printed, when no print callback was set.</summary>
        public string GetPrintOutput()
        {
            return _last == null ? null : _last.PrintOutput;
        }

        // ---- error ----

        /// <summary>What the last <see cref="Run"/> returned.</summary>
        public int GetErrorCode()
        {
            return _lastCode;
        }

        /// <summary>The exception class for a script error, otherwise the kind ("BudgetExceeded", ...); null after a success.</summary>
        public string GetErrorType()
        {
            if (_lastError == null)
                return null;
            return _lastError.Kind == ScriptErrorKind.RuntimeError ? _lastError.PythonType : _lastError.Kind.ToString();
        }

        /// <summary>The error text, or null after a success.</summary>
        public string GetErrorMessage()
        {
            return _lastError == null ? null : _lastError.Message;
        }

        /// <summary>The script line of the error, or 0.</summary>
        public int GetErrorLine()
        {
            return _lastError == null ? 0 : _lastError.Line;
        }

        /// <summary>The limit that ended the run ("MaxSteps", "DeadlineMs", ...), or the refusal of a run that never started
        /// ("FreeMemory", "ThreadStart", "CircuitBreaker", with the type "HostError"), or null.</summary>
        public string GetErrorLimit()
        {
            return _lastError == null ? null : _lastError.LimitName;   // a refusal is a HostError with a name: a host tells it from a failing host function
        }

        /// <summary>The script traceback of the error, or null.</summary>
        public string GetTraceback()
        {
            return _lastError == null ? null : _lastError.ScriptTraceback;
        }

        // ---- statistics ----

        /// <summary>The last run's time in milliseconds.</summary>
        public long GetElapsedMs()
        {
            return _last == null ? 0 : (long)_last.Stats.Elapsed.TotalMilliseconds;
        }

        /// <summary>The steps the last run took.</summary>
        public long GetStepCount()
        {
            return _last == null ? 0 : _last.Stats.Steps;
        }

        /// <summary>The deepest call nesting the last run reached.</summary>
        public int GetPeakCallDepth()
        {
            return _last == null ? 0 : _last.Stats.PeakCallDepth;
        }

        private ResourceLimits BuildLimits()
        {
            if (_timeoutMs == null && _memBytes == null && _recursion == null && _intBits == null)
                return null;   // no per-run overrides => the engine uses its DefaultLimits as-is
            ResourceLimits baseLimits = _engine.DefaultLimits;
            return baseLimits
                .With(deadlineMs: _timeoutMs, maxAllocBytes: _memBytes, maxCallDepth: _recursion, maxIntBits: _intBits)
                .ClampTo(baseLimits);
        }
    }
}
