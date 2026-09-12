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

        public TracebackFrame(string functionName, int line)
        {
            FunctionName = functionName;
            Line = line;
        }
    }

    // Accumulates traceback frames into the ExceptionValue and renders the CPython-style
    // text. The evaluator calls AppendFrame at exactly two points
    // the raise site, and each call-frame boundary (catch -> append -> rethrow with `throw;`).
    internal static class TracebackBuilder
    {
        private const string ScriptFile = "<script>";

        public static void AppendFrame(ExceptionValue exc, string functionName, int line)
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
            exc.TracebackFrames.Add(new TracebackFrame(functionName, line));
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
            var sb = new StringBuilder();
            AppendChain(sb, exc, message, messageOf, 0);
            return sb.ToString();
        }

        private static void AppendChain(StringBuilder sb, ExceptionValue exc, string message, Func<ExceptionValue, string> messageOf, int depth)
        {
            if (depth < 16)
            {
                ExceptionValue prev = exc.Cause ?? (exc.SuppressContext ? null : exc.Context);
                if (prev != null)
                {
                    AppendChain(sb, prev, messageOf == null ? null : messageOf(prev), messageOf, depth + 1);
                    sb.Append("\n\n").Append(exc.Cause != null
                        ? "The above exception was the direct cause of the following exception:"
                        : "During handling of the above exception, another exception occurred:").Append("\n\n");
                }
            }
            AppendOne(sb, exc, message);
        }

        // Frames are accumulated innermost-first, so we walk the list in reverse. An exception that was
        // never raised (a bare `from ValueError()` cause) has no frames and no header, as in CPython.
        private static void AppendOne(StringBuilder sb, ExceptionValue exc, string message)
        {
            if ((exc.TracebackFrames != null && exc.TracebackFrames.Count > 0) || exc.TracebackTruncated)
                sb.Append("Traceback (most recent call last):").Append('\n');
            if (exc.TracebackTruncated)
            {
                sb.Append("  [... ").Append(exc.TracebackDropped.ToString(CultureInfo.InvariantCulture))
                    .Append(" more frames omitted ...]").Append('\n');
            }
            var frames = exc.TracebackFrames;
            if (frames != null)
            {
                for (int i = frames.Count - 1; i >= 0; i--)
                {
                    TracebackFrame f = frames[i];
                    sb.Append("  File \"").Append(ScriptFile).Append("\", line ")
                        .Append(f.Line.ToString(CultureInfo.InvariantCulture))
                        .Append(", in ").Append(f.FunctionName).Append('\n');
                }
            }
            sb.Append(exc.ExcType.Name);
            if (!string.IsNullOrEmpty(message))
            {
                sb.Append(": ").Append(message);
            }
            if (exc.Notes != null)
            {
                foreach (string note in exc.Notes)
                    sb.Append('\n').Append(note);
            }
        }
    }
}
