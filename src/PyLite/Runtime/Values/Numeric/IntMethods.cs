using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // int method slots (to_bytes + bit_length). from_bytes is a classmethod (static slot on the
    // int TypeValue, registered in Builtins.Convert).
    internal static class IntMethods
    {
        internal static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            d["bit_length"] = SlotDescriptor.MakeMethod("bit_length", (self, a, kw, c) => c.Values.Int(BitLength(Val(self))));
            d["bit_count"] = SlotDescriptor.MakeMethod("bit_count", (self, a, kw, c) => c.Values.Int(BitCount(Val(self))));
            d["to_bytes"] = SlotDescriptor.MakeMethod("to_bytes", (self, a, kw, c) => ToBytes(c, Val(self), a, kw));
            d["as_integer_ratio"] = SlotDescriptor.MakeMethod("as_integer_ratio",
                (self, a, kw, c) => c.Values.Tuple(new[] { AsInt(self, c), (ScriptValue)c.Values.Int(BigInteger.One) }));
            // The numeric tower: an int is its own real part and numerator, with no imaginary part.
            d["real"] = SlotDescriptor.MakeProperty("real", (self, c) => AsInt(self, c));
            d["numerator"] = SlotDescriptor.MakeProperty("numerator", (self, c) => AsInt(self, c));
            d["imag"] = SlotDescriptor.MakeProperty("imag", (self, c) => c.Values.Int(BigInteger.Zero));
            d["denominator"] = SlotDescriptor.MakeProperty("denominator", (self, c) => c.Values.Int(BigInteger.One));
            d["conjugate"] = SlotDescriptor.MakeMethod("conjugate", (self, a, kw, c) =>
            {
                Args.None(c, a, kw, "conjugate");
                return AsInt(self, c);
            });
            return d;
        }

        // This table also serves bool, which IS an int in CPython: the value reads as 0 or 1, and an
        // answer that would be "self" comes back as the int, never the bool (True.real is 1, not True).
        private static BigInteger Val(ScriptValue self)
        {
            IntValue iv = self as IntValue;
            return iv != null ? iv.Value
                : ((BoolValue)self).Value ? BigInteger.One : BigInteger.Zero;
        }

        private static ScriptValue AsInt(ScriptValue self, EvalContext c)
        {
            return self is IntValue ? self : c.Values.Int(Val(self));
        }

        private static int BitLength(BigInteger v)
        {
            v = BigInteger.Abs(v);
            int n = 0;
            while (v > 0) { v >>= 1; n++; }
            return n;
        }

        // Popcount of |v| (3.10).
        private static int BitCount(BigInteger v)
        {
            v = BigInteger.Abs(v);
            int n = 0;
            while (v > 0)
            {
                if (!(v & BigInteger.One).IsZero)
                    n++;
                v >>= 1;
            }
            return n;
        }

        // to_bytes(length=1, byteorder='big', *, signed=False) -> bytes (3.11 defaults; both may be
        // keywords). The length is budget-precharged BEFORE the buffer allocation (host-boundary rule).
        internal static ScriptValue ToBytes(EvalContext c, BigInteger v, ScriptValue[] a, KwArgs kw)
        {
            Args.AtMost(c, a, "to_bytes", 2);
            ScriptValue lenArg = a.Length >= 1 ? a[0] : KwGet(kw, "length");
            ScriptValue orderArg = a.Length >= 2 ? a[1] : KwGet(kw, "byteorder");
            int length = 1;
            if (lenArg != null)
            {
                if (lenArg.Kind != ValueKind.Int && lenArg.Kind != ValueKind.Bool)
                    throw Raise.TypeError(c, "'" + lenArg.PyTypeName + "' object cannot be interpreted as an integer");
                BigInteger lenB = NumericOps.AsBigInteger(lenArg);
                if (lenB < 0)
                    throw Raise.ValueError(c, "length argument must be non-negative");
                c.Values.PreCharge(lenB > int.MaxValue ? int.MaxValue : (long)lenB);
                if (lenB > int.MaxValue)   // budget-approved but still not a valid .NET array length
                    throw Raise.Overflow(c, "Python int too large to convert to C ssize_t");
                length = (int)lenB;
            }
            string order = orderArg == null ? "big" : ByteOrder(c, orderArg);
            bool signed = KwBool(c, kw, "signed");

            if (v < 0 && !signed)
                throw Raise.Make(c, PyExceptionTypes.OverflowError, "can't convert negative int to unsigned");
            BigInteger lo = signed && length > 0 ? -(BigInteger.One << (8 * length - 1)) : BigInteger.Zero;
            BigInteger hi = length == 0 ? BigInteger.Zero
                : signed ? (BigInteger.One << (8 * length - 1)) - 1 : (BigInteger.One << (8 * length)) - 1;
            if (v < lo || v > hi)
                throw Raise.Make(c, PyExceptionTypes.OverflowError, "int too big to convert");

            BigInteger work = v < 0 ? v + (BigInteger.One << (8 * length)) : v;
            var big = new byte[length];
            for (int i = length - 1; i >= 0; i--)
            {
                big[i] = (byte)(work & 0xFF);
                work >>= 8;
            }
            if (order == "little")
                Array.Reverse(big);
            return c.Values.Bytes(big);
        }

        // from_bytes(bytes, byteorder='big', *, signed=False) — 3.11 byteorder default, keyword allowed.
        internal static ScriptValue FromBytes(EvalContext c, ScriptValue[] a, KwArgs kw)
        {
            Args.Between(c, a, "from_bytes", 1, 2);
            byte[] data = AsByteSeq(c, a[0]);
            ScriptValue orderArg = a.Length >= 2 ? a[1] : KwGet(kw, "byteorder");
            string order = orderArg == null ? "big" : ByteOrder(c, orderArg);
            bool signed = KwBool(c, kw, "signed");
            byte[] big = (byte[])data.Clone();
            if (order == "little")
                Array.Reverse(big);
            BigInteger v = BigInteger.Zero;
            for (int i = 0; i < big.Length; i++)
                v = (v << 8) | big[i];
            if (signed && big.Length > 0 && (big[0] & 0x80) != 0)
                v -= BigInteger.One << (8 * big.Length);
            return c.Values.Int(v);
        }

        private static byte[] AsByteSeq(EvalContext c, ScriptValue v)
        {
            BytesValue b = v as BytesValue;
            if (b != null)
                return b.Data;
            var list = new List<byte>();
            IScriptIterator it = PyOps.GetIterator(v, c);
            ScriptValue e;
            while (it.MoveNext(c, out e))
            {
                if (e.Kind != ValueKind.Int && e.Kind != ValueKind.Bool)
                    throw Raise.TypeError(c, "'" + e.PyTypeName + "' object cannot be interpreted as an integer");
                BigInteger n = NumericOps.AsBigInteger(e);
                if (n < 0 || n > 255)
                    throw Raise.ValueError(c, "bytes must be in range(0, 256)");
                list.Add((byte)n);
            }
            return list.ToArray();
        }

        private static string ByteOrder(EvalContext c, ScriptValue v)
        {
            StrValue s = v as StrValue;
            if (s == null || (s.Value != "big" && s.Value != "little"))
                throw Raise.ValueError(c, "byteorder must be either 'little' or 'big'");
            return s.Value;
        }

        private static bool KwBool(EvalContext c, KwArgs kw, string name)
        {
            ScriptValue v;
            return kw.TryGet(name, out v) && PyOps.Truth(v, c);
        }

        private static ScriptValue KwGet(KwArgs kw, string name)
        {
            ScriptValue v;
            return kw.TryGet(name, out v) ? v : null;
        }
    }
}
