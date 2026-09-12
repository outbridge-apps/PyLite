using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // str method slots. Lives with the value model (uses Modules.Support helpers
    // which is the allowed direction) so StrValue.TypeInfo can carry the slot table directly. All matching
    // is Ordinal; indices go through Coerce.AdjustIndices; results allocate via the ValueFactory.
    internal static partial class StrMethods
    {
        internal static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var s = new Slots(v => ((StrValue)v).Value.Length);   // every str method is at most one pass over the receiver
            // search / replace
            s.Linear("find", (self, a, kw, c) => FindImpl(c, S(self), a, false, false));
            s.Linear("rfind", (self, a, kw, c) => FindImpl(c, S(self), a, true, false));
            s.Linear("index", (self, a, kw, c) => FindImpl(c, S(self), a, false, true));
            s.Linear("rindex", (self, a, kw, c) => FindImpl(c, S(self), a, true, true));
            s.Linear("count", (self, a, kw, c) => CountImpl(c, S(self), a));
            s.Linear("startswith", (self, a, kw, c) => TailMatch(c, S(self), a, false));
            s.Linear("endswith", (self, a, kw, c) => TailMatch(c, S(self), a, true));
            s.Linear("replace", (self, a, kw, c) => ReplaceImpl(c, self, a));
            // PEP 616 (3.9)
            s.Linear("removeprefix", (self, a, kw, c) => RemoveAffix(c, self, a, true));
            s.Linear("removesuffix", (self, a, kw, c) => RemoveAffix(c, self, a, false));
            // split / join / strip
            s.Linear("split", (self, a, kw, c) => SplitImpl(c, S(self), WithKw(c, a, kw, "split", "sep", "maxsplit"), false));
            s.Linear("rsplit", (self, a, kw, c) => SplitImpl(c, S(self), WithKw(c, a, kw, "rsplit", "sep", "maxsplit"), true));
            s.Linear("splitlines", (self, a, kw, c) => SplitLines(c, S(self), WithKw(c, a, kw, "splitlines", "keepends")));
            s.Linear("partition", (self, a, kw, c) => Partition(c, S(self), a, false));
            s.Linear("rpartition", (self, a, kw, c) => Partition(c, S(self), a, true));
            s.Linear("join", (self, a, kw, c) => JoinImpl(c, S(self), a));
            s.Linear("strip", (self, a, kw, c) => StripImpl(c, self, a, true, true));
            s.Linear("lstrip", (self, a, kw, c) => StripImpl(c, self, a, true, false));
            s.Linear("rstrip", (self, a, kw, c) => StripImpl(c, self, a, false, true));
            s.Linear("format", (self, a, kw, c) => c.Format.StrDotFormat(c, (StrValue)self, a, kw));
            s.Linear("format_map", (self, a, kw, c) => FormatMap(c, (StrValue)self, a));
            s.Linear("translate", (self, a, kw, c) => Translate(c, S(self), a));
            s.Linear("encode", (self, a, kw, c) =>
            {
                ScriptValue[] ea = WithKw(c, a, kw, "encode", "encoding", "errors");
                bool hasEnc = ea.Length >= 1 && ea[0].Kind != ValueKind.None;
                string errors = ea.Length >= 2 && ea[1] is StrValue ? ((StrValue)ea[1]).Value : "strict";
                return c.Values.Bytes(BytesCodec.Encode(c, S(self), hasEnc ? BytesCodec.Normalize(c, ea[0]) : "utf-8", errors));
            });
            AddCaseSlots(s);
            AddPredicateSlots(s);
            return s.Table;
        }

        // Maps keyword arguments onto the positional slots named in `names` (declaration order), so a
        // method implemented positionally also accepts CPython's keyword spelling (split(maxsplit=1)).
        internal static ScriptValue[] WithKw(EvalContext c, ScriptValue[] a, KwArgs kw, string fname, params string[] names)
        {
            if (kw.Count == 0)
                return a;
            var slots = new ScriptValue[names.Length];
            int filled = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (i >= names.Length)
                    throw Args.AtMostError(c, fname, names.Length, a.Length);
                slots[i] = a[i];
                filled = i + 1;
            }
            for (int j = 0; j < kw.Count; j++)
            {
                string name = kw.NameAt(j);
                int idx = Array.IndexOf(names, name);
                if (idx < 0)
                    throw KwReader.UnexpectedError(c, fname, name);
                if (slots[idx] != null)
                    throw Raise.TypeError(c, fname + "() got multiple values for argument '" + name + "'");
                slots[idx] = kw.ValueAt(j);
                if (idx + 1 > filled)
                    filled = idx + 1;
            }
            var result = new ScriptValue[filled];
            for (int i = 0; i < filled; i++)
                result[i] = slots[i] ?? c.Values.None;   // a skipped earlier slot takes its None default
            return result;
        }

        // format_map(mapping): each field name is looked up as mapping[name] when it is met, so any
        // subscriptable value serves - a dict, a defaultdict with its __missing__, a ChainMap, a Counter.
        private static ScriptValue FormatMap(EvalContext c, StrValue self, ScriptValue[] a)
        {
            Args.Exactly(c, a, "format_map", 1);
            return c.Format.StrFormatMap(c, self, a[0]);
        }

        // translate(table): table maps a code point to str (insert), int (code point) or None (delete);
        // a key the table does not have keeps the character. The walk is by code POINT, so an astral
        // key matches its surrogate pair, and any subscriptable table serves - a plain dict directly,
        // anything else through table[cp] with LookupError meaning "keep".
        private static ScriptValue Translate(EvalContext c, string s, ScriptValue[] a)
        {
            Args.Exactly(c, a, "translate", 1);
            ScriptValue table = a[0];
            DictValue dict = table.GetType() == typeof(DictValue) ? (DictValue)table : null;   // a subclass may have __missing__
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                int width = 1;
                int cp = s[i];
                if (i + 1 < s.Length && char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]))
                {
                    cp = char.ConvertToUtf32(s[i], s[i + 1]);
                    width = 2;
                }
                ScriptValue repl;
                if (!TryMapping(c, table, dict, cp, out repl))
                {
                    sb.Append(s, i, width);
                    i += width - 1;
                    continue;
                }
                i += width - 1;
                if (repl.Kind == ValueKind.None)
                    continue;
                StrValue rs = repl as StrValue;
                if (rs != null)
                {
                    c.Values.EnsureStrLen(sb.Length + (long)rs.Value.Length);
                    sb.Append(rs.Value);
                    continue;
                }
                if (repl.Kind == ValueKind.Int || repl.Kind == ValueKind.Bool)
                {
                    System.Numerics.BigInteger target = NumericOps.AsBigInteger(repl);
                    if (target < 0 || target > 0x10FFFF)
                        throw Raise.ValueError(c, "character mapping must be in range(0x110000)");
                    int code = (int)target;
                    sb.Append(code <= 0xFFFF ? ((char)code).ToString() : char.ConvertFromUtf32(code));
                    continue;
                }
                throw Raise.TypeError(c, "character mapping must return integer, None or str");
            }
            return c.Values.StrPrecharged(sb.ToString());
        }

        private static bool TryMapping(EvalContext c, ScriptValue table, DictValue dict, int cp, out ScriptValue repl)
        {
            ScriptValue key = c.Values.Int(cp);
            if (dict != null)
                return dict.TryGet(key, c, out repl);
            try
            {
                repl = PyOps.GetItem(table, key, c);
                return true;
            }
            catch (ScriptException ex) when (ex.Value.ExcType.IsSubtypeOf(PyExceptionTypes.LookupError))
            {
                repl = null;
                return false;
            }
        }

        // PEP 616: returns self unchanged (same instance) when the affix does not match.
        private static ScriptValue RemoveAffix(EvalContext c, ScriptValue self, ScriptValue[] a, bool prefix)
        {
            string name = prefix ? "removeprefix" : "removesuffix";
            Args.Exactly(c, a, name, 1);
            StrValue affix = a[0] as StrValue;
            if (affix == null)
                throw Raise.TypeError(c, name + "() argument must be str, not " + a[0].PyTypeName);
            string s = S(self);
            string p = affix.Value;
            if (p.Length == 0 || p.Length > s.Length)
                return self;
            if (prefix)
                return s.StartsWith(p, StringComparison.Ordinal) ? c.Values.Str(s.Substring(p.Length)) : self;
            return s.EndsWith(p, StringComparison.Ordinal) ? c.Values.Str(s.Substring(0, s.Length - p.Length)) : self;
        }

        internal static string S(ScriptValue self)
        {
            return ((StrValue)self).Value;
        }

        // A str argument or the TypeError (wording advisory). Substring search itself is
        // PySearch's, so a long needle over a long haystack stops on the deadline.
        internal static string ArgStr(EvalContext c, ScriptValue[] a, int i)
        {
            ScriptValue v = i < a.Length ? a[i] : null;
            StrValue sv = v as StrValue;
            if (sv == null)
                throw Raise.TypeError(c, "must be str, not " + (v == null ? "NoneType" : v.PyTypeName));
            return sv.Value;
        }

        private static ScriptValue ArgAt(ScriptValue[] a, int i)
        {
            return i < a.Length ? a[i] : null;
        }

        private static int ArgInt(EvalContext c, ScriptValue[] a, int i, int dflt)
        {
            if (i >= a.Length)
                return dflt;
            System.Numerics.BigInteger b = Coerce.ToIndex(c, a[i], "str method");
            if (b > int.MaxValue)
                return int.MaxValue;
            if (b < int.MinValue)
                return int.MinValue;
            return (int)b;
        }

        // ---- find / rfind / index / rindex ----

        private static ScriptValue FindImpl(EvalContext c, string s, ScriptValue[] a, bool right, bool isIndex)
        {
            string sub = ArgStr(c, a, 0);
            int start, end;
            Coerce.AdjustIndices(c, ArgAt(a, 1), ArgAt(a, 2), s.Length, out start, out end);
            c.Budget.Step(1 + (end - start < 0 ? 0 : end - start) / 64);
            int pos;
            if (sub.Length == 0)
            {
                if (right)
                    pos = end;
                else
                    pos = start <= s.Length && start <= end ? start : -1;
            }
            else if (start > end || end - start < sub.Length)
            {
                pos = -1;
            }
            else if (!right)
            {
                pos = PySearch.IndexOf(c, s, sub, start, end - start);
            }
            else
            {
                pos = PySearch.LastIndexOf(c, s, sub, start, end);
            }
            if (isIndex && pos < 0)
                throw Raise.ValueError(c, "substring not found");
            return c.Values.Int(pos);
        }

        private static ScriptValue CountImpl(EvalContext c, string s, ScriptValue[] a)
        {
            string sub = ArgStr(c, a, 0);
            int start, end;
            Coerce.AdjustIndices(c, ArgAt(a, 1), ArgAt(a, 2), s.Length, out start, out end);
            if (start > end)
                return c.Values.Int(0);
            if (sub.Length == 0)
                return c.Values.Int(end - start + 1);
            int count = 0;
            int i = start;
            while (i <= end - sub.Length)
            {
                int at = PySearch.IndexOf(c, s, sub, i, end - i);
                if (at < 0)
                    break;
                c.Budget.Step();
                count++;
                i = at + sub.Length;
            }
            return c.Values.Int(count);
        }

        // ---- startswith / endswith ----

        private static ScriptValue TailMatch(EvalContext c, string s, ScriptValue[] a, bool end)
        {
            int start, stop;
            Coerce.AdjustIndices(c, ArgAt(a, 1), ArgAt(a, 2), s.Length, out start, out stop);
            ScriptValue arg = a.Length > 0 ? a[0] : null;
            TupleValue t = arg as TupleValue;
            if (t != null)
            {
                for (int i = 0; i < t.Items.Length; i++)
                {
                    StrValue sv = t.Items[i] as StrValue;
                    if (sv == null)
                        throw Raise.TypeError(c, "tuple for " + (end ? "endswith" : "startswith") + " must only contain str, not " + t.Items[i].PyTypeName);
                    if (One(s, sv.Value, start, stop, end))
                        return c.Values.Bool(true);
                }
                return c.Values.Bool(false);
            }
            StrValue prefix = arg as StrValue;
            if (prefix == null)
                throw Raise.TypeError(c, (end ? "endswith" : "startswith") + " first arg must be str or a tuple of str, not " + (arg == null ? "NoneType" : arg.PyTypeName));
            return c.Values.Bool(One(s, prefix.Value, start, stop, end));
        }

        // Port of stringlib::tailmatch: outside the range or too long -> false; empty affix -> true.
        private static bool One(string s, string affix, int start, int stop, bool end)
        {
            if (stop - start < affix.Length || start > s.Length)
                return false;
            int at = end ? stop - affix.Length : start;
            if (at < start)
                return false;
            return string.CompareOrdinal(s, at, affix, 0, affix.Length) == 0;
        }

        // ---- replace ----

        private static ScriptValue ReplaceImpl(EvalContext c, ScriptValue selfVal, ScriptValue[] a)
        {
            string s = S(selfVal);
            string oldS = ArgStr(c, a, 0);
            string newS = ArgStr(c, a, 1);
            int count = ArgInt(c, a, 2, -1);
            if (count == 0 || (oldS == newS))
                return selfVal;

            if (oldS.Length == 0)
            {
                var sb = new StringBuilder();
                int inserted = 0;
                for (int i = 0; i <= s.Length; i++)
                {
                    if (count < 0 || inserted < count)
                    {
                        sb.Append(newS);
                        inserted++;
                    }
                    if (i < s.Length)
                        sb.Append(s[i]);
                }
                long est0 = sb.Length;
                if (est0 > c.Limits.MaxStrChars)
                    throw c.Budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", c.Limits.MaxStrChars, est0);
                return c.Values.Str(sb.ToString());
            }

            var positions = new List<int>();
            int scan = 0;
            while (count < 0 || positions.Count < count)
            {
                int at = PySearch.IndexOf(c, s, oldS, scan, s.Length - scan);
                if (at < 0)
                    break;
                c.Budget.Step();
                positions.Add(at);
                scan = at + oldS.Length;
            }
            if (positions.Count == 0)
                return selfVal;

            long newLen = (long)s.Length + (long)positions.Count * (newS.Length - oldS.Length);
            if (newLen > c.Limits.MaxStrChars)
                throw c.Budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", c.Limits.MaxStrChars, newLen);
            c.Values.PreCharge(2 * newLen + 24);
            var outSb = new StringBuilder((int)newLen);
            int prev = 0;
            for (int k = 0; k < positions.Count; k++)
            {
                outSb.Append(s, prev, positions[k] - prev);
                outSb.Append(newS);
                prev = positions[k] + oldS.Length;
            }
            outSb.Append(s, prev, s.Length - prev);
            return c.Values.StrPrecharged(outSb.ToString());
        }

        // ---- split / rsplit ----

        private static ScriptValue SplitImpl(EvalContext c, string s, ScriptValue[] a, bool right)
        {
            ScriptValue sepArg = ArgAt(a, 0);
            int maxsplit = ArgInt(c, a, 1, -1);
            ListValue result = c.Values.List(4);
            if (sepArg == null || sepArg.Kind == ValueKind.None)
            {
                SplitWhitespace(c, s, maxsplit, right, result);
                return result;
            }
            string sep = ArgStr(c, a, 0);
            if (sep.Length == 0)
                throw Raise.ValueError(c, "empty separator");
            if (!right)
            {
                // Forward split writes each piece straight into the result — no intermediate List<string>
                // and no second wrap pass. Still one Substring + StrValue + charged Add per piece, so the
                // incremental Budget/MaxCollectionItems guards are unchanged (a huge input still aborts).
                SplitBySep(c, s, sep, maxsplit, result);
                return result;
            }
            var parts = new List<string>();
            RSplitBySep(c, s, sep, maxsplit, parts);
            for (int i = 0; i < parts.Count; i++)
                result.Add(c.Values.Str(parts[i]), c);
            return result;
        }

        private static void SplitBySep(EvalContext c, string s, string sep, int maxsplit, ListValue result)
        {
            int start = 0, splits = 0;
            if (sep.Length == 1)
            {
                // Single-char separator (the common case): the char IndexOf overload is a tight scan,
                // skipping the ordinal-comparison setup the string overload does on every call. Left as
                // is on purpose: a counting pass to presize the list, and the one-charge-per-piece form
                // the whitespace split uses, both measured no better here (the two-piece 'key=value'
                // split that dominates real scripts came out slightly worse).
                char ch = sep[0];
                while (maxsplit < 0 || splits < maxsplit)
                {
                    int at = s.IndexOf(ch, start);
                    if (at < 0)
                        break;
                    c.Budget.Step();
                    result.Add(c.Values.Str(s.Substring(start, at - start)), c);
                    start = at + 1;
                    splits++;
                }
                result.Add(c.Values.Str(s.Substring(start)), c);
                return;
            }
            while (maxsplit < 0 || splits < maxsplit)
            {
                int at = PySearch.IndexOf(c, s, sep, start, s.Length - start);
                if (at < 0)
                    break;
                c.Budget.Step();
                result.Add(c.Values.Str(s.Substring(start, at - start)), c);
                start = at + sep.Length;
                splits++;
            }
            result.Add(c.Values.Str(s.Substring(start)), c);
        }

        // One piece's budget in one call — the list slot, the StrValue header and its characters — and
        // the MaxCollectionItems cap; a piece of an admitted string cannot exceed MaxStrChars, so
        // AddPiece builds the StrValue directly instead of through the factory's check.
        private static void ChargePiece(EvalContext c, ListValue result, int len)
        {
            if (result.Items.Count >= c.Limits.MaxCollectionItems)
                SequenceOps.CheckCap((long)result.Items.Count + 1, c);
            c.Budget.Step();
            c.Budget.ChargeAllocation(32L + 2L * len);
        }

        private static void AddPiece(ListValue result, string s, int start, int len)
        {
            result.Items.Add(len == 0 ? StrValue.Empty : new StrValue(s.Substring(start, len)));
        }

        private static void RSplitBySep(EvalContext c, string s, string sep, int maxsplit, List<string> parts)
        {
            int end = s.Length, splits = 0;
            while (maxsplit < 0 || splits < maxsplit)
            {
                int at = PySearch.LastIndexOf(c, s, sep, 0, end);
                if (at < 0 || end == 0)
                    break;
                c.Budget.Step();
                parts.Add(s.Substring(at + sep.Length, end - (at + sep.Length)));
                end = at;
                splits++;
            }
            parts.Add(s.Substring(0, end));
            parts.Reverse();
        }

        private static void SplitWhitespace(EvalContext c, string s, int maxsplit, bool right, ListValue result)
        {
            if (!right)
            {
                // Forward: the pieces go straight into the result. Their count is not known up front,
                // so each is charged as it is added; there is no second pass through a List<string>.
                int i = 0, splits = 0;
                while (i < s.Length)
                {
                    while (i < s.Length && PyUnicode.IsPythonSpace(s[i]))
                        i++;
                    if (i >= s.Length)
                        break;
                    if (maxsplit >= 0 && splits >= maxsplit)
                    {
                        ChargePiece(c, result, s.Length - i);   // remainder incl. trailing spaces
                        AddPiece(result, s, i, s.Length - i);
                        i = s.Length;
                        break;
                    }
                    int startTok = i;
                    while (i < s.Length && !PyUnicode.IsPythonSpace(s[i]))
                        i++;
                    ChargePiece(c, result, i - startTok);
                    AddPiece(result, s, startTok, i - startTok);
                    splits++;
                }
                return;
            }
            // rsplit: the pieces are found from the right and reversed at the end, so a List<string>
            // stays for that; its own block keeps the loop variables apart from the forward branch's.
            var parts = new List<string>();
            {
                int i = s.Length - 1, splits = 0;
                while (i >= 0)
                {
                    while (i >= 0 && PyUnicode.IsPythonSpace(s[i]))
                        i--;
                    if (i < 0)
                        break;
                    if (maxsplit >= 0 && splits >= maxsplit)
                    {
                        parts.Add(s.Substring(0, i + 1));
                        i = -1;
                        break;
                    }
                    int endTok = i;
                    while (i >= 0 && !PyUnicode.IsPythonSpace(s[i]))
                        i--;
                    c.Budget.Step();
                    parts.Add(s.Substring(i + 1, endTok - i));
                    splits++;
                }
                parts.Reverse();
            }
            for (int k = 0; k < parts.Count; k++)
                result.Add(c.Values.Str(parts[k]), c);
        }

        // ---- splitlines ----

        private static ScriptValue SplitLines(EvalContext c, string s, ScriptValue[] a)
        {
            bool keepends = a.Length > 0 && a[0].IsTruthy(c);
            ListValue result = c.Values.List(4);
            int i = 0;
            while (i < s.Length)
            {
                int start = i;
                while (i < s.Length && !PyUnicode.IsLineBreak(s[i]))
                    i++;
                int eol = i;
                if (i < s.Length)
                {
                    if (s[i] == '\r' && i + 1 < s.Length && s[i + 1] == '\n')
                        i += 2;
                    else
                        i += 1;
                }
                c.Budget.Step();
                int len = keepends ? i - start : eol - start;
                result.Add(c.Values.Str(s.Substring(start, len)), c);
            }
            return result;
        }

        // ---- partition / rpartition ----

        private static ScriptValue Partition(EvalContext c, string s, ScriptValue[] a, bool right)
        {
            string sep = ArgStr(c, a, 0);
            if (sep.Length == 0)
                throw Raise.ValueError(c, "empty separator");
            int at = right ? PySearch.LastIndexOf(c, s, sep, 0, s.Length) : PySearch.IndexOf(c, s, sep, 0, s.Length);
            if (at < 0)
            {
                ScriptValue empty = c.Values.Str("");
                return right
                    ? c.Values.Tuple(new[] { empty, empty, c.Values.Str(s) })
                    : c.Values.Tuple(new[] { c.Values.Str(s), empty, empty });
            }
            return c.Values.Tuple(new ScriptValue[]
            {
                c.Values.Str(s.Substring(0, at)),
                c.Values.Str(sep),
                c.Values.Str(s.Substring(at + sep.Length))
            });
        }

        // ---- join ----

        private static ScriptValue JoinImpl(EvalContext c, string sep, ScriptValue[] a)
        {
            Args.Exactly(c, a, "join", 1);
            IScriptIterator it = PyOps.TryGetIterator(a[0], c);
            if (it == null)
                throw Raise.TypeError(c, "can only join an iterable");
            var parts = new List<string>();
            ScriptValue v;
            long total = 0;
            while (it.MoveNext(c, out v))
            {
                StrValue sv = v as StrValue;
                if (sv == null)
                    throw Raise.TypeError(c, "sequence item " + parts.Count + ": expected str instance, " + v.PyTypeName + " found");
                c.Budget.Step();
                parts.Add(sv.Value);
                total += sv.Value.Length;
            }
            if (parts.Count == 0)
                return c.Values.Str("");
            if (parts.Count == 1)
                return c.Values.Str(parts[0]);
            total += (long)sep.Length * (parts.Count - 1);
            if (total > c.Limits.MaxStrChars)
                throw c.Budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", c.Limits.MaxStrChars, total);
            c.Values.PreCharge(2 * total + 24);
            var sb = new StringBuilder((int)total);
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                    sb.Append(sep);
                sb.Append(parts[i]);
            }
            return c.Values.StrPrecharged(sb.ToString());
        }

        // ---- strip / lstrip / rstrip ----

        private static ScriptValue StripImpl(EvalContext c, ScriptValue selfVal, ScriptValue[] a, bool left, bool right)
        {
            string s = S(selfVal);
            string chars = null;
            if (a.Length > 0 && a[0].Kind != ValueKind.None)
            {
                StrValue sv = a[0] as StrValue;
                if (sv == null)
                    throw Raise.TypeError(c, "strip arg must be None or str");
                chars = sv.Value;
            }
            // a long chars argument would make every character a scan of it: past a few, a set
            HashSet<char> set = chars != null && chars.Length > 16 ? new HashSet<char>(chars) : null;
            int start = 0, end = s.Length;
            if (left)
            {
                while (start < end && IsStripChar(s[start], chars, set))
                    start++;
            }
            if (right)
            {
                while (end > start && IsStripChar(s[end - 1], chars, set))
                    end--;
            }
            if (start == 0 && end == s.Length)
                return selfVal;
            if (end <= start)
                return c.Values.Str("");
            return c.Values.Str(s.Substring(start, end - start));
        }

        private static bool IsStripChar(char ch, string chars, HashSet<char> set)
        {
            if (chars == null)
                return PyUnicode.IsPythonSpace(ch);
            return set != null ? set.Contains(ch) : chars.IndexOf(ch) >= 0;
        }
    }
}
