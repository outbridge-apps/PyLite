using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // float method slots (P2 gap-closure): is_integer, as_integer_ratio, hex. fromhex is a classmethod
    // (static slot on the float TypeValue, registered in Builtins.Convert). All exact — no decimal detour.
    internal static class FloatMethods
    {
        internal static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            d["is_integer"] = SlotDescriptor.MakeMethod("is_integer", (self, a, kw, c) =>
            {
                double v = ((FloatValue)self).Value;
                return c.Values.Bool(!double.IsNaN(v) && !double.IsInfinity(v) && Math.Floor(v) == v);
            });
            d["as_integer_ratio"] = SlotDescriptor.MakeMethod("as_integer_ratio",
                (self, a, kw, c) => AsIntegerRatio(c, ((FloatValue)self).Value));
            d["hex"] = SlotDescriptor.MakeMethod("hex", (self, a, kw, c) => c.Values.Str(ToHex(((FloatValue)self).Value)));
            // The numeric tower: a float is its own real part, with a float zero imaginary part.
            d["real"] = SlotDescriptor.MakeProperty("real", (self, c) => self);
            d["imag"] = SlotDescriptor.MakeProperty("imag", (self, c) => c.Values.Float(0.0));
            d["conjugate"] = SlotDescriptor.MakeMethod("conjugate", (self, a, kw, c) =>
            {
                Args.None(c, a, kw, "conjugate");
                return self;
            });
            return d;
        }

        // Exact (numerator, denominator) from the IEEE bits; the denominator is always a power of two.
        private static ScriptValue AsIntegerRatio(EvalContext c, double v)
        {
            if (double.IsNaN(v))
                throw Raise.ValueError(c, "cannot convert NaN to integer ratio");
            if (double.IsInfinity(v))
                throw Raise.Overflow(c, "cannot convert Infinity to integer ratio");
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xF_FFFF_FFFF_FFFFL;
            if (exp == 0)
                exp = 1;
            else
                man |= 1L << 52;
            exp -= 1075;
            BigInteger num = man;
            BigInteger den = BigInteger.One;
            if (exp > 0)
                num <<= exp;
            else if (exp < 0)
                den <<= -exp;
            while (!num.IsZero && num.IsEven && den.IsEven)
            {
                num >>= 1;
                den >>= 1;
            }
            if (num.IsZero)
                den = BigInteger.One;
            if (bits < 0)
                num = -num;
            c.Values.PreCharge(32);
            return c.Values.Tuple(new ScriptValue[] { c.Values.Int(num), c.Values.Int(den) });
        }

        // CPython float.hex(): '[-]0x1.<13 hex digits>p<±exp>' (normals), '0x0.0p+0' (zero),
        // '0x0.<13>p-1022' (subnormals), 'inf'/'nan' passthrough.
        private static string ToHex(double v)
        {
            if (double.IsNaN(v))
                return "nan";
            if (double.IsPositiveInfinity(v))
                return "inf";
            if (double.IsNegativeInfinity(v))
                return "-inf";
            long bits = BitConverter.DoubleToInt64Bits(v);
            string sign = bits < 0 ? "-" : "";
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xF_FFFF_FFFF_FFFFL;
            if (exp == 0 && man == 0)
                return sign + "0x0.0p+0";
            string frac = man.ToString("x13", CultureInfo.InvariantCulture);
            if (exp == 0)
                return sign + "0x0." + frac + "p-1022";
            int e = exp - 1023;
            return sign + "0x1." + frac + "p" + (e >= 0 ? "+" : "") + e.ToString(CultureInfo.InvariantCulture);
        }

        // float.fromhex('[sign][0x]hh[.hh][p±dec]' | 'inf' | 'nan') with correct round-half-even.
        internal static ScriptValue FromHex(EvalContext c, ScriptValue[] args)
        {
            StrValue s = args.Length == 1 ? args[0] as StrValue : null;
            if (s == null)
                throw Raise.TypeError(c, "fromhex() argument must be a string");
            string t = s.Value.Trim();
            int i = 0;
            bool neg = false;
            if (i < t.Length && (t[i] == '+' || t[i] == '-'))
            {
                neg = t[i] == '-';
                i++;
            }
            string rest = t.Substring(i);
            if (rest.Equals("inf", StringComparison.OrdinalIgnoreCase) || rest.Equals("infinity", StringComparison.OrdinalIgnoreCase))
                return c.Values.Float(neg ? double.NegativeInfinity : double.PositiveInfinity);
            if (rest.Equals("nan", StringComparison.OrdinalIgnoreCase))
                return c.Values.Float(double.NaN);
            if (i + 1 < t.Length && t[i] == '0' && (t[i + 1] == 'x' || t[i + 1] == 'X'))
                i += 2;

            BigInteger mant = BigInteger.Zero;
            int digits = 0, fracDigits = 0;
            c.Budget.Step();
            while (i < t.Length && IsHex(t[i]))
            {
                if (++digits > 4096)
                    throw Raise.ValueError(c, "invalid hexadecimal floating-point string");
                mant = (mant << 4) + HexVal(t[i]);
                i++;
            }
            if (i < t.Length && t[i] == '.')
            {
                i++;
                while (i < t.Length && IsHex(t[i]))
                {
                    if (++digits > 4096)
                        throw Raise.ValueError(c, "invalid hexadecimal floating-point string");
                    mant = (mant << 4) + HexVal(t[i]);
                    fracDigits++;
                    i++;
                }
            }
            if (digits == 0)
                throw Raise.ValueError(c, "invalid hexadecimal floating-point string");
            int pexp = 0;
            if (i < t.Length && (t[i] == 'p' || t[i] == 'P'))
            {
                i++;
                bool eneg = false;
                if (i < t.Length && (t[i] == '+' || t[i] == '-'))
                {
                    eneg = t[i] == '-';
                    i++;
                }
                int estart = i;
                long acc = 0;
                while (i < t.Length && t[i] >= '0' && t[i] <= '9')
                {
                    acc = acc * 10 + (t[i] - '0');
                    if (acc > 1 << 24)
                        acc = 1 << 24;   // clamp: far past double range either way
                    i++;
                }
                if (i == estart)
                    throw Raise.ValueError(c, "invalid hexadecimal floating-point string");
                pexp = (int)(eneg ? -acc : acc);
            }
            if (i != t.Length)
                throw Raise.ValueError(c, "invalid hexadecimal floating-point string");

            double d = Assemble(c, mant, pexp - 4 * fracDigits);
            return c.Values.Float(neg ? -d : d);
        }

        // value = mant * 2^exp2 rounded half-even into a double; overflow -> OverflowError.
        private static double Assemble(EvalContext c, BigInteger mant, int exp2)
        {
            if (mant.IsZero)
                return 0.0;
            int bitlen = BitLen(mant);
            int e = exp2 + bitlen - 1;                       // floor(log2(value))
            int prec = e >= -1022 ? 53 : e + 1075;           // significant bits available at this scale
            if (prec <= 0)
                return 0.0;                                   // below half the smallest subnormal rounds to 0

            // Round once to `prec` bits (half-even), keeping the exact invariant value = q * 2^t.
            int shift = bitlen - prec;
            long t = (long)exp2 + shift;
            BigInteger q;
            if (shift > 0)
            {
                BigInteger half = BigInteger.One << (shift - 1);
                BigInteger rem = mant & ((BigInteger.One << shift) - 1);
                q = mant >> shift;
                if (rem > half || (rem == half && !(q & BigInteger.One).IsZero))
                    q += 1;                                   // may carry to prec+1 bits; normalized below
            }
            else
            {
                q = mant << -shift;
            }

            // Normalize into the IEEE fields: normals need q in [2^52, 2^53), subnormals t == -1074.
            BigInteger two52 = BigInteger.One << 52;
            BigInteger two53 = BigInteger.One << 53;
            while (q >= two53)
            {
                q >>= 1;                                      // exact: only a carry power-of-two lands here
                t += 1;
            }
            while (q < two52 && t > -1074)
            {
                q <<= 1;
                t -= 1;
            }
            long bits;
            if (q >= two52)
            {
                long biased = t + 1075;
                if (biased > 2046)
                    throw Raise.Overflow(c, "hexadecimal value too large to represent as a float");
                bits = (biased << 52) | (long)(q - two52);
            }
            else
            {
                bits = (long)q;                               // t == -1074: the subnormal field is q itself
            }
            return BitConverter.Int64BitsToDouble(bits);
        }

        private static int BitLen(BigInteger v)
        {
            int n = 0;
            while (v > 0)
            {
                v >>= 1;
                n++;
            }
            return n;
        }

        private static bool IsHex(char ch)
        {
            return (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
        }

        private static int HexVal(char ch)
        {
            if (ch <= '9')
                return ch - '0';
            return (ch | 0x20) - 'a' + 10;
        }
    }
}
