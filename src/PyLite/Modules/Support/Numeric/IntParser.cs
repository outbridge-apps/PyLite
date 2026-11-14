using System;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // int(str, base) — port of Objects/longobject.c::PyLong_FromString.
    internal static class IntParser
    {
        internal static BigInteger Parse(EvalContext ctx, string original, int baseArg)
        {
            BigInteger value;
            ScriptException error = ParseOrError(ctx, original, baseArg, out value);
            if (error != null)
                throw error;
            return value;
        }

        // Parse with its ValueError returned, not thrown (int()'s quiet twin).
        internal static ScriptException ParseOrError(EvalContext ctx, string original, int baseArg, out BigInteger value)
        {
            value = BigInteger.Zero;
            if (baseArg != 0 && (baseArg < 2 || baseArg > 36))
                return Raise.ValueError(ctx, "int() base must be >= 2 and <= 36, or 0");

            int n = original.Length;
            int i = 0;
            while (i < n && PyUnicode.IsPythonSpace(original[i]))
                i++;

            int sign = 1;
            if (i < n && (original[i] == '+' || original[i] == '-'))
            {
                if (original[i] == '-')
                    sign = -1;
                i++;
            }

            int actualBase = baseArg;
            bool requireAllZeros = false;
            if (baseArg == 0)
            {
                if (HasPrefix(original, i, 'x'))
                {
                    actualBase = 16;
                    i += 2;
                }
                else if (HasPrefix(original, i, 'o'))
                {
                    actualBase = 8;
                    i += 2;
                }
                else if (HasPrefix(original, i, 'b'))
                {
                    actualBase = 2;
                    i += 2;
                }
                else if (i < n && original[i] == '0')
                {
                    actualBase = 10;
                    requireAllZeros = true;   // int('077', 0) is illegal; only all-zero is 0
                }
                else
                {
                    actualBase = 10;
                }
            }
            else if (baseArg == 16 && HasPrefix(original, i, 'x'))
            {
                i += 2;
            }
            else if (baseArg == 8 && HasPrefix(original, i, 'o'))
            {
                i += 2;
            }
            else if (baseArg == 2 && HasPrefix(original, i, 'b'))
            {
                i += 2;
            }

            // CPython's int_max_str_digits: past 4300 digits a base that is no power of two has no int
            if ((actualBase & (actualBase - 1)) != 0 && n - i > BigOps.MaxStrDigits)
            {
                ctx.Budget.ChargeLinear(n - i);
                long digits = 0;
                for (int k = i; k < n; k++)
                {
                    int dv = DigitValue(original[k]);
                    if (original[k] == '_')
                        continue;
                    if (dv < 0 || dv >= actualBase)
                        break;
                    digits++;
                }
                if (digits > BigOps.MaxStrDigits)
                    return Raise.ValueError(ctx, "Exceeds the limit (4300 digits) for integer string conversion: value has "
                        + digits.ToString(System.Globalization.CultureInfo.InvariantCulture) + " digits; use sys.set_int_max_str_digits() to increase the limit");
            }

            // A value within MaxIntBits has at most maxDigits significant digits. Longer input is counted
            // first (linear): past maxDigits it is a MaxIntBits refusal before the quadratic accumulation,
            // and leading zeros are not charged as BigInteger work.
            long costDigits = n;
            double log2Base = Math.Log(actualBase, 2);
            long maxDigits = (long)(ctx.Limits.MaxIntBits / log2Base) + 2;
            if (n - i > maxDigits)
            {
                ctx.Budget.ChargeLinear(n - i);
                costDigits = SignificantDigits(original, i, actualBase);
                if (costDigits > maxDigits)
                    throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxIntBits", ctx.Limits.MaxIntBits,
                        (long)((costDigits - 1) * log2Base));
            }
            bool pow2 = (actualBase & (actualBase - 1)) == 0;
            ctx.Budget.Step((int)Math.Min(int.MaxValue, pow2 ? 1 + costDigits / 64 : 1 + costDigits * costDigits / 1024));
            ctx.Values.PreCharge(costDigits / 2 + 16);
            if (n > 64)
                ctx.Budget.CheckDeadlineNow();
            if (pow2 && n - i > 64)
                return ParsePow2(ctx, original, baseArg, i, actualBase, sign, out value);   // linear: bits, not multiplies

            // Dual accumulator: one scan with every validation branch shared, but the arithmetic
            // stays in a long until the next multiply-add could overflow, then seeds the exact
            // BigInteger continuation. Short numbers (the overwhelming majority) never touch
            // BigInteger at all.
            const long LongSafe = (long.MaxValue - 35) / 36;   // safe to *base(<=36) + digit(<=35)
            long accL = 0;
            BigInteger acc = BigInteger.Zero;
            long chunk = 0, chunkMul = 1;   // past the long range, digits gather here and fold into acc ~18 at a time
            bool big = false;
            bool any = false;
            int scanned = 0;
            while (i < n)
            {
                char c = original[i];
                if (PyUnicode.IsPythonSpace(c))
                    break;
                if (c == '_')   // PEP 515: legal only between digits (3.6+; modernized from the 3.4 rejection)
                {
                    int nv = i + 1 < n ? DigitValue(original[i + 1]) : -1;
                    if (any && nv >= 0 && nv < actualBase)
                    {
                        i++;
                        continue;
                    }
                    return Invalid(ctx, original, baseArg);
                }
                int v = DigitValue(c);
                if (v < 0 || v >= actualBase)
                    return Invalid(ctx, original, baseArg);
                if (requireAllZeros && v != 0)
                    return Invalid(ctx, original, baseArg);
                if (big)
                {
                    chunk = chunk * actualBase + v;
                    chunkMul *= actualBase;
                    if (chunkMul > LongSafe)
                    {
                        acc = acc * chunkMul + chunk;
                        chunk = 0;
                        chunkMul = 1;
                    }
                }
                else if (accL <= LongSafe)
                {
                    accL = accL * actualBase + v;
                }
                else
                {
                    big = true;
                    acc = (BigInteger)accL * actualBase + v;
                }
                any = true;
                i++;
                if ((++scanned & 1023) == 0)
                {
                    // The quadratic work was charged up front, so the step cadence alone would not see the
                    // deadline pass during a long parse (300k digits: seconds).
                    ctx.Budget.Step(16);
                    ctx.Budget.CheckDeadlineNow();
                }
            }
            if (!any)
                return Invalid(ctx, original, baseArg);

            while (i < n && PyUnicode.IsPythonSpace(original[i]))
                i++;
            if (i != n)
                return Invalid(ctx, original, baseArg);

            if (big && chunkMul > 1)
                acc = acc * chunkMul + chunk;
            value = big ? sign * acc : sign * (BigInteger)accL;
            return null;
        }

        // A base that is a power of two: each digit is a fixed number of bits, so they are packed straight into the
        // little-endian bytes BigInteger takes (one pass), where the general loop multiplies per chunk (quadratic).
        // The validation is the general loop's: underscores between digits, trailing whitespace only.
        private static ScriptException ParsePow2(EvalContext ctx, string original, int baseArg, int i, int numBase, int sign, out BigInteger value)
        {
            value = BigInteger.Zero;
            int n = original.Length;
            int bits = numBase == 2 ? 1 : numBase == 4 ? 2 : numBase == 8 ? 3 : numBase == 16 ? 4 : 5;
            var digits = new System.Collections.Generic.List<byte>(n - i);
            while (i < n)
            {
                char c = original[i];
                if (PyUnicode.IsPythonSpace(c))
                    break;
                if (c == '_')
                {
                    int nv = i + 1 < n ? DigitValue(original[i + 1]) : -1;
                    if (digits.Count > 0 && nv >= 0 && nv < numBase)
                    {
                        i++;
                        continue;
                    }
                    return Invalid(ctx, original, baseArg);
                }
                int v = DigitValue(c);
                if (v < 0 || v >= numBase)
                    return Invalid(ctx, original, baseArg);
                digits.Add((byte)v);
                i++;
                if ((digits.Count & 4095) == 0)
                    ctx.Budget.CheckDeadlineNow();
            }
            if (digits.Count == 0)
                return Invalid(ctx, original, baseArg);
            while (i < n && PyUnicode.IsPythonSpace(original[i]))
                i++;
            if (i != n)
                return Invalid(ctx, original, baseArg);

            long totalBits = (long)digits.Count * bits;
            var buf = new byte[(totalBits + 7) / 8 + 1];   // the extra zero byte keeps the value non-negative
            long at = 0;
            for (int k = digits.Count - 1; k >= 0; k--, at += bits)
            {
                int d = digits[k];
                int byteAt = (int)(at >> 3), shift = (int)(at & 7);
                buf[byteAt] |= (byte)(d << shift);
                if (shift + bits > 8)
                    buf[byteAt + 1] |= (byte)(d >> (8 - shift));
            }
            BigInteger acc = new BigInteger(buf);
            value = sign < 0 ? -acc : acc;
            return null;
        }

        // Digits of numBase after the leading zeros, up to the first character the loop would not take as one.
        private static long SignificantDigits(string s, int i, int numBase)
        {
            long count = 0;
            for (; i < s.Length; i++)
            {
                if (s[i] == '_')
                    continue;
                int v = DigitValue(s[i]);
                if (v < 0 || v >= numBase)
                    break;
                if (count > 0 || v != 0)
                    count++;
            }
            return count;
        }

        private static bool HasPrefix(string s, int i, char lower)
        {
            return i + 1 < s.Length && s[i] == '0'
                && (s[i + 1] == lower || s[i + 1] == char.ToUpperInvariant(lower));
        }

        private static int DigitValue(char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'a' && c <= 'z')
                return c - 'a' + 10;
            if (c >= 'A' && c <= 'Z')
                return c - 'A' + 10;
            return PyUnicode.DecimalDigitValue(c);   // Nd digits (e.g. Arabic-Indic); -1 otherwise
        }

        private static ScriptException Invalid(EvalContext ctx, string original, int baseArg)
        {
            string repr = ctx.Values.Str(Truncate(original)).Repr(ctx);
            return Raise.ValueError(ctx, "invalid literal for int() with base " + baseArg + ": " + repr);
        }

        private static string Truncate(string s)
        {
            return s.Length <= 200 ? s : s.Substring(0, 200);
        }
    }
}
