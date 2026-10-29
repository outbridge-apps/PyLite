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
            if (baseArg != 0 && (baseArg < 2 || baseArg > 36))
                throw Raise.ValueError(ctx, "int() base must be >= 2 and <= 36");

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
            ctx.Budget.Step((int)Math.Min(int.MaxValue, 1 + costDigits * costDigits / 1024));
            ctx.Values.PreCharge(costDigits / 2 + 16);
            if (n > 64)
                ctx.Budget.CheckDeadlineNow();

            // Dual accumulator: one scan with every validation branch shared, but the arithmetic
            // stays in a long until the next multiply-add could overflow, then seeds the exact
            // BigInteger continuation. Short numbers (the overwhelming majority) never touch
            // BigInteger at all.
            const long LongSafe = (long.MaxValue - 35) / 36;   // safe to *base(<=36) + digit(<=35)
            long accL = 0;
            BigInteger acc = BigInteger.Zero;
            long chunk = 0, chunkMul = 1;   // past the long range, digits gather here and fold into acc ~18 at a time (P17)
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
                    throw Invalid(ctx, original, baseArg);
                }
                int v = DigitValue(c);
                if (v < 0 || v >= actualBase)
                    throw Invalid(ctx, original, baseArg);
                if (requireAllZeros && v != 0)
                    throw Invalid(ctx, original, baseArg);
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
                throw Invalid(ctx, original, baseArg);

            while (i < n && PyUnicode.IsPythonSpace(original[i]))
                i++;
            if (i != n)
                throw Invalid(ctx, original, baseArg);

            if (big && chunkMul > 1)
                acc = acc * chunkMul + chunk;
            return big ? sign * acc : sign * (BigInteger)accL;
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
