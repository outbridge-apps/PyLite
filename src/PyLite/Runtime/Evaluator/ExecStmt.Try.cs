using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // try/except/else/finally + raise. EngineAbort ALWAYS flies through except
    // finally runs on every outcome and its signal overrides the pending one; the `as` variable is
    // cleared on exit (Py3). ALL control flow uses ExecSignal — the raise statement travels as a
    // Raised signal (a .NET throw costs tens of microseconds on net472); only expression-level raises
    // (engine C# code) and EngineAbort arrive as C# exceptions, and an unmatched exception leaves as
    // a signal again (RunBody/RunModule restore the ScriptException carrier at the boundary).
    // Exceptions from the else block, the except type expressions and the handler bodies are NOT
    // matched by this try's own handlers, but still reach its finally (CPython).
    internal sealed partial class Evaluator
    {
        private ExecSignal ExecTry(TryNode n, Environment env, EvalContext ctx)
        {
            ExecSignal pendingSignal = ExecSignal.Normal;
            System.Exception pendingExc = null;
            ExceptionValue bodyRaise = null;   // a script exception from the BODY, awaiting handler matching
            bool bodyNormal = false;

            try
            {
                ExecSignal sig = ExecBlock(n.Body, env, ctx);
                if (sig.Kind == ExecSignalKind.Raised)
                {
                    bodyRaise = sig.Raised;   // a raise statement arrives as a signal — no .NET throw
                }
                else if (!sig.IsNormal)
                {
                    pendingSignal = sig;
                }
                else
                {
                    bodyNormal = true;
                }
            }
            catch (System.Exception ex)
            {
                if (EngineAbort.IsOrWraps(ex))
                {
                    pendingExc = EngineAbort.TryUnwrap(ex);   // never caught by except
                }
                else
                {
                    ScriptException se = ex as ScriptException;
                    if (se == null)
                    {
                        pendingExc = ex;   // an engine fault — do not catch, off to finally
                    }
                    else
                    {
                        bodyRaise = se.Value;
                    }
                }
            }

            if (bodyRaise != null)
            {
                // Matching and the handler body run OUTSIDE the body's catch: an exception THEY raise
                // (a NameError in the type expression, a raise in the handler) is not re-matched by
                // this try's own handlers, but still reaches finally below (CPython).
                try
                {
                    if (!MatchAndRunHandler(n, bodyRaise, env, ctx, ref pendingSignal))
                    {
                        pendingSignal = ExecSignal.Raise(bodyRaise);   // no match — propagate after finally
                    }
                }
                catch (System.Exception hex)
                {
                    pendingExc = hex;
                }
            }
            else if (bodyNormal && n.OrElse.Count > 0)
            {
                // else runs outside the body's catch too: its exceptions bypass this try's handlers
                // (CPython), but still reach finally below.
                try
                {
                    pendingSignal = ExecBlock(n.OrElse, env, ctx);
                }
                catch (System.Exception eex)
                {
                    pendingExc = eex;
                }
            }

            if (n.Finally.Count > 0)
            {
                bool terminating = ctx.Budget.IsTerminating;
                ExecSignal fsig;
                try
                {
                    fsig = ExecBlock(n.Finally, env, ctx);
                }
                catch (System.Exception fex)
                {
                    if (EngineAbort.IsOrWraps(fex))
                    {
                        throw EngineAbort.TryUnwrap(fex);
                    }
                    if (terminating)
                    {
                        fsig = ExecSignal.Normal;   // swallow: EngineAbort dominates
                    }
                    else
                    {
                        throw;   // an exception from finally REPLACES pending
                    }
                }
                if (fsig.Kind == ExecSignalKind.Raised && terminating)
                {
                    fsig = ExecSignal.Normal;   // swallow, like the catch above: EngineAbort dominates
                }
                if (!fsig.IsNormal)
                {
                    return fsig;   // finally break/continue/return/raise overrides pending (and suppresses pendingExc)
                }
            }

            if (pendingExc != null)
            {
                throw pendingExc;
            }
            return pendingSignal;
        }

        // Returns false when no handler matched. On a match, runs the handler body and stores its
        // signal in pendingSignal; exceptions from matching or the handler body propagate to the caller.
        private bool MatchAndRunHandler(TryNode n, ExceptionValue exc, Environment env,
            EvalContext ctx, ref ExecSignal pendingSignal)
        {
            ExceptHandler matched = null;
            for (int i = 0; i < n.Handlers.Count; i++)
            {
                ExceptHandler h = n.Handlers[i];
                ctx.Budget.Step(BudgetCost.HandlerMatch);
                if (h.Type == null)
                {
                    matched = h;
                    break;
                }
                if (MatchesHandler(exc, EvalExpr(h.Type, env, ctx), ctx))
                {
                    matched = h;
                    break;
                }
            }
            if (matched == null)
            {
                return false;
            }

            // Frames are appended when an exception LEAVES a frame, so one raised by the engine and
            // caught in the same frame would carry none at all and print without a Traceback header
            // once it becomes a __context__/__cause__ of something else. Stamp its raise site here.
            if ((exc.TracebackFrames == null || exc.TracebackFrames.Count == 0) && exc.RaiseLine > 0)
            {
                TracebackBuilder.AppendFrame(exc, ctx.CurrentFunctionName, exc.RaiseLine);
                exc.FrameStampedAtCatch = true;
            }
            else if (!exc.FrameStampedAtCatch && exc.TracebackFrames != null && exc.TracebackFrames.Count > 0)
            {
                // Arrived from a callee: the catching frame is part of the traceback too (the line of the
                // call that raised, as CPython shows it), and traceback.format_exc() reads it right here.
                // The stamp flag makes the frame-exit append skip this frame should the handler re-raise.
                TracebackBuilder.AppendFrame(exc, ctx.CurrentFunctionName, ctx.CurrentLine);
                exc.FrameStampedAtCatch = true;
            }

            ExceptionValue prevHandled = ctx.CurrentHandledException;
            ctx.CurrentHandledException = exc;
            try
            {
                if (matched.Name != null)
                {
                    env.BindScopeName(matched.Name, exc);
                }
                try
                {
                    pendingSignal = ExecBlock(matched.Body, env, ctx);
                    return true;
                }
                finally
                {
                    if (matched.Name != null)
                    {
                        env.SafeDelScopeName(matched.Name);   // Py3: del e on exit
                    }
                }
            }
            finally
            {
                ctx.CurrentHandledException = prevHandled;
            }
        }

        private bool MatchesHandler(ExceptionValue exc, ScriptValue typeVal, EvalContext ctx)
        {
            TypeValue tv = typeVal as TypeValue;
            if (tv != null && tv.ExcType != null)
            {
                return exc.ExcType.IsSubtypeOf(tv.ExcType);
            }
            TupleValue tup = typeVal as TupleValue;
            if (tup != null)
            {
                for (int i = 0; i < tup.Items.Length; i++)
                {
                    if (MatchesHandler(exc, tup.Items[i], ctx))
                    {
                        return true;
                    }
                }
                return false;
            }
            throw Raise.TypeError(ctx, "catching classes that do not inherit from BaseException is not allowed");
        }

        private ExecSignal ExecRaise(RaiseNode n, Environment env, EvalContext ctx)
        {
            if (n.Exc == null)
            {
                if (ctx.CurrentHandledException == null)
                {
                    throw Raise.RuntimeError(ctx, "No active exception to re-raise");
                }
                return ExecSignal.Raise(ctx.CurrentHandledException);   // traceback keeps accumulating
            }

            ExceptionValue ev = CoerceToException(EvalExpr(n.Exc, env, ctx), ctx);
            if (n.Cause != null)
            {
                // `raise X from None` clears the explicit cause (CPython also flags suppress_context).
                ScriptValue cause = EvalExpr(n.Cause, env, ctx);
                ev.Cause = cause.Kind == ValueKind.None ? null : CoerceToException(cause, ctx);
                ev.SuppressContext = true;
            }
            if (ctx.CurrentHandledException != null && !ReferenceEquals(ctx.CurrentHandledException, ev))
            {
                SetContextChain(ev, ctx.CurrentHandledException);
            }
            ctx.Budget.Step(BudgetCost.Raise);
            ev.RaiseLine = ctx.CurrentLine;   // explicit raise refreshes the raise site (unlike bare raise)
            ev.FrameStampedAtCatch = false;   // ... and re-arms the frame this raise travels out of
            return ExecSignal.Raise(ev);
        }

        private ExceptionValue CoerceToException(ScriptValue v, EvalContext ctx)
        {
            ExceptionValue ev = v as ExceptionValue;
            if (ev != null)
            {
                return ev;
            }
            TypeValue tv = v as TypeValue;
            if (tv != null && tv.ExcType != null)
            {
                return (ExceptionValue)Call(tv, System.Array.Empty<ScriptValue>(), KwArgs.Empty, ctx);
            }
            throw Raise.TypeError(ctx, "exceptions must derive from BaseException");
        }

        // Attach `context` as ev's implicit __context__, trimming the chain to 16.
        private static void SetContextChain(ExceptionValue ev, ExceptionValue context)
        {
            ev.Context = context;
            ExceptionValue p = ev;
            int depth = 0;
            while (p.Context != null && depth < 16)
            {
                p = p.Context;
                depth++;
            }
            if (p.Context != null)
            {
                p.Context = null;
            }
        }
    }
}
