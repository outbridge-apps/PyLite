using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // traceback: the text the host would print for an exception, from inside the script. format_exc()
    // renders the exception being handled (the engine's own frames: "<script>", line, function);
    // format_exception/format_exception_only take an exception object (3.10+ signature). print_* write
    // to the script's print sink. There is no traceback object, so the (type, value, tb) triple, the
    // limit argument and extract_tb/walk_tb are absent.
    public static class TracebackModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["format_exc"] = BuiltinFunctionValue.Make("format_exc", (s, a, kw, c) => c.Values.Str(FormatCurrent(c, a, kw, "format_exc"))),
                ["print_exc"] = BuiltinFunctionValue.Make("print_exc", (s, a, kw, c) => Print(c, FormatCurrent(c, a, kw, "print_exc"))),
                ["format_exception"] = BuiltinFunctionValue.Make("format_exception", (s, a, kw, c) => Lines(c, FormatOf(c, a, kw, "format_exception", true))),
                ["print_exception"] = BuiltinFunctionValue.Make("print_exception", (s, a, kw, c) => Print(c, FormatOf(c, a, kw, "print_exception", true))),
                ["format_exception_only"] = BuiltinFunctionValue.Make("format_exception_only", (s, a, kw, c) => Lines(c, FormatOf(c, a, kw, "format_exception_only", false))),
            };
            return ctx.Values.Module("traceback", m);
        }

        private static bool ChainArg(EvalContext ctx, ScriptValue[] a, KwArgs kw, int pos)
        {
            ScriptValue v = a.Length > pos ? a[pos] : null;
            ScriptValue kv;
            if (kw.TryGet("chain", out kv))
                v = kv;
            return v == null || v.IsTruthy(ctx);
        }

        // The exception being handled, rendered as the host would; "NoneType: None" outside an except.
        private static string FormatCurrent(EvalContext ctx, ScriptValue[] a, KwArgs kw, string fn)
        {
            Args.AtMost(ctx, a, fn, 2);
            ExceptionValue exc = ctx.CurrentHandledException;
            if (exc == null)
                return "NoneType: None\n";
            return Render(ctx, exc, ChainArg(ctx, a, kw, 1), true);
        }

        private static string FormatOf(EvalContext ctx, ScriptValue[] a, KwArgs kw, string fn, bool withFrames)
        {
            ScriptValue v = a.Length >= 1 ? a[0] : null;
            ScriptValue kv;
            if (kw.TryGet("exc", out kv))
                v = kv;
            if (v == null)
                throw Raise.TypeError(ctx, fn + "() missing required argument 'exc'");
            if (a.Length > 1 && !(a[1] is ExceptionValue) && a[1].Kind != ValueKind.None)
                throw Raise.TypeError(ctx, fn + "() takes an exception object; the (type, value, tb) form is not supported in this dialect");
            if (a.Length > 1)
                v = a[1];   // format_exception(type, value, tb): the value is what gets rendered
            if (v.Kind == ValueKind.None)
                return "NoneType: None\n";
            ExceptionValue exc = v as ExceptionValue;
            if (exc == null)
                throw Raise.TypeError(ctx, fn + "() argument must be an exception object, not " + v.PyTypeName);
            return Render(ctx, exc, withFrames && ChainArg(ctx, a, kw, 3), withFrames);
        }

        private static string Render(EvalContext ctx, ExceptionValue exc, bool chain, bool withFrames)
        {
            string text;
            if (!withFrames)
                text = OnlyLine(ctx, exc);
            else if (chain)
                text = TracebackBuilder.BuildText(exc, exc.GetMessageText(ctx), e => e.GetMessageText(ctx));
            else
                text = TracebackBuilder.BuildText(Unchained(exc), exc.GetMessageText(ctx));
            ctx.Values.EnsureStrLen(text.Length + 1);
            return text + "\n";
        }

        // A shallow twin without __cause__/__context__ so the builder renders one block.
        private static ExceptionValue Unchained(ExceptionValue exc)
        {
            if (exc.Cause == null && exc.Context == null)
                return exc;
            var twin = new ExceptionValue(exc.ExcType, exc.Args);
            twin.TracebackFrames = exc.TracebackFrames;
            twin.TracebackTruncated = exc.TracebackTruncated;
            twin.TracebackDropped = exc.TracebackDropped;
            twin.Notes = exc.Notes;
            return twin;
        }

        private static string OnlyLine(EvalContext ctx, ExceptionValue exc)
        {
            string msg = exc.GetMessageText(ctx);
            string line = string.IsNullOrEmpty(msg) ? exc.ExcType.Name : exc.ExcType.Name + ": " + msg;
            if (exc.Notes != null)
                foreach (string note in exc.Notes)
                    line += "\n" + note;
            return line;
        }

        // format_exception*: the text split into lines, each keeping its newline (CPython's shape).
        private static ScriptValue Lines(EvalContext ctx, string text)
        {
            string[] parts = text.Split('\n');
            ListValue list = ctx.Values.List(parts.Length);
            for (int i = 0; i < parts.Length - 1; i++)
                list.Add(ctx.Values.Str(parts[i] + "\n"), ctx);
            if (parts[parts.Length - 1].Length > 0)
                list.Add(ctx.Values.Str(parts[parts.Length - 1]), ctx);
            return list;
        }

        private static ScriptValue Print(EvalContext ctx, string text)
        {
            // the same two routes as print(): the host sink, or the buffer of an unhosted run
            if (ctx.Out != null)
                ctx.Out.Write(text);
            else
            {
                ctx.Budget.ChargeOutput(2L * text.Length);
                if (ctx.PrintBuffer == null)
                    ctx.PrintBuffer = new System.Text.StringBuilder();
                ctx.PrintBuffer.Append(text);
            }
            return ctx.Values.None;
        }
    }
}
