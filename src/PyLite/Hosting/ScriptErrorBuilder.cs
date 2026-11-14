using System;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Hosting
{
    // Turns any exception escaping a run into a host-facing ScriptError. Called from
    // RunnerThread.ThreadMain and from hosting. The full script message text is computed EXACTLY here,
    // once, guarded against a secondary EngineAbort.
    internal static class ScriptErrorBuilder
    {
        public static ScriptError Build(Exception ex, EvalContext ctx)
        {
            int line = ctx != null ? ctx.CurrentLine : 0;

            EngineAbort abort = EngineAbort.TryUnwrap(ex);
            if (abort != null)
            {
                // Budget errors: message is fixed and cheap; the stack collection may not have finished.
                return new ScriptError(ScriptErrorKind.BudgetExceeded, null, abort.Message,
                    line, 0, abort.LimitName, "");
            }

            ScriptException se = ex as ScriptException;
            if (se != null)
            {
                // Line = the innermost frame (the raise site); ctx.CurrentLine has been restored to the
                // module-level statement by the Call unwind and is the fallback for frameless throws.
                // A frame of a module (string.Template, a host PythonModules module) carries that source's lines:
                // Line is the innermost frame of the script itself.
                var frames = se.Value.TracebackFrames;
                if (frames != null && frames.Count > 0)
                {
                    line = frames[0].Line;
                    foreach (TracebackFrame f in frames)
                    {
                        if (f.File == null)
                        {
                            line = f.Line;
                            break;
                        }
                    }
                }
                string typeName = se.Value.ExcType.Name;
                string message = SafeMessage(se.Value, typeName, ctx);
                string traceback = TracebackBuilder.BuildText(se.Value, message, e => SafeMessage(e, e.ExcType.Name, ctx),
                    EngineHardLimits.MaxTracebackChars);
                if (ctx != null)
                {
                    ctx.Stats.TracebackTruncated = se.Value.TracebackTruncated;
                }
                return new ScriptError(ScriptErrorKind.RuntimeError, typeName, message,
                    line, 0, null, traceback);
            }

            // An overflow outside any call (an operator's own cast): still the script's error, not a fault.
            if (ex is OverflowException)
            {
                return new ScriptError(ScriptErrorKind.RuntimeError, "OverflowError",
                    "Python int too large to convert to C int", line, 0, null, null);
            }

            if (IsShortage(ex))
                return OutOfMemory;

            // An engine bug: never leak script data into Message; the full exception is logged elsewhere.
            return new ScriptError(ScriptErrorKind.EngineFault, null, "internal engine error",
                line, 0, null, null);
        }

        // Memory the process could not get, or a thread it could not start, is a shortage and not an engine bug: the host
        // reads it as "try later", like a refused run. Pre-built, as building anything now may fail too.
        internal static readonly ScriptError OutOfMemory = new ScriptError(ScriptErrorKind.HostError, null,
            "engine unavailable: out of memory", 0, 0, "FreeMemory", null);

        internal static bool IsShortage(Exception ex)
        {
            return ex is OutOfMemoryException || ex is System.Threading.ThreadStartException;
        }

        private static string SafeMessage(ExceptionValue value, string typeName, EvalContext ctx)
        {
            try
            {
                return value.GetMessageText(ctx);
            }
            catch (EngineAbort)
            {
                return "<" + typeName + ": message unavailable>";
            }
            catch (ScriptException)
            {
                return "<" + typeName + ": message unavailable>";
            }
        }
    }
}
