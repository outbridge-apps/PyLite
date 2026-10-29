using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Outbridge.PyLite.DiffHarness
{
    internal enum DiffClass { Match, Known, Bug, EngineError, IronError }

    internal sealed class EngineOutcome
    {
        public bool Succeeded;
        public bool Skipped;
        public bool EngineFault;
        public string Value;        // the repr of `result` on success
        public string ErrorType;    // PythonType/Kind on failure

        public static EngineOutcome Ok(string v) { return new EngineOutcome { Succeeded = true, Value = v }; }
        public static EngineOutcome Failure(string t, bool engineFault) { return new EngineOutcome { ErrorType = t, EngineFault = engineFault }; }
        public static EngineOutcome AsSkipped() { return new EngineOutcome { Skipped = true }; }
    }

    internal sealed class DiffRow
    {
        public string Name;
        public DiffClass Class;
        public string PyValue, IronValue, Detail;

        public string OneLine()
        {
            string tag = Class == DiffClass.Match ? "  ok  " : Class == DiffClass.Known ? " known"
                : Class == DiffClass.IronError ? " iron?" : Class == DiffClass.EngineError ? " FAULT" : " *BUG*";
            return "[" + tag + "] " + Name + (Class == DiffClass.Match ? "" : "  " + Detail);
        }
    }

    // T-TST-13 comparator: EngineFault dominates; then success/error mismatch; then normalized value diff,
    // filtered through known-differences.json.
    internal static class Comparator
    {
        internal static DiffRow Compare(string name, EngineOutcome py, EngineOutcome ir, KnownDifferences known)
        {
            var row = new DiffRow { Name = name, PyValue = py.Value ?? py.ErrorType, IronValue = ir.Value ?? ir.ErrorType };

            if (py.EngineFault)
                return Set(row, DiffClass.EngineError, "PyLite EngineFault: " + py.ErrorType);
            if (ir.Skipped)
                return Set(row, DiffClass.Match, "");   // single-engine mode: nothing to compare

            if (ir.ErrorType == "IronRunnerError")
                return Set(row, DiffClass.IronError, "iron host error: " + ir.Value);

            if (py.Succeeded != ir.Succeeded)
            {
                if (known.IsKnown(name, "outcome"))
                    return Set(row, DiffClass.Known, "outcome differs (known)");
                return Set(row, DiffClass.Bug, "outcome: pylite " + (py.Succeeded ? "ok" : py.ErrorType)
                    + " vs iron " + (ir.Succeeded ? "ok" : ir.ErrorType));
            }

            if (!py.Succeeded)
            {
                // both failed; error-type parity is best-effort (IronPython type names differ) -> informational
                if (py.ErrorType == ir.ErrorType || known.IsKnown(name, "error-type"))
                    return Set(row, DiffClass.Match, "");
                return Set(row, DiffClass.Known, "error type: " + py.ErrorType + " vs " + ir.ErrorType);
            }

            if (Normalization.ValuesEqual(py.Value, ir.Value))
                return Set(row, DiffClass.Match, "");
            if (known.IsKnown(name, "value"))
                return Set(row, DiffClass.Known, "value differs (known): " + py.Value + " vs " + ir.Value);
            return Set(row, DiffClass.Bug, "value: pylite '" + py.Value + "' vs iron '" + ir.Value + "'");
        }

        private static DiffRow Set(DiffRow row, DiffClass c, string detail)
        {
            row.Class = c;
            row.Detail = detail;
            return row;
        }
    }

    // T-TST-13 normalization: str Ordinal; float re-parse + bitwise; dict/set repr as a set of pairs;
    // list/tuple order-sensitive with per-element float normalization.
    internal static class Normalization
    {
        internal static bool ValuesEqual(string a, string b)
        {
            if (a == null || b == null)
                return a == b;
            a = a.Trim();
            b = b.Trim();
            if (string.Equals(a, b, StringComparison.Ordinal))
                return true;
            if (TryFloat(a, out double fa) && TryFloat(b, out double fb))
                return BitConverter.DoubleToInt64Bits(fa) == BitConverter.DoubleToInt64Bits(fb);
            // IronPython dict repr may differ only in key order -> compare as a set of "k: v" pairs.
            if (IsDict(a) && IsDict(b))
                return SetEqual(SplitPairs(a), SplitPairs(b));
            if (IsSet(a) && IsSet(b))
                return SetEqual(SplitElems(a), SplitElems(b));
            return false;
        }

        private static bool TryFloat(string s, out double d)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }

        private static bool IsDict(string s) { return s.Length >= 2 && s[0] == '{' && s.Contains(":"); }
        private static bool IsSet(string s) { return s.Length >= 2 && s[0] == '{' && !s.Contains(":"); }

        private static bool SetEqual(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
                return false;
            var sa = new HashSet<string>(a.Select(x => x.Trim()));
            return b.All(x => sa.Contains(x.Trim()));
        }

        private static List<string> SplitPairs(string dictRepr) { return SplitTop(dictRepr.Substring(1, dictRepr.Length - 2)); }
        private static List<string> SplitElems(string setRepr) { return SplitTop(setRepr.Substring(1, setRepr.Length - 2)); }

        // Split on top-level commas (ignoring nested () [] {} and quoted strings).
        private static List<string> SplitTop(string s)
        {
            var parts = new List<string>();
            int depth = 0;
            bool inStr = false;
            char quote = '\0';
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (inStr)
                {
                    sb.Append(c);
                    if (c == quote)
                        inStr = false;
                    continue;
                }
                if (c == '\'' || c == '"')
                {
                    inStr = true;
                    quote = c;
                    sb.Append(c);
                    continue;
                }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                if (c == ',' && depth == 0)
                {
                    parts.Add(sb.ToString());
                    sb.Clear();
                }
                else
                    sb.Append(c);
            }
            if (sb.Length > 0)
                parts.Add(sb.ToString());
            return parts;
        }
    }
}
