using System;
using System.Diagnostics;
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
            ctx.Budget.CheckDeadlineNow();
            long t0 = Stopwatch.GetTimestamp();
            ScriptValue result;
            try
            {
                result = impl(null, args, kw, ctx);   // free builtin: self == null
            }
            catch (EngineAbort)
            {
                RecordHostTime(ctx, t0);
                throw;
            }
            catch (ScriptException)
            {
                RecordHostTime(ctx, t0);
                throw;
            }
            catch (Exception ex)
            {
                RecordHostTime(ctx, t0);
                throw new HostCallFailure(name, ex);
            }
            RecordHostTime(ctx, t0);
            if (ctx.Budget.IsTerminating)
                throw ctx.Budget.PendingAbort;   // the host may have swallowed a budget abort
            ctx.Budget.CheckDeadlineNow();
            return result ?? ctx.Values.None;
        }
        // The print-callback path (no ScriptValue result). Used by OutputSink.
        public static void InvokePrint(PrintDelegate sink, string line, EvalContext ctx)
        {
            ctx.Budget.CheckDeadlineNow();
            long t0 = Stopwatch.GetTimestamp();
            try
            {
                sink(line);
            }
            catch (EngineAbort)
            {
                RecordHostTime(ctx, t0);
                throw;
            }
            catch (ScriptException)
            {
                RecordHostTime(ctx, t0);
                throw;
            }
            catch (Exception ex)
            {
                RecordHostTime(ctx, t0);
                throw new HostCallFailure("print", ex);
            }
            RecordHostTime(ctx, t0);
        }

        private static void RecordHostTime(EvalContext ctx, long t0)
        {
            long dt = Stopwatch.GetTimestamp() - t0;
            ctx.Stats.HostCallTime += TimeSpan.FromSeconds((double)dt / Stopwatch.Frequency);
        }
    }
}
