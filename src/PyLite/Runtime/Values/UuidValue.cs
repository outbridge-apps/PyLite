using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // uuid.UUID: the single source of truth is byte[16] big-endian (not System.Guid).
    // Kind==Opaque; comparisons and hashing are by the 128-bit int value.
    internal sealed class UuidValue : ScriptValue
    {
        internal const string RESERVED_NCS = "reserved for NCS compatibility";
        internal const string RFC_4122 = "specified in RFC 4122";
        internal const string RESERVED_MICROSOFT = "reserved for Microsoft compatibility";
        internal const string RESERVED_FUTURE = "reserved for future definition";

        private static readonly ScriptTypeInfo UuidType = new ScriptTypeInfo("UUID", BuildSlots());

        private readonly byte[] _bytes;   // exactly 16, big-endian

        internal UuidValue(byte[] bytes16) { _bytes = bytes16; }

        public override ScriptTypeInfo TypeInfo { get { return UuidType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        internal byte[] BytesBE { get { return _bytes; } }

        internal BigInteger IntValue
        {
            get
            {
                BigInteger v = BigInteger.Zero;
                for (int i = 0; i < 16; i++)
                    v = (v << 8) | _bytes[i];
                return v;
            }
        }

        // Swap between BE and LE layouts: reverse bytes of the first three fields (4+2+2), rest unchanged.
        internal static byte[] SwapLe(byte[] src)
        {
            var b = new byte[16];
            b[0] = src[3]; b[1] = src[2]; b[2] = src[1]; b[3] = src[0];
            b[4] = src[5]; b[5] = src[4];
            b[6] = src[7]; b[7] = src[6];
            System.Array.Copy(src, 8, b, 8, 8);
            return b;
        }

        internal static byte[] FromInt(BigInteger v)
        {
            var b = new byte[16];
            for (int i = 15; i >= 0; i--)
            {
                b[i] = (byte)(v & 0xff);
                v >>= 8;
            }
            return b;
        }

        internal string Hex
        {
            get
            {
                var sb = new System.Text.StringBuilder(32);
                for (int i = 0; i < 16; i++)
                    sb.Append(_bytes[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        internal string Canonical
        {
            get
            {
                string h = Hex;
                return h.Substring(0, 8) + "-" + h.Substring(8, 4) + "-" + h.Substring(12, 4)
                    + "-" + h.Substring(16, 4) + "-" + h.Substring(20, 12);
            }
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("UUID('");
            sb.Append(Canonical);
            sb.Append("')");
        }

        protected internal override void StrLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append(Canonical);
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            return NumericHashOfInt(IntValue);
        }

        private static long NumericHashOfInt(BigInteger v)
        {
            // hash(u) == hash(u.int); reuse the int hashing so hashes are stable within the process.
            return unchecked((long)(ulong)(v % ((BigInteger.One << 61) - 1)));
        }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            UuidValue o = other as UuidValue;
            return o != null && IntValue == o.IntValue;
        }

        protected internal override bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        {
            UuidValue o = other as UuidValue;
            if (o == null)
            {
                cmp = 0;
                return false;
            }
            cmp = IntValue.CompareTo(o.IntValue);
            return true;
        }

        // ---- bit fields ----

        private BigInteger Field(int shift, BigInteger mask) { return (IntValue >> shift) & mask; }

        internal string Variant()
        {
            BigInteger i = IntValue;
            if ((i & (new BigInteger(0x8000) << 48)) == 0)
                return RESERVED_NCS;
            if ((i & (new BigInteger(0x4000) << 48)) == 0)
                return RFC_4122;
            if ((i & (new BigInteger(0x2000) << 48)) == 0)
                return RESERVED_MICROSOFT;
            return RESERVED_FUTURE;
        }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var s = new Dictionary<string, SlotDescriptor>(System.StringComparer.Ordinal);
            s["hex"] = SlotDescriptor.MakeProperty("hex", (self, ctx) => ctx.Values.Str(((UuidValue)self).Hex));
            s["int"] = SlotDescriptor.MakeProperty("int", (self, ctx) => ctx.Values.Int(((UuidValue)self).IntValue));
            s["urn"] = SlotDescriptor.MakeProperty("urn", (self, ctx) => ctx.Values.Str("urn:uuid:" + ((UuidValue)self).Canonical));
            s["bytes"] = SlotDescriptor.MakeProperty("bytes", (self, ctx) => ctx.Values.Bytes((byte[])((UuidValue)self)._bytes.Clone()));
            s["bytes_le"] = SlotDescriptor.MakeProperty("bytes_le", (self, ctx) => ctx.Values.Bytes(SwapLe(((UuidValue)self)._bytes)));
            s["time_low"] = SlotDescriptor.MakeProperty("time_low", (self, ctx) => ctx.Values.Int(((UuidValue)self).Field(96, 0xFFFFFFFF)));
            s["time_mid"] = SlotDescriptor.MakeProperty("time_mid", (self, ctx) => ctx.Values.Int(((UuidValue)self).Field(80, 0xFFFF)));
            s["time_hi_version"] = SlotDescriptor.MakeProperty("time_hi_version", (self, ctx) => ctx.Values.Int(((UuidValue)self).Field(64, 0xFFFF)));
            s["clock_seq_hi_variant"] = SlotDescriptor.MakeProperty("clock_seq_hi_variant", (self, ctx) => ctx.Values.Int(((UuidValue)self).Field(56, 0xFF)));
            s["clock_seq_low"] = SlotDescriptor.MakeProperty("clock_seq_low", (self, ctx) => ctx.Values.Int(((UuidValue)self).Field(48, 0xFF)));
            s["node"] = SlotDescriptor.MakeProperty("node", (self, ctx) => ctx.Values.Int(((UuidValue)self).Field(0, new BigInteger(0xFFFFFFFFFFFF))));
            s["clock_seq"] = SlotDescriptor.MakeProperty("clock_seq", (self, ctx) => ctx.Values.Int(ClockSeq((UuidValue)self)));
            s["time"] = SlotDescriptor.MakeProperty("time", (self, ctx) => ctx.Values.Int(Time((UuidValue)self)));
            s["fields"] = SlotDescriptor.MakeProperty("fields", (self, ctx) => Fields((UuidValue)self, ctx));
            s["variant"] = SlotDescriptor.MakeProperty("variant", (self, ctx) => ctx.Values.Str(((UuidValue)self).Variant()));
            s["version"] = SlotDescriptor.MakeProperty("version", (self, ctx) => Version((UuidValue)self, ctx));
            return s;
        }

        private static BigInteger ClockSeq(UuidValue u)
        {
            BigInteger hi = u.Field(56, 0xFF);
            BigInteger low = u.Field(48, 0xFF);
            return ((hi & 0x3f) << 8) | low;
        }

        private static BigInteger Time(UuidValue u)
        {
            BigInteger timeLow = u.Field(96, 0xFFFFFFFF);
            BigInteger timeMid = u.Field(80, 0xFFFF);
            BigInteger timeHi = u.Field(64, 0xFFFF);
            return ((timeHi & 0x0fff) << 48) | (timeMid << 32) | timeLow;
        }

        private static ScriptValue Fields(UuidValue u, EvalContext ctx)
        {
            return ctx.Values.Tuple(new ScriptValue[]
            {
                ctx.Values.Int(u.Field(96, 0xFFFFFFFF)),
                ctx.Values.Int(u.Field(80, 0xFFFF)),
                ctx.Values.Int(u.Field(64, 0xFFFF)),
                ctx.Values.Int(u.Field(56, 0xFF)),
                ctx.Values.Int(u.Field(48, 0xFF)),
                ctx.Values.Int(u.Field(0, new BigInteger(0xFFFFFFFFFFFF))),
            });
        }

        private static ScriptValue Version(UuidValue u, EvalContext ctx)
        {
            if (u.Variant() != RFC_4122)
                return ctx.Values.None;
            return ctx.Values.Int((u.IntValue >> 76) & 0xf);
        }
    }
}
