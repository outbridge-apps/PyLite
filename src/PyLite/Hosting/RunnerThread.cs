using System;
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
        public const int StackSizeBytes = 32 * 1024 * 1024;   // x64 reserve is cheap
        private static long _runIdCounter;                    // infrastructure statics only: a name counter
        private static long _gcHintCounter;                   // ...and the hygiene-hint counter

        private readonly Thread _thread;
        private readonly ManualResetEventSlim _done = new ManualResetEventSlim(false);
        private readonly Func<RunResult> _work;               // returns a success result or throws
        private readonly Func<EvalContext> _ctxProvider;      // current ctx for error building (may be null early)
        private readonly RunGate _gate;                       // null in unit tests of the thread alone
        private readonly RunTicket _ticket;
        private readonly MemoryHygieneOptions _hygiene;       // null => off

        private RunResult _result;
        private Exception _engineFault;
        internal int LeakCounted;                             // RunGate leak protocol (Interlocked)

        // Pre-built at type load so the last line of defense allocates NOTHING: under memory pressure
        // the pretty failure path can itself throw (OOM while building strings), and an exception
        // escaping a background thread kills the process on net472. Assigning a static is throw-free.
        private static readonly RunResult FallbackFault = RunResult.Failure(
            new ScriptError(ScriptErrorKind.EngineFault, null, "internal engine error", 0, 0, null, null),
            null, new RunStatistics());

        public RunnerThread(Func<RunResult> work, Func<EvalContext> ctxProvider,
            RunGate gate = null, RunTicket ticket = null, MemoryHygieneOptions hygiene = null)
        {
            _work = work;
            _ctxProvider = ctxProvider;
            _gate = gate;
            _ticket = ticket;
            _hygiene = hygiene;
            long id = Interlocked.Increment(ref _runIdCounter);
            _thread = new Thread(ThreadMain, StackSizeBytes)
            {
                IsBackground = true,   // the host must not wait for our threads at shutdown
                Name = "PyLite-Run-" + id.ToString(CultureInfo.InvariantCulture),
            };
        }

        public void Start()
        {
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

        private void ThreadMain()
        {
            try
            {
                try
                {
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
                // The epilogue must be as escape-proof as the barrier: an exception leaving a finally
                // on this thread would kill the process. Gate bookkeeping is guarded; the completion
                // signal fires no matter what.
                try
                {
                    if (_gate != null)
                    {
                        _gate.UnregisterLeak(this);   // no-op unless the host marked this thread leaked
                        _gate.Release(_ticket);       // slot returned strictly here
                    }
                }
                catch (Exception)
                {
                    // a broken gate must not take the process down; the run result is already set
                }
                finally
                {
                    _done.Set();                      // unconditional completion signal
                }
                TryMemoryHygiene();                   // self-guarded, after the caller unblocks
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
            if (EngineAbort.TryUnwrap(ex) == null && !(ex is ScriptException))
            {
                _engineFault = ex;   // an engine bug; hosting logs the full exception
            }
            ScriptError err = ScriptErrorBuilder.Build(ex, ctx);
            return RunResult.Failure(err, null, StatsFrom(ctx));
        }

        private static RunStatistics StatsFrom(EvalContext ctx)
        {
            var s = new RunStatistics();
            if (ctx != null)
            {
                s.Steps = ctx.Budget.StepsUsed;
                s.AllocatedBytes = ctx.Budget.AllocBytesUsed;
                s.OutputBytes = ctx.Budget.OutputBytesUsed;
                s.TerminatingEntered = ctx.Budget.IsTerminating;
                s.ModulesImported = ctx.ModuleInstances.Count;
            }
            return s;
        }
    }
}
