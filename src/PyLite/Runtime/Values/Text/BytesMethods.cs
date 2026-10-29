using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // bytes method slots. Lives with the value model (uses Modules.Support helpers). Search
    // accepts a bytes sub or an int byte; case ops are ASCII only; results allocate via the ValueFactory.
    internal static partial class BytesMethods
    {
        internal static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var s = new Slots(v => ((BytesValue)v).Data.Length);   // every bytes method is at most one pass over the receiver
            s.Linear("decode", (self, a, kw, c) => Decode(c, B(self), StrMethods.WithKw(c, a, kw, "decode", "encoding", "errors")));
            s.Linear("hex", (self, a, kw, c) => Hex(c, B(self), StrMethods.WithKw(c, a, kw, "hex", "sep", "bytes_per_sep")));
            s.Linear("find", 1, 3, (self, a, kw, c) => c.Values.Int(Find(c, B(self), a, false, false)));
            s.Linear("rfind", 1, 3, (self, a, kw, c) => c.Values.Int(Find(c, B(self), a, true, false)));
            s.Linear("index", 1, 3, (self, a, kw, c) => c.Values.Int(Find(c, B(self), a, false, true)));
            s.Linear("rindex", 1, 3, (self, a, kw, c) => c.Values.Int(Find(c, B(self), a, true, true)));
            s.Linear("count", 1, 3, (self, a, kw, c) => c.Values.Int(Count(c, B(self), a)));
            s.Linear("startswith", 1, 3, (self, a, kw, c) => c.Values.Bool(EndsMatch(c, B(self), a, false)));
            s.Linear("endswith", 1, 3, (self, a, kw, c) => c.Values.Bool(EndsMatch(c, B(self), a, true)));
            s.Linear("replace", (self, a, kw, c) => Replace(c, B(self), StrMethods.WithKw(c, a, kw, "replace", "old", "new", "count")));
            s.Linear("split", (self, a, kw, c) => Split(c, B(self), StrMethods.WithKw(c, a, kw, "split", "sep", "maxsplit"), false));
            s.Linear("rsplit", (self, a, kw, c) => Split(c, B(self), StrMethods.WithKw(c, a, kw, "rsplit", "sep", "maxsplit"), true));
            s.Linear("join", 1, 1, (self, a, kw, c) => Join(c, B(self), a));
            s.Linear("strip", 0, 1, (self, a, kw, c) => Strip(c, B(self), a, true, true));
            s.Linear("lstrip", 0, 1, (self, a, kw, c) => Strip(c, B(self), a, true, false));
            s.Linear("rstrip", 0, 1, (self, a, kw, c) => Strip(c, B(self), a, false, true));
            s.Linear("upper", 0, 0, (self, a, kw, c) => MapCase(c, B(self), true));
            s.Linear("lower", 0, 0, (self, a, kw, c) => MapCase(c, B(self), false));
            AddTextSlots(s);
            return s.Table;
        }

        private static byte[] B(ScriptValue self) { return ((BytesValue)self).Data; }

        private static byte[] ArgBytes(EvalContext c, ScriptValue[] a, int i, string what)
        {
            ScriptValue v = i < a.Length ? a[i] : null;
            BytesValue b = v as BytesValue;
            if (b == null)
                throw Raise.TypeError(c, what + ": a bytes-like object is required, not '" + (v == null ? "NoneType" : v.PyTypeName) + "'");
            return b.Data;
        }

        private static ScriptValue Decode(EvalContext c, byte[] data, ScriptValue[] a)
        {
            string enc = a.Length >= 1 && a[0].Kind != ValueKind.None ? BytesCodec.Normalize(c, a[0]) : "utf-8";
            string errors = "strict";
            if (a.Length >= 2)
            {
                StrValue ev = a[1] as StrValue;
                if (ev == null)
                    throw Raise.TypeError(c, "decode() argument 'errors' must be str, not " + a[1].PyTypeName);
                errors = ev.Value;
            }
            return c.Values.Str(BytesCodec.Decode(c, data, enc, errors));
        }

        // hex([sep[, bytes_per_sep]]): a positive group count separates from the right, negative from the left.
        internal static ScriptValue Hex(EvalContext c, byte[] data, ScriptValue[] a)
        {
            string sep = null;
            int per = 1;
            if (a.Length >= 1 && a[0].Kind != ValueKind.None)
            {
                StrValue ss = a[0] as StrValue;
                BytesValue bs = a[0] as BytesValue;
                sep = ss != null ? ss.Value : bs != null ? System.Text.Encoding.ASCII.GetString(bs.Data) : null;
                if (sep == null)
                    throw Raise.TypeError(c, "sep must be str or bytes.");
                if (sep.Length != 1)
                    throw Raise.ValueError(c, "sep must be length 1.");
                if (a.Length >= 2)
                {
                    per = Coerce.ToCInt(c, a[1], "bytes_per_sep");
                }
            }
            var sb = new System.Text.StringBuilder(data.Length * 3);
            int group = per == 0 ? 0 : Math.Abs(per);
            for (int i = 0; i < data.Length; i++)
            {
                if (sep != null && group > 0 && i > 0)
                {
                    bool boundary = per > 0 ? (data.Length - i) % group == 0 : i % group == 0;
                    if (boundary)
                        sb.Append(sep);
                }
                sb.Append(Modules.Support.Hex.Digit(data[i] >> 4));
                sb.Append(Modules.Support.Hex.Digit(data[i] & 0xF));
            }
            return c.Values.Str(sb.ToString());
        }

        // Matches must lie entirely inside [from, to); the walk is PySearch's, so a long needle over a
        // long haystack stops on the deadline instead of running past it.
        private static int IndexOf(EvalContext c, byte[] hay, byte[] needle, int from, int to)
        {
            return PySearch.IndexOf(c, hay, needle, from, to);
        }

        private static int LastIndexOf(EvalContext c, byte[] hay, byte[] needle, int from, int to)
        {
            return PySearch.LastIndexOf(c, hay, needle, from, to);
        }

        // Optional start/end at a[idx], a[idx+1] (None allowed), normalised as str's find/count do: a start
        // past the end stays past it, so an empty needle is not found there.
        private static void Window(EvalContext c, ScriptValue[] a, int idx, int len, out int start, out int end)
        {
            Coerce.AdjustIndices(c, a.Length > idx ? a[idx] : null, a.Length > idx + 1 ? a[idx + 1] : null, len,
                out start, out end);
        }

        private static byte[] SubArg(EvalContext c, ScriptValue[] a)
        {
            ScriptValue v = a.Length >= 1 ? a[0] : null;
            if (v != null && (v.Kind == ValueKind.Int || v.Kind == ValueKind.Bool))
            {
                BigInteger n = NumericOps.AsBigInteger(v);
                if (n < 0 || n > 255)
                    throw Raise.ValueError(c, "byte must be in range(0, 256)");
                return new[] { (byte)n };
            }
            return ArgBytes(c, a, 0, "argument");
        }

        private static int Find(EvalContext c, byte[] data, ScriptValue[] a, bool right, bool raising)
        {
            byte[] sub = SubArg(c, a);
            int start, end;
            Window(c, a, 1, data.Length, out start, out end);
            int r = right ? LastIndexOf(c, data, sub, start, end) : IndexOf(c, data, sub, start, end);
            if (r < 0 && raising)
                throw Raise.ValueError(c, "subsection not found");
            return r;
        }

        private static int Count(EvalContext c, byte[] data, ScriptValue[] a)
        {
            byte[] sub = SubArg(c, a);
            int start, end;
            Window(c, a, 1, data.Length, out start, out end);
            if (sub.Length == 0)
                return start <= end ? end - start + 1 : 0;
            int n = 0, i = start;
            while ((i = IndexOf(c, data, sub, i, end)) >= 0)
            {
                c.Budget.Step();
                n++;
                i += sub.Length;
            }
            return n;
        }

        // startswith/endswith(prefix_or_tuple[, start[, end]]) over the [start, end) window.
        private static bool EndsMatch(EvalContext c, byte[] data, ScriptValue[] a, bool end)
        {
            string fname = end ? "endswith" : "startswith";
            Args.AtLeast(c, a, fname, 1);
            int lo, hi;
            Window(c, a, 1, data.Length, out lo, out hi);
            TupleValue options = a[0] as TupleValue;
            if (options == null)
                return MatchesAt(data, ArgBytes(c, a, 0, fname), lo, hi, end);
            for (int i = 0; i < options.Items.Length; i++)
            {
                BytesValue bv = options.Items[i] as BytesValue;
                if (bv == null)
                    throw Raise.TypeError(c, "a bytes-like object is required, not '" + options.Items[i].PyTypeName + "'");
                if (MatchesAt(data, bv.Data, lo, hi, end))
                    return true;
            }
            return false;
        }

        private static bool MatchesAt(byte[] data, byte[] p, int lo, int hi, bool end)
        {
            if (lo > hi || p.Length > hi - lo)
                return false;
            int off = end ? hi - p.Length : lo;
            for (int i = 0; i < p.Length; i++)
                if (data[off + i] != p[i])
                    return false;
            return true;
        }

        private static ScriptValue Replace(EvalContext c, byte[] data, ScriptValue[] a)
        {
            Args.Between(c, a, "replace", 2, 3);
            byte[] oldB = ArgBytes(c, a, 0, "replace");
            byte[] newB = ArgBytes(c, a, 1, "replace");
            int max = a.Length >= 3 ? Coerce.ToInt32(c, a[2], "replace") : -1;
            // An empty needle matches before every byte and at the end, as in str.replace.
            List<int> positions = null;
            int hits;
            if (oldB.Length == 0)
            {
                hits = max < 0 ? data.Length + 1 : (int)Math.Min(max, data.Length + 1L);
            }
            else
            {
                positions = new List<int>();
                int scan = 0, at;
                while ((max < 0 || positions.Count < max) && (at = PySearch.IndexOf(c, data, oldB, scan, data.Length)) >= 0)
                {
                    c.Budget.Step();
                    positions.Add(at);
                    scan = at + oldB.Length;
                }
                hits = positions.Count;
            }
            if (hits == 0)
                return c.Values.Bytes((byte[])data.Clone());
            long newLen = data.Length + (long)hits * (newB.Length - oldB.Length);
            c.Values.CheckStrLen(newLen);
            c.Budget.ChargeLinear(hits);
            var outB = new byte[newLen];
            int prev = 0, w = 0;
            for (int k = 0; k < hits; k++)
            {
                int p = positions == null ? k : positions[k];
                Buffer.BlockCopy(data, prev, outB, w, p - prev);
                w += p - prev;
                Buffer.BlockCopy(newB, 0, outB, w, newB.Length);
                w += newB.Length;
                prev = p + oldB.Length;
            }
            Buffer.BlockCopy(data, prev, outB, w, data.Length - prev);
            return c.Values.Bytes(outB);
        }

        private static ScriptValue Split(EvalContext c, byte[] data, ScriptValue[] a, bool right)
        {
            ListValue result = c.Values.List(4);
            bool bySep = a.Length >= 1 && a[0].Kind != ValueKind.None;
            int max = a.Length >= 2 && a[1].Kind != ValueKind.None ? Coerce.ToInt32(c, a[1], "split") : -1;
            var parts = new List<byte[]>();
            if (!bySep)
            {
                // whitespace runs (no empty fields); after maxsplit pieces the remainder keeps its inner whitespace
                if (!right)
                {
                    int i = 0;
                    while (i < data.Length)
                    {
                        while (i < data.Length && IsSpace(data[i]))
                            i++;
                        if (i >= data.Length)
                            break;
                        if (max >= 0 && parts.Count == max)
                        {
                            parts.Add(Slice(data, i, data.Length));
                            break;
                        }
                        int start = i;
                        while (i < data.Length && !IsSpace(data[i]))
                            i++;
                        parts.Add(Slice(data, start, i));
                    }
                }
                else
                {
                    int i = data.Length;
                    while (i > 0)
                    {
                        while (i > 0 && IsSpace(data[i - 1]))
                            i--;
                        if (i <= 0)
                            break;
                        if (max >= 0 && parts.Count == max)
                        {
                            parts.Add(Slice(data, 0, i));
                            break;
                        }
                        int stop = i;
                        while (i > 0 && !IsSpace(data[i - 1]))
                            i--;
                        parts.Add(Slice(data, i, stop));
                    }
                    parts.Reverse();
                }
            }
            else
            {
                byte[] sep = ArgBytes(c, a, 0, right ? "rsplit" : "split");
                if (sep.Length == 0)
                    throw Raise.ValueError(c, "empty separator");
                if (!right)
                {
                    int p = 0, idx;
                    while ((max < 0 || parts.Count < max) && (idx = IndexOf(c, data, sep, p, data.Length)) >= 0)
                    {
                        c.Budget.Step();
                        parts.Add(Slice(data, p, idx));
                        p = idx + sep.Length;
                    }
                    parts.Add(Slice(data, p, data.Length));
                }
                else
                {
                    int p = data.Length, idx;
                    while ((max < 0 || parts.Count < max) && (idx = LastIndexOf(c, data, sep, 0, p)) >= 0)
                    {
                        c.Budget.Step();
                        parts.Add(Slice(data, idx + sep.Length, p));
                        p = idx;
                    }
                    parts.Add(Slice(data, 0, p));
                    parts.Reverse();
                }
            }
            for (int k = 0; k < parts.Count; k++)
                result.Add(c.Values.Bytes(parts[k]), c);
            return result;
        }

        private static ScriptValue Join(EvalContext c, byte[] sep, ScriptValue[] a)
        {
            Args.Exactly(c, a, "join", 1);
            var parts = new List<byte[]>();
            long total = 0;
            IScriptIterator it = PyOps.GetIterator(a[0], c);
            ScriptValue e;
            while (it.MoveNext(c, out e))
            {
                BytesValue bv = e as BytesValue;
                if (bv == null)
                    throw Raise.TypeError(c, "sequence item " + parts.Count + ": expected a bytes-like object, " + e.PyTypeName + " found");
                total += (parts.Count > 0 ? sep.Length : 0) + bv.Data.Length;
                c.Values.CheckStrLen(total);
                parts.Add(bv.Data);
            }
            c.Budget.ChargeLinear(parts.Count);
            var outB = new byte[total];
            int w = 0;
            for (int k = 0; k < parts.Count; k++)
            {
                if (k > 0)
                {
                    Buffer.BlockCopy(sep, 0, outB, w, sep.Length);
                    w += sep.Length;
                }
                Buffer.BlockCopy(parts[k], 0, outB, w, parts[k].Length);
                w += parts[k].Length;
            }
            return c.Values.Bytes(outB);
        }

        private static ScriptValue Strip(EvalContext c, byte[] data, ScriptValue[] a, bool left, bool right)
        {
            Func<byte, bool> drop;
            if (a.Length >= 1 && a[0].Kind != ValueKind.None)
            {
                byte[] chars = ArgBytes(c, a, 0, "strip");
                if (chars.Length > 16)
                {
                    var table = new bool[256];   // a long chars argument would make every byte a scan of it
                    foreach (byte ch in chars)
                        table[ch] = true;
                    drop = b => table[b];
                }
                else
                    drop = b => Array.IndexOf(chars, b) >= 0;
            }
            else
            {
                drop = IsSpace;
            }
            int lo = 0, hi = data.Length;
            if (left)
                while (lo < hi && drop(data[lo]))
                    lo++;
            if (right)
                while (hi > lo && drop(data[hi - 1]))
                    hi--;
            return c.Values.Bytes(Slice(data, lo, hi));
        }

        private static ScriptValue MapCase(EvalContext c, byte[] data, bool upper)
        {
            var buf = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if (upper && b >= (byte)'a' && b <= (byte)'z')
                    b -= 32;
                else if (!upper && b >= (byte)'A' && b <= (byte)'Z')
                    b += 32;
                buf[i] = b;
            }
            return c.Values.Bytes(buf);
        }

        private static byte[] Slice(byte[] data, int start, int end)
        {
            int len = end - start;
            if (len <= 0)
                return Array.Empty<byte>();
            var buf = new byte[len];
            Array.Copy(data, start, buf, 0, len);
            return buf;
        }

        private static bool IsSpace(byte b) { return b == (byte)' ' || b == (byte)'\t' || b == (byte)'\n' || b == (byte)'\r' || b == 11 || b == 12; }
    }
}
