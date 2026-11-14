using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Hosting
{
    // The dedicated 32 MB run thread with a total fault barrier. On net472 an
    // unhandled exception on any thread kills the process, so NOTHING escapes ThreadMain. The result is
    // written before _done is set; the host reads it after WaitCompleted (MRES happens-before barrier).
    internal sealed class RunnerThread
    {
        public const int StackSizeBytes = 32 * 1024 * 1024;   // .NET Framework commits all of it at start
        private static long _runIdCounter;                    // infrastructure statics only: a name counter
        private static long _gcHintCounter;                   // ...and the hygiene-hint counter
        private static readonly HashSet<RunnerThread> _running = new HashSet<RunnerThread>();   // started, not yet ended

        private readonly Thread _thread;
        private readonly ManualResetEventSlim _done = new ManualResetEventSlim(false);
        private readonly Func<RunResult> _work;               // returns a success result or throws
        private readonly Func<EvalContext> _ctxProvider;      // current ctx for error building (may be null early)
        private readonly LeakBreaker _breaker;                // null in unit tests of the thread alone
        private readonly MemoryHygieneOptions _hygiene;       // null => off

        private RunResult _result;
        private Exception _engineFault;
        internal int LeakCounted;                             // LeakBreaker's protocol (Interlocked)

        // Pre-built at type load so the last line of defense allocates NOTHING: under memory pressure
        // the pretty failure path can itself throw (OOM while building strings), and an exception
        // escaping a background thread kills the process on net472. Assigning a static is throw-free.
        private static readonly RunResult FallbackFault = RunResult.Failure(
            new ScriptError(ScriptErrorKind.EngineFault, null, "internal engine error", 0, 0, null, null),
            null, new RunStatistics());

        public RunnerThread(Func<RunResult> work, Func<EvalContext> ctxProvider,
            LeakBreaker breaker = null, MemoryHygieneOptions hygiene = null)
        {
            _work = work;
            _ctxProvider = ctxProvider;
            _breaker = breaker;
            _hygiene = hygiene;
            long id = Interlocked.Increment(ref _runIdCounter);
            _thread = new Thread(ThreadMain, StackSizeBytes)
            {
                IsBackground = true,   // the host must not wait for our threads at shutdown
                Name = "PyLite-Run-" + id.ToString(CultureInfo.InvariantCulture),
            };
        }

        // The tests' handle on a thread that cannot start, which the suite cannot cause for real; null in production.
        internal static Func<Exception> StartFaultForTests;

        public void Start()
        {
            Func<Exception> fault = StartFaultForTests;
            if (fault != null)
                throw fault();
            _thread.Start();
        }

        public bool WaitCompleted(int millisecondsTimeout)
        {
            return _done.Wait(millisecondsTimeout);
        }

        public RunResult TakeResult()
        {
            return _result;
        }

        public Exception EngineFault { get { return _engineFault; } }

        // The run threads of the process that compete for CPU. One inside host code is not counted: a hung host call uses
        // none, and counting the runs stuck in a hung service made them leak later and the breaker open later.
        internal static int Runnable
        {
            get
            {
                lock (_running)
                {
                    int n = 0;
                    foreach (RunnerThread r in _running)
                    {
                        if (!r.InHostCode)
                            n++;
                    }
                    return n;
                }
            }
        }

        // Host code is the only code of a run that can wait for ever: the engine's own work always ends. Written only
        // by this thread, read by the caller waiting for it.
        [ThreadStatic] private static RunnerThread _current;
        // The script call depth at the innermost host call (-1: none, int.MaxValue: a call that cannot re-enter the script).
        // Script code the host calls back (a __repr__ through ScriptValue.Repr) runs deeper, so it is no host code.
        private int _hostAt = -1;
        private EvalContext _hostCtx;

        internal bool InHostCode
        {
            get
            {
                int at = Volatile.Read(ref _hostAt);
                if (at < 0)
                    return false;
                EvalContext ctx = Volatile.Read(ref _hostCtx);
                return ctx == null || Volatile.Read(ref ctx.CallDepth) <= at;
            }
        }

        // Returns the mark LeaveHost restores; ctx null for a call that cannot re-enter the script (clock, sleep policy).
        internal static int EnterHost(EvalContext ctx)
        {
            RunnerThread r = _current;
            if (r == null)
                return -1;
            int previous = r._hostAt;
            if (ctx != null)
                Volatile.Write(ref r._hostCtx, ctx);
            Volatile.Write(ref r._hostAt, ctx != null ? ctx.CallDepth : int.MaxValue);
            return previous;
        }

        internal static void LeaveHost(int mark)
        {
            RunnerThread r = _current;
            if (r != null)
                Volatile.Write(ref r._hostAt, mark);
        }

        private void ThreadMain()
        {
            _current = this;
            // Never inherited: in an ASP.NET host in .NET 4.5 mode a new thread took the machine's culture, in a
            // console host the caller's, and .NET's texts, minus signs and number formats reached scripts. Set here, on this
            // thread: from .NET 4.6 on, setting it on the unstarted Thread object sets the CALLER's culture.
            try
            {
                try
                {
                    lock (_running)
                        _running.Add(this);
                    Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                    Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
                    _result = _work();
                }
                catch (Exception ex)
                {
                    _result = BuildFailure(ex);
                }
            }
            catch (Exception)
            {
                // Last line of defense: even failure construction threw (e.g. OOM while building the
                // message). The pre-built static is a plain assignment — this path cannot throw.
                _result = FallbackFault;
            }
            finally
            {
                try
                {
                    lock (_running)
                        _running.Remove(this);
                }
                catch (Exception)
                {
                    // nothing may leave this finally
                }
                // The epilogue must be as escape-proof as the barrier: an exception leaving a finally
                // on this thread would kill the process. The breaker's bookkeeping is guarded; the
                // completion signal fires no matter what.
                try
                {
                    try
                    {
                        if (_breaker != null)
                            _breaker.UnregisterLeak(this);   // no-op unless the host marked this thread leaked
                    }
                    catch (Exception)
                    {
                        // a broken breaker must not take the process down; the run result is already set
                    }
                    finally
                    {
                        _done.Set();                  // unconditional completion signal
                    }
                    TryMemoryHygiene();               // after the caller unblocks
                }
                catch (Exception)
                {
                    // out of memory here too, in the first JIT of what is called above: its own try starts too late
                }
            }
        }

        // Threshold-gated GC hint; Optimized mode lets the CLR decline. Hygiene must never fault the engine.
        private void TryMemoryHygiene()
        {
            try
            {
                if (_hygiene == null || _hygiene.PostRunGcHintThresholdBytes <= 0)
                    return;
                RunResult r = _result;
                long allocated = r != null && r.Stats != null ? r.Stats.AllocatedBytes : 0;
                if (allocated < _hygiene.PostRunGcHintThresholdBytes)
                    return;
                long n = Interlocked.Increment(ref _gcHintCounter);
                if (_hygiene.LohCompactionEveryNHints > 0 && n % _hygiene.LohCompactionEveryNHints == 0)
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                        System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Optimized);
            }
            catch (Exception)
            {
                // swallowed by design: a hygiene failure must not affect the completed run
            }
        }

        private RunResult BuildFailure(Exception ex)
        {
            EvalContext ctx = _ctxProvider != null ? _ctxProvider() : null;
            if (EngineAbort.TryUnwrap(ex) == null && !(ex is ScriptException))   // a shortage too: reported as one, still logged
            {
                _engineFault = ex;   // an engine bug; hosting logs the full exception
            }
            ScriptError err = ScriptErrorBuilder.Build(ex, ctx);
            return RunResult.Failure(err, null, StatsFrom(ctx));
        }

        // ctx.Stats keeps the running totals (host/regex time, peak depth) the run already accumulated.
        private static RunStatistics StatsFrom(EvalContext ctx)
        {
            if (ctx == null)
                return new RunStatistics();
            ctx.Stats.Snapshot(ctx);
            return ctx.Stats;
        }
    }
}
