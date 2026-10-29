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
            d["bit_length"] = SlotDescriptor.MakeMethod("bit_length", Args.Positional("bit_length", 0, 0, (self, a, kw, c) => c.Values.Int(BitLength(self))));
            d["bit_count"] = SlotDescriptor.MakeMethod("bit_count", Args.Positional("bit_count", 0, 0, (self, a, kw, c) => c.Values.Int(BitCount(Val(self)))));
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

        // The value's cached bit length: a bit-at-a-time shift loop is quadratic on a big int.
        private static long BitLength(ScriptValue self)
        {
            IntValue iv = self as IntValue;
            return iv != null ? iv.BitLength() : (((BoolValue)self).Value ? 1 : 0);
        }

        // Popcount of |v| (3.10), one pass over the magnitude's bytes.
        private static long BitCount(BigInteger v)
        {
            if (v >= long.MinValue && v <= long.MaxValue)
            {
                long l = (long)v;
                return PopCount(l < 0 ? (ulong)(-(l + 1)) + 1 : (ulong)l);   // |long.MinValue| fits a ulong
            }
            long n = 0;
            foreach (byte b in BigInteger.Abs(v).ToByteArray())
                n += PopCount(b);
            return n;
        }

        private static int PopCount(ulong x)
        {
            int n = 0;
            while (x != 0)
            {
                x &= x - 1;
                n++;
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
                c.Values.CheckStrLen(lenB > long.MaxValue ? long.MaxValue : (long)lenB);   // the bytes cap, before the buffer
                c.Values.PreCharge(lenB > int.MaxValue ? int.MaxValue : (long)lenB);
                if (lenB > int.MaxValue)   // budget-approved but still not a valid .NET array length
                    throw Raise.Overflow(c, "Python int too large to convert to C ssize_t");
                length = (int)lenB;
            }
            string order = orderArg == null ? "big" : ByteOrder(c, orderArg);
            bool signed = KwBool(c, kw, "signed");

            if (v < 0 && !signed)
                throw Raise.Make(c, PyExceptionTypes.OverflowError, "can't convert negative int to unsigned");
            // In range by bit length, in long arithmetic: unsigned needs bits(v) <= 8*length, signed needs
            // bits(v) or bits(-v-1) <= 8*length-1 (2^(8*length) is never built, and 8*length may pass int).
            long room = 8L * length - (signed ? 1 : 0);
            long need = length == 0 ? (v.IsZero ? 0 : 1) : NumericOps.BitLength(v.Sign < 0 ? -v - 1 : v);
            if (need > Math.Max(room, 0))
                throw Raise.Make(c, PyExceptionTypes.OverflowError, "int too big to convert");

            // The range check above guarantees v fits: its two's-complement bytes, sign-extended to length,
            // in one pass (a byte-at-a-time shift loop was quadratic).
            byte[] raw = v.ToByteArray();
            byte fill = v.Sign < 0 ? (byte)0xFF : (byte)0;
            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
                bytes[i] = i < raw.Length ? raw[i] : fill;
            if (order == "big")
                Array.Reverse(bytes);
            return c.Values.Bytes(bytes);
        }

        // from_bytes(bytes, byteorder='big', *, signed=False) — 3.11 byteorder default, keyword allowed.
        internal static ScriptValue FromBytes(EvalContext c, ScriptValue[] a, KwArgs kw)
        {
            Args.Between(c, a, "from_bytes", 1, 2);
            byte[] data = AsByteSeq(c, a[0]);
            ScriptValue orderArg = a.Length >= 2 ? a[1] : KwGet(kw, "byteorder");
            string order = orderArg == null ? "big" : ByteOrder(c, orderArg);
            bool signed = KwBool(c, kw, "signed");
            // One linear pass: BigInteger reads little-endian two's complement, and a trailing zero byte
            // keeps an unsigned value positive (a byte-at-a-time shift loop was quadratic). Values.Int
            // then applies MaxIntBits.
            c.Budget.ChargeLinear(data.Length);
            byte[] le = new byte[data.Length + (signed ? 0 : 1)];
            if (order == "little")
                Array.Copy(data, le, data.Length);
            else
            {
                for (int i = 0; i < data.Length; i++)
                    le[i] = data[data.Length - 1 - i];
            }
            return c.Values.Int(new BigInteger(le));
        }

        private static byte[] AsByteSeq(EvalContext c, ScriptValue v)
        {
            return Modules.Support.Iter.ToByteArray(c, v);
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
