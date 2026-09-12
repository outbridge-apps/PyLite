using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // struct (corpus-gated): pack/unpack/unpack_from/calcsize + struct.error.
    // Codes: x c b B ? h H i I l L q Q f d s with repeat counts; order chars @ = < > !.
    // DEVIATION: '@' behaves like '=' — standard sizes, no native alignment padding
    // (native mode on x64 Windows differs only by alignment, which scripts here never rely on).
    public static class StructModule
    {
        private struct Item
        {
            public char Code;
            public int Count;
        }

        private sealed class Format
        {
            public bool BigEndian;
            public List<Item> Items;
            public int Size;
            public int ValueCount;   // pack args / unpack results (x consumes none, s is one value)
        }

        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["pack"] = BuiltinFunctionValue.Make("pack", (s, a, kw, c) => Pack(c, a));
            m["unpack"] = BuiltinFunctionValue.Make("unpack", (s, a, kw, c) => Unpack(c, a));
            m["unpack_from"] = BuiltinFunctionValue.Make("unpack_from", UnpackFrom);
            m["calcsize"] = BuiltinFunctionValue.Make("calcsize", (s, a, kw, c) => c.Values.Int(Parsed(c, FmtArg(c, a)).Size));
            m["iter_unpack"] = BuiltinFunctionValue.Make("iter_unpack", (s, a, kw, c) => IterUnpack(c, a));
            m["error"] = ErrorType();
            return ctx.Values.Module("struct", m);
        }

        private static TypeValue ErrorType()
        {
            BuiltinDelegate ctor = (self, args, kw, c) => c.Values.Exception(PyExceptionTypes.StructError, args);
            return TypeValue.Make("error", ctor, null, null, PyExceptionTypes.StructError);
        }

        private static ScriptException Err(EvalContext ctx, string msg)
        {
            return Raise.Make(ctx, PyExceptionTypes.StructError, msg);
        }

        private static string FmtArg(EvalContext ctx, ScriptValue[] a)
        {
            if (a.Length < 1)
                throw Raise.TypeError(ctx, "missing required format argument");
            StrValue s = a[0] as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "format must be str, not " + a[0].PyTypeName);
            return s.Value;
        }

        // ---- format parsing ----

        private static int SizeOf(EvalContext ctx, char code)
        {
            switch (code)
            {
                case 'x': case 'c': case 'b': case 'B': case '?': case 's': return 1;
                case 'h': case 'H': return 2;
                case 'i': case 'I': case 'l': case 'L': case 'f': return 4;
                case 'q': case 'Q': case 'd': return 8;
                default: throw Err(ctx, "bad char in struct format");
            }
        }

        // A parsed format per run, keyed by its text: scripts call pack/unpack in a loop with the same
        // literal, and re-parsing it each time is most of the call. The Format is read-only once built.
        private const int FormatCacheEntries = 256;

        private static Format Parsed(EvalContext ctx, string fmt)
        {
            object o;
            Dictionary<string, Format> cache;
            if (ctx.ModuleState.TryGetValue("struct", out o))
            {
                cache = (Dictionary<string, Format>)o;
            }
            else
            {
                cache = new Dictionary<string, Format>(StringComparer.Ordinal);
                ctx.ModuleState["struct"] = cache;
            }
            Format f;
            if (cache.TryGetValue(fmt, out f))
            {
                ctx.Budget.Step();
                return f;
            }
            f = ParseFormat(ctx, fmt);
            if (cache.Count < FormatCacheEntries)
                cache[fmt] = f;
            return f;
        }

        private static Format ParseFormat(EvalContext ctx, string fmt)
        {
            var f = new Format { BigEndian = false, Items = new List<Item>() };
            int i = 0;
            if (fmt.Length > 0 && (fmt[0] == '@' || fmt[0] == '=' || fmt[0] == '<' || fmt[0] == '>' || fmt[0] == '!'))
            {
                f.BigEndian = fmt[0] == '>' || fmt[0] == '!';
                i = 1;
            }
            while (i < fmt.Length)
            {
                ctx.Budget.Step();
                char c = fmt[i];
                if (c == ' ')
                {
                    i++;
                    continue;
                }
                int count = -1;
                if (c >= '0' && c <= '9')
                {
                    count = 0;
                    while (i < fmt.Length && fmt[i] >= '0' && fmt[i] <= '9')
                    {
                        count = count * 10 + (fmt[i] - '0');
                        if (count > 0x7FFFFF)
                            throw Err(ctx, "repeat count too large");
                        i++;
                    }
                    if (i >= fmt.Length)
                        throw Err(ctx, "repeat count given without format specifier");
                    c = fmt[i];
                }
                i++;
                int n = count < 0 ? 1 : count;
                int size = SizeOf(ctx, c);
                f.Items.Add(new Item { Code = c, Count = n });
                f.Size += size * n;
                if (c == 's')
                    f.ValueCount += 1;             // one bytes value of length n
                else if (c != 'x')
                    f.ValueCount += n;
                ctx.Values.EnsureCollectionSize(f.Size);
            }
            return f;
        }

        // ---- pack ----

        private static ScriptValue Pack(EvalContext ctx, ScriptValue[] a)
        {
            Format f = Parsed(ctx, FmtArg(ctx, a));
            int given = a.Length - 1;
            if (given != f.ValueCount)
                throw Err(ctx, "pack expected " + f.ValueCount + " items for packing (got " + given + ")");
            ctx.Values.PreCharge(24 + f.Size);
            var buf = new byte[f.Size];
            int pos = 0, argIdx = 1;
            foreach (Item item in f.Items)
            {
                if (item.Code == 'x')
                {
                    pos += item.Count;   // zero padding
                    continue;
                }
                if (item.Code == 's')
                {
                    BytesValue bv = a[argIdx++] as BytesValue;
                    if (bv == null)
                        throw Err(ctx, "argument for 's' must be a bytes object");
                    int copy = Math.Min(bv.Data.Length, item.Count);
                    Array.Copy(bv.Data, 0, buf, pos, copy);   // rest stays zero (pad); longer input truncates
                    pos += item.Count;
                    continue;
                }
                for (int r = 0; r < item.Count; r++)
                {
                    ctx.Budget.Step();
                    PackOne(ctx, buf, ref pos, item.Code, a[argIdx++], f.BigEndian);
                }
            }
            return ctx.Values.Bytes(buf);
        }

        private static void PackOne(EvalContext ctx, byte[] buf, ref int pos, char code, ScriptValue v, bool be)
        {
            switch (code)
            {
                case 'c':
                    BytesValue cb = v as BytesValue;
                    if (cb == null || cb.Data.Length != 1)
                        throw Err(ctx, "char format requires a bytes object of length 1");
                    buf[pos++] = cb.Data[0];
                    return;
                case '?':
                    buf[pos++] = (byte)(v.IsTruthy(ctx) ? 1 : 0);
                    return;
                case 'b': PackInt(ctx, buf, ref pos, v, 1, true, be, "byte format requires -128 <= number <= 127"); return;
                case 'B': PackInt(ctx, buf, ref pos, v, 1, false, be, "ubyte format requires 0 <= number <= 255"); return;
                case 'h': PackInt(ctx, buf, ref pos, v, 2, true, be, "short format requires -32768 <= number <= 32767"); return;
                case 'H': PackInt(ctx, buf, ref pos, v, 2, false, be, "ushort format requires 0 <= number <= 65535"); return;
                case 'i': case 'l': PackInt(ctx, buf, ref pos, v, 4, true, be, "argument out of range"); return;
                case 'I': case 'L': PackInt(ctx, buf, ref pos, v, 4, false, be, "argument out of range"); return;
                case 'q': PackInt(ctx, buf, ref pos, v, 8, true, be, "argument out of range"); return;
                case 'Q': PackInt(ctx, buf, ref pos, v, 8, false, be, "argument out of range"); return;
                case 'f':
                {
                    double dv = AsDoubleArg(ctx, v);
                    if (!double.IsInfinity(dv) && !double.IsNaN(dv) && Math.Abs(dv) > float.MaxValue)
                        throw Raise.Overflow(ctx, "float too large to pack with f format");
                    PackBytes(buf, ref pos, BitConverter.GetBytes((float)dv), be);
                    return;
                }
                default: PackBytes(buf, ref pos, BitConverter.GetBytes(AsDoubleArg(ctx, v)), be); return;   // 'd'
            }
        }

        private static double AsDoubleArg(EvalContext ctx, ScriptValue v)
        {
            if (v.Kind == ValueKind.Float)
                return ((FloatValue)v).Value;
            if (v.Kind == ValueKind.Int || v.Kind == ValueKind.Bool)
                return (double)NumericOps.AsBigInteger(v);
            throw Err(ctx, "required argument is not a float");
        }

        private static void PackInt(EvalContext ctx, byte[] buf, ref int pos, ScriptValue v,
            int size, bool signed, bool be, string rangeMsg)
        {
            if (v.Kind != ValueKind.Int && v.Kind != ValueKind.Bool)
                throw Err(ctx, "required argument is not an integer");
            ulong bits;
            IntValue iv = v as IntValue;
            if (iv != null && iv.IsSmall)
            {
                // The common case: the value already is a long, so the range check and the two's
                // complement are integer arithmetic rather than a handful of BigInteger allocations.
                long n = iv.Small;
                if (signed)
                {
                    long lo = size == 8 ? long.MinValue : -(1L << (size * 8 - 1));
                    long hi = size == 8 ? long.MaxValue : (1L << (size * 8 - 1)) - 1;
                    if (n < lo || n > hi)
                        throw Err(ctx, rangeMsg);
                }
                else
                {
                    ulong hi = size == 8 ? ulong.MaxValue : (1UL << (size * 8)) - 1;
                    if (n < 0 || (ulong)n > hi)
                        throw Err(ctx, rangeMsg);
                }
                bits = (ulong)n;
            }
            else
            {
                BigInteger n = NumericOps.AsBigInteger(v);
                BigInteger min = signed ? -(BigInteger.One << (size * 8 - 1)) : BigInteger.Zero;
                BigInteger max = signed ? (BigInteger.One << (size * 8 - 1)) - 1 : (BigInteger.One << (size * 8)) - 1;
                if (n < min || n > max)
                    throw Err(ctx, rangeMsg);
                if (n.Sign < 0)
                    n += BigInteger.One << (size * 8);   // two's complement
                bits = (ulong)n;                          // in range, so it fits 8 bytes
            }
            for (int i = 0; i < size; i++)
            {
                int shift = be ? (size - 1 - i) * 8 : i * 8;
                buf[pos + i] = (byte)(bits >> shift);
            }
            pos += size;
        }

        private static void PackBytes(byte[] buf, ref int pos, byte[] data, bool be)
        {
            if (be == BitConverter.IsLittleEndian)
                Array.Reverse(data);
            Array.Copy(data, 0, buf, pos, data.Length);
            pos += data.Length;
        }

        // ---- unpack ----

        private static ScriptValue Unpack(EvalContext ctx, ScriptValue[] a)
        {
            Format f = Parsed(ctx, FmtArg(ctx, a));
            byte[] data = BufferArg(ctx, a, 1);
            if (data.Length != f.Size)
                throw Err(ctx, "unpack requires a buffer of " + f.Size + " bytes");
            return UnpackAt(ctx, f, data, 0);
        }

        // iter_unpack(fmt, buffer): one tuple per record, unpacked as it is pulled. Lazy as CPython's is,
        // which is what makes reading the first few records of a large buffer cost only those records.
        private static ScriptValue IterUnpack(EvalContext ctx, ScriptValue[] a)
        {
            Format f = Parsed(ctx, FmtArg(ctx, a));
            byte[] data = BufferArg(ctx, a, 1);
            if (f.Size <= 0)
                throw Err(ctx, "cannot iteratively unpack with a struct of length 0");
            if (data.Length % f.Size != 0)
                throw Err(ctx, "iterative unpacking requires a buffer of a multiple of " + f.Size + " bytes");
            return ctx.Values.Iterator(new StructRecordIterator(f, data), "unpack_iterator");
        }

        private sealed class StructRecordIterator : ScriptIteratorBase
        {
            private readonly Format _f;
            private readonly byte[] _data;
            private int _pos;

            internal StructRecordIterator(Format f, byte[] data)
            {
                _f = f;
                _data = data;
            }

            protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
            {
                if (_pos > _data.Length - _f.Size)
                {
                    value = null;
                    return false;
                }
                value = UnpackAt(ctx, _f, _data, _pos);
                _pos += _f.Size;
                return true;
            }
        }

        private static ScriptValue UnpackFrom(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Format f = Parsed(ctx, FmtArg(ctx, a));
            byte[] data = BufferArg(ctx, a, 1);
            int offset = 0;
            ScriptValue ov = a.Length >= 3 ? a[2] : null;
            ScriptValue okw;
            if (kw.TryGet("offset", out okw))
                ov = okw;
            if (ov != null && ov.Kind != ValueKind.None)
                offset = Support.Coerce.ToInt32(ctx, ov, "'" + ov.PyTypeName + "' object cannot be interpreted as an integer");
            if (offset < 0)
                offset += data.Length;
            if (offset < 0 || data.Length - offset < f.Size)
                throw Err(ctx, "unpack_from requires a buffer of at least " + f.Size + " bytes");
            return UnpackAt(ctx, f, data, offset);
        }

        private static byte[] BufferArg(EvalContext ctx, ScriptValue[] a, int i)
        {
            if (a.Length <= i)
                throw Raise.TypeError(ctx, "missing required buffer argument");
            BytesValue b = a[i] as BytesValue;
            if (b == null)
                throw Raise.TypeError(ctx, "a bytes-like object is required, not '" + a[i].PyTypeName + "'");
            return b.Data;
        }

        // The result array is sized from the format, the steps are charged once for the record, and the
        // integer codes read into a long instead of building a BigInteger per field.
        private static ScriptValue UnpackAt(EvalContext ctx, Format f, byte[] data, int pos)
        {
            var results = new ScriptValue[f.ValueCount];
            ctx.Budget.Step(f.ValueCount);
            int at = 0;
            bool be = f.BigEndian;
            List<Item> items = f.Items;
            for (int k = 0; k < items.Count; k++)
            {
                Item item = items[k];
                if (item.Code == 'x')
                {
                    pos += item.Count;
                    continue;
                }
                if (item.Code == 's')
                {
                    var s = new byte[item.Count];
                    Array.Copy(data, pos, s, 0, item.Count);
                    results[at++] = ctx.Values.Bytes(s);
                    pos += item.Count;
                    continue;
                }
                for (int r = 0; r < item.Count; r++)
                    results[at++] = UnpackOne(ctx, data, ref pos, item.Code, be);
            }
            return ctx.Values.Tuple(results);
        }

        private static ScriptValue UnpackOne(EvalContext ctx, byte[] data, ref int pos, char code, bool be)
        {
            switch (code)
            {
                case 'c':
                    var one = new byte[] { data[pos++] };
                    return ctx.Values.Bytes(one);
                case '?':
                    return ctx.Values.Bool(data[pos++] != 0);
                case 'b': return ctx.Values.Int(Signed(ReadBits(data, ref pos, 1, be), 1));
                case 'B': return ctx.Values.Int((long)ReadBits(data, ref pos, 1, be));
                case 'h': return ctx.Values.Int(Signed(ReadBits(data, ref pos, 2, be), 2));
                case 'H': return ctx.Values.Int((long)ReadBits(data, ref pos, 2, be));
                case 'i': case 'l': return ctx.Values.Int(Signed(ReadBits(data, ref pos, 4, be), 4));
                case 'I': case 'L': return ctx.Values.Int((long)ReadBits(data, ref pos, 4, be));
                case 'q': return ctx.Values.Int((long)ReadBits(data, ref pos, 8, be));   // the bits ARE the long
                case 'Q':
                    ulong u = ReadBits(data, ref pos, 8, be);
                    return u <= long.MaxValue ? ctx.Values.Int((long)u) : ctx.Values.Int((BigInteger)u);
                case 'f':
                    byte[] scratch = _floatScratch ?? (_floatScratch = new byte[4]);
                    uint fb = (uint)ReadBits(data, ref pos, 4, be);
                    scratch[0] = (byte)fb;
                    scratch[1] = (byte)(fb >> 8);
                    scratch[2] = (byte)(fb >> 16);
                    scratch[3] = (byte)(fb >> 24);
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(scratch);
                    return ctx.Values.Float(BitConverter.ToSingle(scratch, 0));
                default:
                    return ctx.Values.Float(BitConverter.Int64BitsToDouble((long)ReadBits(data, ref pos, 8, be)));   // 'd'
            }
        }

        [ThreadStatic] private static byte[] _floatScratch;

        // The raw bits of a 1..8 byte field. Every fixed-width code fits a ulong, so no field needs a
        // BigInteger: only an unsigned 8-byte value above long.MaxValue is widened, at the call site.
        private static ulong ReadBits(byte[] data, ref int pos, int size, bool be)
        {
            ulong n = 0;
            if (be)
            {
                for (int i = 0; i < size; i++)
                    n = (n << 8) | data[pos + i];
            }
            else
            {
                for (int i = size - 1; i >= 0; i--)
                    n = (n << 8) | data[pos + i];
            }
            pos += size;
            return n;
        }

        // Sign-extend a field narrower than 8 bytes.
        private static long Signed(ulong bits, int size)
        {
            ulong signBit = 1UL << (size * 8 - 1);
            return (bits & signBit) == 0 ? (long)bits : (long)bits - (long)(signBit << 1);
        }

        private static byte[] ReadRaw(byte[] data, ref int pos, int size, bool be)
        {
            var raw = new byte[size];
            Array.Copy(data, pos, raw, 0, size);
            pos += size;
            if (be == BitConverter.IsLittleEndian)
                Array.Reverse(raw);
            return raw;
        }
    }
}
