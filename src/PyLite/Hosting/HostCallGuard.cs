using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Hosting
{
    // The single wrapper around any host delegate. Deadline is checked before/after; host time
    // counts toward the run deadline; anything the delegate throws other than EngineAbort/ScriptException
    // becomes a HostCallFailure (the script cannot catch it).
    internal static class HostCallGuard
    {
        // A host-provided builtin override (HostFunctionTable.Set). EngineAbort/ScriptException fly through
        // (a ScriptException is a script-catchable error via ScriptExceptions); anything else -> HostCallFailure
        // -> HostError. A null result becomes None.
        public static ScriptValue Invoke(string name, BuiltinDelegate impl, EvalContext ctx,
            ScriptValue[] args, KwArgs kw)
        {
            return InvokeOn(name, impl, null, ctx, args, kw);   // free builtin: self == null
        }

        // The same for a method of a host-built type (SlotDescriptor.MakeMethod), with its receiver.
        internal static ScriptValue InvokeOn(string name, BuiltinDelegate impl, ScriptValue self, EvalContext ctx,
            ScriptValue[] args, KwArgs kw)
        {
            ctx.Budget.CheckDeadlineNow();
            long t0 = Stopwatch.GetTimestamp();
            int hostMark = RunnerThread.EnterHost(ctx);
            ScriptValue result;
            try
            {
                result = impl(self, args, kw, ctx);
            }
            catch (HostScriptException hse)
            {
                throw Fresh(hse, ctx);
            }
            catch (Exception ex) when (IsHostFault(ex))
            {
                throw Failure(name, ex);
            }
            finally
            {
                RunnerThread.LeaveHost(hostMark);
                RecordHostTime(ctx, t0);
            }
            if (ctx.Budget.IsTerminating)
                throw ctx.Budget.PendingAbort;   // the host may have swallowed a budget abort
            ctx.Budget.CheckDeadlineNow();
            return result ?? ctx.Values.None;
        }
        // A property of a host-built type (SlotDescriptor.MakeProperty), fenced as Invoke.
        internal static ScriptValue Get(string name, Func<ScriptValue, EvalContext, ScriptValue> getter, ScriptValue self, EvalContext ctx)
        {
            ctx.Budget.CheckDeadlineNow();
            long t0 = Stopwatch.GetTimestamp();
            int hostMark = RunnerThread.EnterHost(ctx);
            ScriptValue result;
            try
            {
                result = getter(self, ctx);
            }
            catch (HostScriptException hse)
            {
                throw Fresh(hse, ctx);
            }
            catch (Exception ex) when (IsHostFault(ex))
            {
                throw Failure(name, ex);
            }
            finally
            {
                RunnerThread.LeaveHost(hostMark);
                RecordHostTime(ctx, t0);
            }
            if (ctx.Budget.IsTerminating)
                throw ctx.Budget.PendingAbort;
            ctx.Budget.CheckDeadlineNow();
            return result ?? ctx.Values.None;
        }

        // The print-callback path (no ScriptValue result). Used by OutputSink.
        public static void InvokePrint(PrintDelegate sink, string line, EvalContext ctx)
        {
            ctx.Budget.CheckDeadlineNow();
            long t0 = Stopwatch.GetTimestamp();
            int hostMark = RunnerThread.EnterHost(ctx);
            try
            {
                sink(line);
            }
            catch (HostScriptException hse)
            {
                throw Fresh(hse, ctx);
            }
            catch (Exception ex) when (IsHostFault(ex))
            {
                throw Failure("print", ex);
            }
            finally
            {
                RunnerThread.LeaveHost(hostMark);
                RecordHostTime(ctx, t0);
            }
        }

        // A host module (EngineOptions.ExtraModules) is host code: its CreateInstance and every function it
        // hands out are fenced like HostFunctionTable.Set's, where an escaping exception was an engine fault.
        public static ModuleValue CreateModule(IModuleProvider provider, EvalContext ctx)
        {
            ctx.Budget.CheckDeadlineNow();
            long t0 = Stopwatch.GetTimestamp();
            int hostMark = RunnerThread.EnterHost(ctx);
            ModuleValue m;
            try
            {
                m = provider.CreateInstance(ctx);
            }
            catch (HostScriptException hse)
            {
                throw Fresh(hse, ctx);
            }
            catch (Exception ex) when (IsHostFault(ex))
            {
                throw Failure(provider.Name, ex);
            }
            finally
            {
                RunnerThread.LeaveHost(hostMark);
                RecordHostTime(ctx, t0);
            }
            if (ctx.Budget.IsTerminating)
                throw ctx.Budget.PendingAbort;
            if (m == null)
                throw new HostCallFailure(provider.Name, new InvalidOperationException("CreateInstance returned null"));
            var members = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, ScriptValue> kv in m.Members)
            {
                if (kv.Value == null)
                    throw new HostCallFailure(provider.Name, new InvalidOperationException("the member '" + kv.Key + "' is null"));
                BuiltinFunctionValue f = kv.Value as BuiltinFunctionValue;
                members[kv.Key] = f != null && !f.IsHostProvided ? ctx.Values.BuiltinFunction(f.Name, f.Fn) : kv.Value;
            }
            return ctx.Values.Module(m.Name, members);
        }

        // A step of an iterator host code made (ValueFactory.Iterator), fenced as Invoke; a null item becomes None.
        public static bool MoveNext(IScriptIterator it, string name, EvalContext ctx, out ScriptValue value)
        {
            long t0 = Stopwatch.GetTimestamp();
            int hostMark = RunnerThread.EnterHost(ctx);
            bool more;
            try
            {
                more = it.MoveNext(ctx, out value);
            }
            catch (HostScriptException hse)
            {
                throw Fresh(hse, ctx);
            }
            catch (Exception ex) when (IsHostFault(ex))
            {
                throw Failure(name, ex);
            }
            finally
            {
                RunnerThread.LeaveHost(hostMark);
                RecordHostTime(ctx, t0);
            }
            if (ctx.Budget.IsTerminating)
                throw ctx.Budget.PendingAbort;
            ctx.Budget.CheckDeadlineAfterCall();
            if (more && value == null)
                value = ctx.Values.None;
            return more;
        }

        // A host may throw one ScriptExceptions-built object from every run (a static field): each run raises
        // its own copy, or run B read and changed run A's notes, cause and traceback, and the message's cached
        // hash crossed engines.
        private static ScriptException Fresh(HostScriptException hse, EvalContext ctx)
        {
            ExceptionValue v = hse.Value;
            var args = new ScriptValue[v.Args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                StrValue s = v.Args[i] as StrValue;
                args[i] = s != null ? ctx.Values.Str(s.Value) : v.Args[i];
            }
            return new ScriptException(ctx.Values.Exception(v.ExcType, args));
        }

        // EngineAbort and ScriptException are the engine's own and fly through unwrapped; a host can only build a
        // ScriptException with no exception value in it (its public constructor), which is no script error at all.
        private static bool IsHostFault(Exception ex)
        {
            ScriptException se = ex as ScriptException;
            return se != null ? se.Value == null : !(ex is EngineAbort);
        }

        // A host call that aborts its own thread (Response.End, Thread.CurrentThread.Abort) ends the run, not the
        // thread: unreset, the abort rises again at the end of every catch up to the runner, and the run was an
        // engine fault. An AppDomain unloading keeps its abort. One already reset, or thrown from another thread's
        // task, has nothing to reset: ResetAbort would throw.
        internal static HostCallFailure Failure(string name, Exception ex)
        {
            if (ex is ThreadAbortException && !AppDomain.CurrentDomain.IsFinalizingForUnload()
                && (Thread.CurrentThread.ThreadState & System.Threading.ThreadState.AbortRequested) != 0)
                Thread.ResetAbort();
            return new HostCallFailure(name, ex);
        }

        private static void RecordHostTime(EvalContext ctx, long t0)
        {
            long dt = Stopwatch.GetTimestamp() - t0;
            ctx.Stats.HostCallTime += TimeSpan.FromSeconds((double)dt / Stopwatch.Frequency);
        }
    }

    // What ScriptExceptions builds: an exception made outside any run, copied into the run that raises it.
    internal sealed class HostScriptException : ScriptException
    {
        internal HostScriptException(ExceptionValue value) : base(value)
        {
        }
    }

    // An iterator host code made for a script: every step runs through HostCallGuard.MoveNext.
    internal sealed class FencedIterator : IScriptIterator
    {
        private readonly IScriptIterator _inner;
        private readonly string _name;

        internal FencedIterator(IScriptIterator inner, string name)
        {
            _inner = inner;
            _name = name;
        }

        public bool MoveNext(EvalContext ctx, out ScriptValue value)
        {
            return HostCallGuard.MoveNext(_inner, _name, ctx, out value);
        }
    }
}
