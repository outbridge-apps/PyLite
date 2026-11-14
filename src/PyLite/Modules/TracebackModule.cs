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
    // to the script's print sink. There is no traceback object, so the (type, value, tb) triple and
    // extract_tb/walk_tb are absent.
    internal static class TracebackModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["format_exc"] = BuiltinFunctionValue.Make("format_exc", Args.KnownKeywords("format_exc", 2, (s, a, kw, c) => c.Values.Str(FormatCurrent(c, a, kw, "format_exc", 1)), "limit", "chain")),
                ["print_exc"] = BuiltinFunctionValue.Make("print_exc", Args.KnownKeywords("print_exc", 3, (s, a, kw, c) => Print(c, FormatCurrent(c, a, kw, "print_exc", 2), FileArg(a, kw, 1)), "limit", "file", "chain")),
                ["format_exception"] = BuiltinFunctionValue.Make("format_exception", Args.Signature("format_exception", 5, (s, a, kw, c) => Lines(c, FormatOf(c, a, kw, "format_exception", 4)), "exc", "value", "tb", "limit", "chain")),
                ["print_exception"] = BuiltinFunctionValue.Make("print_exception", Args.Signature("print_exception", 6, (s, a, kw, c) => Print(c, FormatOf(c, a, kw, "print_exception", 5), FileArg(a, kw, 4)), "exc", "value", "tb", "limit", "file", "chain")),
                ["format_exception_only"] = BuiltinFunctionValue.Make("format_exception_only", Args.Signature("format_exception_only", 2, (s, a, kw, c) => Lines(c, FormatOf(c, a, kw, "format_exception_only", -1)), "exc", "value", "show_group")),
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

        // limit by position or name: an int (a bool counts), or None for every frame. CPython hands it to islice or a
        // deque, whose errors these are.
        private static int? LimitArg(EvalContext ctx, ScriptValue[] a, KwArgs kw, int pos)
        {
            ScriptValue v = a.Length > pos ? a[pos] : null;
            ScriptValue kv;
            if (kw.TryGet("limit", out kv))
                v = kv;
            if (v == null || v.Kind == ValueKind.None)
                return null;
            IntValue iv = v as IntValue;
            if (iv != null)
            {
                System.Numerics.BigInteger n = iv.Value;
                return n > int.MaxValue ? int.MaxValue : n < -int.MaxValue ? -int.MaxValue : (int)n;
            }
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return bv.Value ? 1 : 0;
            FloatValue fv = v as FloatValue;
            if (fv != null && fv.Value >= 0)
                throw Raise.ValueError(ctx, "Stop argument for islice() must be None or an integer: 0 <= x <= sys.maxsize.");
            if (fv != null)
                throw Raise.TypeError(ctx, "an integer is required");
            throw Raise.TypeError(ctx, "'>=' not supported between instances of '" + v.PyTypeName + "' and 'int'");
        }

        // The exception being handled, rendered as the host would; "NoneType: None" outside an except.
        // chainPos: where chain sits by position (format_exc(limit, chain), print_exc(limit, file, chain)).
        private static string FormatCurrent(EvalContext ctx, ScriptValue[] a, KwArgs kw, string fn, int chainPos)
        {
            Args.AtMost(ctx, a, fn, chainPos + 1);
            ExceptionValue exc = ctx.CurrentHandledException;
            if (exc == null)
                return "NoneType: None\n";
            return Render(ctx, exc, ChainArg(ctx, a, kw, chainPos), true, LimitArg(ctx, a, kw, 0));
        }

        // chainPos: chain's position (format_exception 4, print_exception 5), or -1 for format_exception_only (no frames).
        private static string FormatOf(EvalContext ctx, ScriptValue[] a, KwArgs kw, string fn, int chainPos)
        {
            bool withFrames = chainPos >= 0;
            ScriptValue v = a.Length >= 1 ? a[0] : null;
            ScriptValue kv;
            if (kw.TryGet("exc", out kv))
                v = kv;
            if (v == null)
                throw Raise.TypeError(ctx, fn + "() missing required argument 'exc'");
            if (a.Length > 1 && !(a[1] is ExceptionValue) && a[1].Kind != ValueKind.None)
                throw Raise.TypeError(ctx, fn + "() takes an exception object; the (type, value, tb) form is not supported in this dialect");
            // format_exception(type, value, tb): the value is what gets rendered, None included; value and tb
            // come together or not at all (format_exception_only has no tb)
            ScriptValue tbv;
            bool valueGiven = a.Length > 1 || kw.TryGet("value", out kv);
            bool tbGiven = a.Length > 2 || kw.TryGet("tb", out tbv);
            if (withFrames && valueGiven != tbGiven)
                throw Raise.ValueError(ctx, "Both or neither of value and tb must be given");
            if (a.Length > 1)
                v = a[1];
            else if (kw.TryGet("value", out kv))
                v = kv;
            if (v.Kind == ValueKind.None)
                return "NoneType: None\n";
            ExceptionValue exc = v as ExceptionValue;
            if (exc == null)
                throw Raise.TypeError(ctx, fn + "() argument must be an exception object, not " + v.PyTypeName);
            return Render(ctx, exc, withFrames && ChainArg(ctx, a, kw, chainPos), withFrames, withFrames ? LimitArg(ctx, a, kw, 3) : null);
        }

        private static string Render(EvalContext ctx, ExceptionValue exc, bool chain, bool withFrames, int? limit)
        {
            string text;
            if (!withFrames)
                text = OnlyLine(ctx, exc);
            else if (chain)
                text = TracebackBuilder.BuildText(exc, exc.GetMessageText(ctx), e => e.GetMessageText(ctx), ctx.Limits.MaxStrChars, limit);
            else
                text = TracebackBuilder.BuildText(Unchained(exc), exc.GetMessageText(ctx), null, ctx.Limits.MaxStrChars, limit);
            ctx.Values.EnsureStrLen(text.Length + 1);   // a text cut at MaxStrChars carries its marker: refused here
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
            if (exc.Notes == null)
                return line;
            // One builder, its size checked as it grows: `line += note` was quadratic in the note count.
            var sb = new System.Text.StringBuilder(line);
            foreach (string note in exc.Notes)
            {
                ctx.Values.CheckStrLen((long)sb.Length + 1 + note.Length);   // the cap only: the line is charged once, as a str
                sb.Append('\n').Append(note);
            }
            return sb.ToString();
        }

        private static readonly string[] ChainSeparators =
        {
            "\nDuring handling of the above exception, another exception occurred:\n\n",
            "\nThe above exception was the direct cause of the following exception:\n\n",
        };

        // format_exception*: the text split into lines, each keeping its newline (CPython's shape).
        private static ScriptValue Lines(EvalContext ctx, string text)
        {
            // Piece by piece, each charged: Split held every line of a 14M-char message at once, uncharged.
            ctx.Budget.ChargeLinear(text.Length);
            ListValue list = ctx.Values.List(4);
            int start = 0;
            while (start < text.Length)
            {
                int nl = text.IndexOf('\n', start);
                int end = nl < 0 ? text.Length : nl + 1;
                // the line between two exceptions of a chain is one item, its blank lines with it, as CPython yields it
                foreach (string sep in ChainSeparators)
                {
                    if (string.CompareOrdinal(text, start, sep, 0, sep.Length) == 0)
                    {
                        end = start + sep.Length;
                        break;
                    }
                }
                list.Add(ctx.Values.Str(text.Substring(start, end - start)), ctx);
                start = end;
            }
            return list;
        }

        // file= by position or name, or null for the run's output.
        private static ScriptValue FileArg(ScriptValue[] a, KwArgs kw, int pos)
        {
            ScriptValue f = a.Length > pos ? a[pos] : null;
            ScriptValue kf;
            if (kw.TryGet("file", out kf))
                f = kf;
            return f == null || f.Kind == ValueKind.None ? null : f;
        }

        private static ScriptValue Print(EvalContext ctx, string text, ScriptValue file)
        {
            if (file != null)
            {
                ctx.CallHook1(PyOps.GetAttr(file, "write", ctx), ctx.Values.Str(text));   // file= was ignored: the text went to the run's output
                return ctx.Values.None;
            }
            Builtins.Emit(ctx, text);   // print()'s route: the host sink, or the buffer of an unhosted run
            return ctx.Values.None;
        }
    }
}
