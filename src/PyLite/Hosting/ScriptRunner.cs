using System;
using System.Collections.Generic;
using System.Threading;

namespace Outbridge.PyLite.Hosting
{
    // The flat facade for calls from X++ (#11): no generics/Task/IEnumerable in the
    // public surface, every exception is extinguished inside, results come back through getters. One runner
    // holds one configuration; Run builds a RunRequest, invokes the shared ScriptEngine, and unpacks the
    // RunResult. Configuration survives across Run calls; Reset() clears it.
    public sealed class ScriptRunner
    {
        private static readonly Lazy<ScriptEngine> SharedEngine =
            new Lazy<ScriptEngine>(() => new ScriptEngine(new EngineOptions()));

        private readonly ScriptEngine _engine;

        private readonly Dictionary<string, string> _scalarsIn = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _jsonIn = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _scalarOut = new List<string>();
        private readonly List<string> _jsonOut = new List<string>();
        private PrintDelegate _printSink;
        private int? _timeoutMs;
        private long? _memBytes;
        private int? _recursion;

        private RunResult _last;
        private ScriptError _lastError;
        private int _lastCode;

        public ScriptRunner()
            : this(SharedEngine.Value)
        {
        }

        internal ScriptRunner(ScriptEngine engine)
        {
            _engine = engine;
        }

        // ---- input ----

        public void SetScalar(string name, string value)
        {
            _scalarsIn[name] = value;
        }

        public void SetJson(string name, string json)
        {
            _jsonIn[name] = json;
        }

        public void DeclareScalarOutput(string name)
        {
            _scalarOut.Add(name);
        }

        public void DeclareJsonOutput(string name)
        {
            _jsonOut.Add(name);
        }

        // ---- limits (downward only from the server defaults; an invalid value is a host coding error) ----

        public void SetLimitTimeoutMs(int ms)
        {
            if (ms <= 0)
                throw new ArgumentOutOfRangeException(nameof(ms), "timeout must be positive");
            _timeoutMs = ms;
        }

        public void SetLimitMemoryBytes(long bytes)
        {
            if (bytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(bytes), "memory limit must be positive");
            _memBytes = bytes;
        }

        public void SetLimitRecursion(int depth)
        {
            if (depth <= 0)
                throw new ArgumentOutOfRangeException(nameof(depth), "recursion depth must be positive");
            _recursion = depth;
        }

        // ---- print ----

        public void SetPrintCallback(PrintDelegate print)
        {
            _printSink = print;
        }

        // ---- execution: 0 = OK, otherwise (int)ScriptErrorKind (1..5); NEVER throws ----

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
                // The top-level guarantee: nothing ever flies out to X++ (ScriptEngine.Run itself never throws,
                // so this is a defensive net → EngineFault, code 5).
                _last = null;
                _lastError = new ScriptError(ScriptErrorKind.EngineFault, null,
                    "internal engine error: " + ex.GetType().Name + ": " + ex.Message, 0, 0, null, null);
                _lastCode = (int)ScriptErrorKind.EngineFault;
                return _lastCode;
            }
        }

        // Reset the whole input configuration AND the last result (the result is also reset at each Run).
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
            _last = null;
            _lastError = null;
            _lastCode = 0;
        }

        // ---- output ----

        public string GetScalar(string name)
        {
            string v;
            return _last != null && _last.ScalarsOut.TryGetValue(name, out v) ? v : null;
        }

        public string GetJson(string name)
        {
            string v;
            return _last != null && _last.JsonOut.TryGetValue(name, out v) ? v : null;
        }

        public string GetPrintOutput()
        {
            return _last == null ? null : _last.PrintOutput;
        }

        // ---- error ----

        public int GetErrorCode()
        {
            return _lastCode;
        }

        public string GetErrorType()
        {
            if (_lastError == null)
                return null;
            return _lastError.Kind == ScriptErrorKind.RuntimeError ? _lastError.PythonType : _lastError.Kind.ToString();
        }

        public string GetErrorMessage()
        {
            return _lastError == null ? null : _lastError.Message;
        }

        public int GetErrorLine()
        {
            return _lastError == null ? 0 : _lastError.Line;
        }

        public string GetErrorLimit()
        {
            return _lastError != null && _lastError.Kind == ScriptErrorKind.BudgetExceeded ? _lastError.LimitName : null;
        }

        public string GetTraceback()
        {
            return _lastError == null ? null : _lastError.ScriptTraceback;
        }

        // ---- statistics ----

        public long GetElapsedMs()
        {
            return _last == null ? 0 : (long)_last.Stats.Elapsed.TotalMilliseconds;
        }

        public long GetStepCount()
        {
            return _last == null ? 0 : _last.Stats.Steps;
        }

        public int GetPeakCallDepth()
        {
            return _last == null ? 0 : _last.Stats.PeakCallDepth;
        }

        private ResourceLimits BuildLimits()
        {
            if (_timeoutMs == null && _memBytes == null && _recursion == null)
                return null;   // no per-run overrides => the engine uses its DefaultLimits as-is
            ResourceLimits baseLimits = _engine.DefaultLimits;
            return baseLimits
                .With(deadlineMs: _timeoutMs, maxAllocBytes: _memBytes, maxCallDepth: _recursion)
                .ClampTo(baseLimits);
        }
    }
}
