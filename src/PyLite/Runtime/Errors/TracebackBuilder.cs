using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Outbridge.PyLite.Hosting;

namespace Outbridge.PyLite.Runtime.Errors
{
    // A single traceback frame: struct — two cheap fields.
    public struct TracebackFrame
    {
        public string FunctionName;   // "<module>", "f", "<lambda>", "<genexpr>"
        public int Line;
        internal string File;         // null: the script; else the module's file (ResolvedProgram.ModuleFile)

        public TracebackFrame(string functionName, int line)
        {
            FunctionName = functionName;
            Line = line;
            File = null;
        }
    }

    // Accumulates traceback frames into the ExceptionValue and renders the CPython-style
    // text. The evaluator calls AppendFrame at exactly two points
    // the raise site, and each call-frame boundary (catch -> append -> rethrow with `throw;`).
    internal static class TracebackBuilder
    {
        private const string ScriptFile = "<script>";

        public static void AppendFrame(ExceptionValue exc, string functionName, int line, string file = null)
        {
            if (exc.FrameStampedAtCatch)
            {
                exc.FrameStampedAtCatch = false;   // an except handler already stamped this frame
                return;
            }
            if (exc.TracebackFrames == null)
            {
                exc.TracebackFrames = new List<TracebackFrame>(8);
            }
            if (exc.TracebackFrames.Count >= EngineHardLimits.MaxTracebackFrames)
            {
                exc.TracebackTruncated = true;
                exc.TracebackDropped++;
                return;
            }
            exc.TracebackFrames.Add(new TracebackFrame(functionName, line) { File = file });
        }

        // Full CPython-style block: the chained exceptions first (__cause__, else __context__ unless
        // suppressed by `raise ... from`), each as header, frames (outermost first), "Type: message";
        // messageOf renders a chained exception's message (null => type name only).
        public static string BuildText(ExceptionValue exc, string message)
        {
            return BuildText(exc, message, null);
        }

        public static string BuildText(ExceptionValue exc, string message, Func<ExceptionValue, string> messageOf)
        {
            return BuildText(exc, message, messageOf, int.MaxValue);
        }

        // maxChars: the text stops there with a marker, so long names, huge messages and notes cost at most
        // that much. A chain that comes back to an exception already in it (raise e from e) ends there, as
        // CPython's traceback does.
        public static string BuildText(ExceptionValue exc, string message, Func<ExceptionValue, string> messageOf, int maxChars)
        {
            return BuildText(exc, message, messageOf, maxChars, null);
        }

        // limit: traceback's argument, applied to each exception of the chain: n keeps the n outermost frames, -n the n
        // innermost, 0 none (and no header); null all.
        public static string BuildText(ExceptionValue exc, string message, Func<ExceptionValue, string> messageOf, int maxChars, int? limit)
        {
            var chain = new List<ExceptionValue>(2) { exc };
            for (ExceptionValue cur = exc; chain.Count <= 16; )
            {
                ExceptionValue prev = cur.Cause ?? (cur.SuppressContext ? null : cur.Context);
                if (prev == null || Seen(chain, prev))
                    break;
                chain.Add(prev);
                cur = prev;
            }

            var text = new CappedText(maxChars);
            for (int i = chain.Count - 1; i >= 0 && !text.Full; i--)
            {
                ExceptionValue e = chain[i];
                AppendOne(text, e, i == 0 ? message : (messageOf == null ? null : messageOf(e)), limit);
                if (i > 0)
                    text.Add("\n\n").Add(chain[i - 1].Cause != null
                        ? "The above exception was the direct cause of the following exception:"
                        : "During handling of the above exception, another exception occurred:").Add("\n\n");
            }
            return text.ToString();
        }

        private static bool Seen(List<ExceptionValue> chain, ExceptionValue e)
        {
            foreach (ExceptionValue c in chain)
            {
                if (ReferenceEquals(c, e))
                    return true;
            }
            return false;
        }

        // Frames are accumulated innermost-first, so we walk the list in reverse. An exception that was
        // never raised (a bare `from ValueError()` cause) has no frames and no header, as in CPython.
        private static void AppendOne(CappedText text, ExceptionValue exc, string message, int? limit)
        {
            var frames = exc.TracebackFrames;
            int count = frames == null ? 0 : frames.Count;
            // the shown frames in printed order (outermost first): `shown` of them from `first`
            int first = 0, shown = count;
            if (limit.HasValue)
            {
                shown = (int)Math.Min(count, Math.Abs((long)limit.Value));
                if (limit.Value < 0)
                    first = count - shown;
            }
            // the omitted frames are the outermost: their marker only when the outer end is shown
            bool marker = exc.TracebackTruncated && (!limit.HasValue || limit.Value > 0);
            if (shown > 0 || marker)
                text.Add("Traceback (most recent call last):\n");
            if (marker)
            {
                text.Add("  [... ").Add(exc.TracebackDropped.ToString(CultureInfo.InvariantCulture))
                    .Add(" more frames omitted ...]\n");
            }
            for (int d = first; d < first + shown; d++)
            {
                TracebackFrame f = frames[count - 1 - d];
                text.Add("  File \"").Add(f.File ?? ScriptFile).Add("\", line ")
                    .Add(f.Line.ToString(CultureInfo.InvariantCulture))
                    .Add(", in ").Add(f.FunctionName).Add("\n");
            }
            text.Add(exc.ExcType.Name);
            if (!string.IsNullOrEmpty(message))
                text.Add(": ").Add(message);
            if (exc.Notes != null)
            {
                foreach (string note in exc.Notes)
                    text.Add("\n").Add(note);
            }
        }

        // A StringBuilder that stops at max chars and says so once.
        private sealed class CappedText
        {
            private const string Marker = "\n[... traceback truncated ...]";
            private readonly StringBuilder _sb = new StringBuilder();
            private readonly int _max;
            internal bool Full;

            internal CappedText(int max) { _max = max; }

            internal CappedText Add(string s)
            {
                if (Full)
                    return this;
                if ((long)_sb.Length + s.Length > _max)
                {
                    _sb.Append(s, 0, _max - _sb.Length).Append(Marker);
                    Full = true;
                }
                else
                {
                    _sb.Append(s);
                }
                return this;
            }

            public override string ToString() { return _sb.ToString(); }
        }
    }
}
