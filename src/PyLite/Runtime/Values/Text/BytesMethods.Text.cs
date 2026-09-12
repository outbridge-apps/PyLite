using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // The text-shaped half of the bytes surface: padding, partitioning, line splitting, the ASCII
    // predicates and translate. Every one of them works on bytes as ASCII, which is what CPython does
    // too - a byte over 127 is never a letter, a space or a case pair.
    internal static partial class BytesMethods
    {
        private static void AddTextSlots(Slots s)
        {
            s.Linear("partition", (self, a, kw, c) => Partition(c, B(self), a, false));
            s.Linear("rpartition", (self, a, kw, c) => Partition(c, B(self), a, true));
            s.Linear("splitlines", (self, a, kw, c) => SplitLines(c, B(self), StrMethods.WithKw(c, a, kw, "splitlines", "keepends")));
            s.Linear("center", (self, a, kw, c) => Pad(c, B(self), a, "center", 0));
            s.Linear("ljust", (self, a, kw, c) => Pad(c, B(self), a, "ljust", 1));
            s.Linear("rjust", (self, a, kw, c) => Pad(c, B(self), a, "rjust", 2));
            s.Linear("zfill", (self, a, kw, c) => ZFill(c, B(self), a));
            s.Linear("expandtabs", (self, a, kw, c) => ExpandTabs(c, B(self), StrMethods.WithKw(c, a, kw, "expandtabs", "tabsize")));
            s.Linear("removeprefix", (self, a, kw, c) => RemoveFix(c, B(self), a, false));
            s.Linear("removesuffix", (self, a, kw, c) => RemoveFix(c, B(self), a, true));
            s.Linear("translate", (self, a, kw, c) => Translate(c, B(self), a, kw));
            s.Linear("capitalize", (self, a, kw, c) => Capitalize(c, B(self)));
            s.Linear("title", (self, a, kw, c) => Title(c, B(self)));
            s.Linear("swapcase", (self, a, kw, c) => SwapCase(c, B(self)));
            s.Linear("isascii", (self, a, kw, c) => c.Values.Bool(IsAscii(B(self))));
            s.Linear("isalpha", (self, a, kw, c) => c.Values.Bool(AllOf(B(self), IsAlphaB)));
            s.Linear("isdigit", (self, a, kw, c) => c.Values.Bool(AllOf(B(self), IsDigitB)));
            s.Linear("isalnum", (self, a, kw, c) => c.Values.Bool(AllOf(B(self), IsAlnumB)));
            s.Linear("isspace", (self, a, kw, c) => c.Values.Bool(AllOf(B(self), IsSpace)));
            s.Linear("islower", (self, a, kw, c) => c.Values.Bool(IsCased(B(self), false)));
            s.Linear("isupper", (self, a, kw, c) => c.Values.Bool(IsCased(B(self), true)));
            s.Linear("istitle", (self, a, kw, c) => c.Values.Bool(IsTitle(B(self))));
        }

        // ---- partition ----

        private static ScriptValue Partition(EvalContext c, byte[] data, ScriptValue[] a, bool right)
        {
            string fn = right ? "rpartition" : "partition";
            Args.Exactly(c, a, fn, 1);
            byte[] sep = ArgBytes(c, a, 0, fn);
            if (sep.Length == 0)
                throw Raise.ValueError(c, "empty separator");
            int at = right ? PySearch.LastIndexOf(c, data, sep, 0, data.Length) : PySearch.IndexOf(c, data, sep, 0, data.Length);
            var parts = new ScriptValue[3];
            if (at < 0)
            {
                // A miss puts the whole string first for partition and LAST for rpartition.
                parts[0] = c.Values.Bytes(right ? Array.Empty<byte>() : data);
                parts[1] = c.Values.Bytes(Array.Empty<byte>());
                parts[2] = c.Values.Bytes(right ? data : Array.Empty<byte>());
            }
            else
            {
                parts[0] = c.Values.Bytes(Slice(data, 0, at));
                parts[1] = c.Values.Bytes(sep);
                parts[2] = c.Values.Bytes(Slice(data, at + sep.Length, data.Length));
            }
            return c.Values.Tuple(parts);
        }

        // ---- splitlines ----

        // Universal newlines and nothing else: bytes breaks on \n, \r and \r\n, while str also breaks on
        // the vertical tab, form feed, the file/group/record separators, NEL, LS and PS.
        private static ScriptValue SplitLines(EvalContext c, byte[] data, ScriptValue[] a)
        {
            bool keepends = a.Length >= 1 && a[0].Kind != ValueKind.None && a[0].IsTruthy(c);
            ListValue result = c.Values.List(4);
            int start = 0;
            int i = 0;
            while (i < data.Length)
            {
                byte b = data[i];
                if (b != (byte)'\n' && b != (byte)'\r')
                {
                    i++;
                    continue;
                }
                int endOfLine = i;
                i++;
                if (b == (byte)'\r' && i < data.Length && data[i] == (byte)'\n')
                    i++;
                result.Add(c.Values.Bytes(Slice(data, start, keepends ? i : endOfLine)), c);
                start = i;
            }
            if (start < data.Length)
                result.Add(c.Values.Bytes(Slice(data, start, data.Length)), c);
            return result;
        }

        // ---- padding ----

        private static ScriptValue Pad(EvalContext c, byte[] data, ScriptValue[] a, string fn, int mode)
        {
            Args.Between(c, a, fn, 1, 2);
            int width = WidthArg(c, a[0], fn);
            byte fill = (byte)' ';
            if (a.Length == 2)
            {
                byte[] f = ArgBytes(c, a, 1, fn);
                if (f.Length != 1)
                    throw Raise.TypeError(c, fn + "() argument 2 must be a byte string of length 1, not a bytes object of length " + f.Length);
                fill = f[0];
            }
            if (width <= data.Length)
                return c.Values.Bytes(data);
            c.Values.EnsureStrLen(width);
            var buf = new byte[width];
            int pad = width - data.Length;
            // center's odd byte goes left or right by CPython's own rule, the same one str.center uses.
            int left = mode == 0 ? pad / 2 + (pad & width & 1) : mode == 2 ? pad : 0;
            for (int i = 0; i < width; i++)
                buf[i] = fill;
            Array.Copy(data, 0, buf, left, data.Length);
            return c.Values.Bytes(buf);
        }

        private static ScriptValue ZFill(EvalContext c, byte[] data, ScriptValue[] a)
        {
            Args.Exactly(c, a, "zfill", 1);
            int width = WidthArg(c, a[0], "zfill");
            if (width <= data.Length)
                return c.Values.Bytes(data);
            c.Values.EnsureStrLen(width);
            var buf = new byte[width];
            int pad = width - data.Length;
            int at = 0;
            // A leading sign stays in front of the zeros.
            if (data.Length > 0 && (data[0] == (byte)'+' || data[0] == (byte)'-'))
            {
                buf[0] = data[0];
                at = 1;
            }
            for (int i = at; i < at + pad; i++)
                buf[i] = (byte)'0';
            Array.Copy(data, at, buf, at + pad, data.Length - at);
            return c.Values.Bytes(buf);
        }

        private static ScriptValue ExpandTabs(EvalContext c, byte[] data, ScriptValue[] a)
        {
            int size = a.Length >= 1 && a[0].Kind != ValueKind.None ? WidthArg(c, a[0], "expandtabs") : 8;
            var outB = new List<byte>(data.Length + 8);
            int column = 0;
            foreach (byte b in data)
            {
                if (b == (byte)'\t')
                {
                    int width = size <= 0 ? 0 : size - column % size;
                    c.Values.EnsureStrLen((long)outB.Count + width);   // a huge tabsize would otherwise OOM here
                    for (int i = 0; i < width; i++)
                        outB.Add((byte)' ');
                    column += width;
                }
                else
                {
                    outB.Add(b);
                    column = b == (byte)'\n' || b == (byte)'\r' ? 0 : column + 1;
                }
            }
            c.Values.EnsureStrLen(outB.Count);
            return c.Values.Bytes(outB.ToArray());
        }

        private static ScriptValue RemoveFix(EvalContext c, byte[] data, ScriptValue[] a, bool suffix)
        {
            string fn = suffix ? "removesuffix" : "removeprefix";
            Args.Exactly(c, a, fn, 1);
            byte[] fix = ArgBytes(c, a, 0, fn);
            if (fix.Length == 0 || fix.Length > data.Length)
                return c.Values.Bytes(data);
            int at = suffix ? data.Length - fix.Length : 0;
            for (int i = 0; i < fix.Length; i++)
            {
                if (data[at + i] != fix[i])
                    return c.Values.Bytes(data);
            }
            return c.Values.Bytes(suffix ? Slice(data, 0, at) : Slice(data, fix.Length, data.Length));
        }

        // ---- translate ----

        // translate(table, delete=b''): the table is 256 bytes or None; deletion happens first.
        private static ScriptValue Translate(EvalContext c, byte[] data, ScriptValue[] a, KwArgs kw)
        {
            ScriptValue[] args = StrMethods.WithKw(c, a, kw, "translate", "table", "delete");
            Args.AtLeast(c, args, "translate", 1);
            byte[] table = null;
            if (args[0].Kind != ValueKind.None)
            {
                table = ArgBytes(c, args, 0, "translate");
                if (table.Length != 256)
                    throw Raise.ValueError(c, "translation table must be 256 characters long");
            }
            bool[] drop = null;
            if (args.Length >= 2 && args[1].Kind != ValueKind.None)
            {
                byte[] del = ArgBytes(c, args, 1, "translate");
                drop = new bool[256];
                foreach (byte b in del)
                    drop[b] = true;
            }
            var outB = new List<byte>(data.Length);
            foreach (byte b in data)
            {
                if (drop != null && drop[b])
                    continue;
                outB.Add(table == null ? b : table[b]);
            }
            return c.Values.Bytes(outB.ToArray());
        }

        // bytes.maketrans(frm, to): the identity table with frm's bytes mapped to to's.
        internal static ScriptValue MakeTrans(EvalContext c, ScriptValue[] a)
        {
            Args.Exactly(c, a, "maketrans", 2);
            byte[] frm = ArgBytes(c, a, 0, "maketrans");
            byte[] to = ArgBytes(c, a, 1, "maketrans");
            if (frm.Length != to.Length)
                throw Raise.ValueError(c, "maketrans arguments must have same length");
            var table = new byte[256];
            for (int i = 0; i < 256; i++)
                table[i] = (byte)i;
            for (int i = 0; i < frm.Length; i++)
                table[frm[i]] = to[i];
            return c.Values.Bytes(table);
        }

        // ---- case ----

        private static ScriptValue Capitalize(EvalContext c, byte[] data)
        {
            var buf = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                buf[i] = i == 0 ? ToUpperB(data[i]) : ToLowerB(data[i]);
            return c.Values.Bytes(buf);
        }

        private static ScriptValue Title(EvalContext c, byte[] data)
        {
            var buf = new byte[data.Length];
            bool previousCased = false;
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                buf[i] = previousCased ? ToLowerB(b) : ToUpperB(b);
                previousCased = IsAlphaB(b);
            }
            return c.Values.Bytes(buf);
        }

        private static ScriptValue SwapCase(EvalContext c, byte[] data)
        {
            var buf = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                buf[i] = IsUpperB(b) ? ToLowerB(b) : IsLowerB(b) ? ToUpperB(b) : b;
            }
            return c.Values.Bytes(buf);
        }

        // ---- predicates ----

        private static bool IsAscii(byte[] data)
        {
            foreach (byte b in data)
            {
                if (b > 127)
                    return false;
            }
            return true;   // empty bytes ARE ascii, unlike the other predicates
        }

        private static bool AllOf(byte[] data, Func<byte, bool> pred)
        {
            if (data.Length == 0)
                return false;
            foreach (byte b in data)
            {
                if (!pred(b))
                    return false;
            }
            return true;
        }

        // At least one cased byte, and every cased byte in the asked-for case.
        private static bool IsCased(byte[] data, bool upper)
        {
            bool any = false;
            foreach (byte b in data)
            {
                if (upper ? IsLowerB(b) : IsUpperB(b))
                    return false;
                if (IsAlphaB(b))
                    any = true;
            }
            return any;
        }

        private static bool IsTitle(byte[] data)
        {
            bool any = false;
            bool previousCased = false;
            foreach (byte b in data)
            {
                if (IsUpperB(b))
                {
                    if (previousCased)
                        return false;
                    any = true;
                    previousCased = true;
                }
                else if (IsLowerB(b))
                {
                    if (!previousCased)
                        return false;
                    any = true;
                    previousCased = true;
                }
                else
                    previousCased = false;
            }
            return any;
        }

        private static int WidthArg(EvalContext c, ScriptValue v, string fn)
        {
            System.Numerics.BigInteger n = Coerce.ToIndex(c, v, fn);
            if (n > int.MaxValue)
                return int.MaxValue;
            if (n < int.MinValue)
                return int.MinValue;
            return (int)n;
        }

        private static bool IsUpperB(byte b) { return b >= (byte)'A' && b <= (byte)'Z'; }
        private static bool IsLowerB(byte b) { return b >= (byte)'a' && b <= (byte)'z'; }
        private static bool IsAlphaB(byte b) { return IsUpperB(b) || IsLowerB(b); }
        private static bool IsDigitB(byte b) { return b >= (byte)'0' && b <= (byte)'9'; }
        private static bool IsAlnumB(byte b) { return IsAlphaB(b) || IsDigitB(b); }
        private static byte ToUpperB(byte b) { return IsLowerB(b) ? (byte)(b - 32) : b; }
        private static byte ToLowerB(byte b) { return IsUpperB(b) ? (byte)(b + 32) : b; }
    }
}
