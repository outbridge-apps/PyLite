using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Modules.Support
{
    // The single point where a double is rendered. Repr uses a
    // shortest round-tripping G-ladder; the fixed/scientific/general renderers and round() use an EXACT
    // decimal decomposition of the double (BigInteger) so half-even rounding matches CPython.
    public static class FloatRepr
    {
        public static bool IsNegativeZero(double d)
        {
            return d == 0.0 && BitConverter.DoubleToInt64Bits(d) != 0L;
        }

        private static bool IsNegative(double d)
        {
            return BitConverter.DoubleToInt64Bits(d) < 0L;
        }

        public static string Repr(double d)
        {
            if (double.IsNaN(d))
                return "nan";
            if (double.IsPositiveInfinity(d))
                return "inf";
            if (double.IsNegativeInfinity(d))
                return "-inf";
            if (d == 0.0)
                return IsNegativeZero(d) ? "-0.0" : "0.0";

            // Shortest scientific string that round-trips: smallest significant-digit count.
            long targetBits = BitConverter.DoubleToInt64Bits(d);
            string sci = null;
            for (int prec = 1; prec <= 17; prec++)
            {
                string cand = d.ToString("E" + (prec - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                double parsed;
                try
                {
                    parsed = double.Parse(cand, CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                    continue;
                } // low-precision round-up overflowed; try more digits

                if (BitConverter.DoubleToInt64Bits(parsed) == targetBits)
                {
                    sci = cand;
                    break;
                }
            }
            if (sci == null)
                sci = d.ToString("E16", CultureInfo.InvariantCulture);

            // Decompose: sign, significant digits, exponent of the leading digit.
            bool neg = sci[0] == '-';
            int eIdx = sci.IndexOf('E');
            string mant = sci.Substring(neg ? 1 : 0, eIdx - (neg ? 1 : 0));
            int decExp = int.Parse(sci.Substring(eIdx + 1), CultureInfo.InvariantCulture);
            string digits = mant.Replace(".", "");

            string body = (decExp < -4 || decExp >= 16)
                ? FormatExponential(digits, decExp)
                : FormatPositional(digits, decExp);
            return neg ? "-" + body : body;
        }

        private static string FormatExponential(string digits, int decExp)
        {
            var sb = new StringBuilder();
            sb.Append(digits[0]);
            if (digits.Length > 1)
            {
                sb.Append('.');
                sb.Append(digits, 1, digits.Length - 1);
            }
            sb.Append('e');
            sb.Append(decExp >= 0 ? '+' : '-');
            sb.Append(Math.Abs(decExp).ToString("D2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static string FormatPositional(string digits, int decExp)
        {
            if (decExp < 0)
            {
                var sb = new StringBuilder("0.");
                sb.Append('0', -decExp - 1);
                sb.Append(digits);
                return sb.ToString();
            }
            int intLen = decExp + 1;
            if (digits.Length <= intLen)
                return digits + new string('0', intLen - digits.Length) + ".0";
            return digits.Substring(0, intLen) + "." + digits.Substring(intLen);
        }

        // --- exact decimal machinery ----

        // v = ±mantissa * 2^exponent2, mantissa <= 2^53. v==0 -> mantissa 0.
        public static void Decompose(double v, out bool negative, out ulong mantissa, out int exponent2)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            negative = bits < 0;
            int rawExp = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            if (rawExp == 0)
            {
                mantissa = (ulong)frac;      // subnormal (or zero)
                exponent2 = -1074;
            }
            else
            {
                mantissa = (ulong)(frac | 0x10000000000000L);   // add the implicit 53rd bit
                exponent2 = rawExp - 1075;
            }
        }

        // magnitude = num / 10^den10 (den10 >= 0), exact.
        private static void ExactMagnitude(EvalContext ctx, ulong mant, int e2, out BigInteger num, out int den10)
        {
            if (e2 >= 0)
            {
                num = new BigInteger(mant) << e2;
                den10 = 0;
            }
            else
            {
                int k = -e2;
                ctx.Budget.Step(1 + k / 8);
                ctx.Values.PreCharge(2L * (k + 40));
                num = new BigInteger(mant) * Pow5(k);
                den10 = k;
            }
        }

        // Exact significant-digit string of |v| (mant,e2), plus decExp so that |v| = 0.digits * 10^decExp.
        private static string ExactDigits(EvalContext ctx, ulong mant, int e2, out int decExp)
        {
            BigInteger num;
            int den10;
            ExactMagnitude(ctx, mant, e2, out num, out den10);
            string digits = num.ToString(CultureInfo.InvariantCulture);
            decExp = digits.Length - den10;
            return digits;
        }

        // _PyLong_DivmodNear port: round a/b to the nearest integer, ties to even (a>=0, b>0).
        internal static BigInteger DivmodNearHalfEven(BigInteger a, BigInteger b)
        {
            BigInteger r;
            BigInteger q = BigInteger.DivRem(a, b, out r);
            BigInteger twice = r * 2;
            if (twice > b || (twice == b && !q.IsEven))
                q += 1;
            return q;
        }

        // Round a significant-digit string to m digits, ties to even; a carry bumps decExp.
        private static string RoundSignificant(string digits, int m, ref int decExp)
        {
            if (digits.Length <= m)
                return digits + new string('0', m - digits.Length);
            BigInteger head = BigInteger.Parse(digits.Substring(0, m), CultureInfo.InvariantCulture);
            char next = digits[m];
            bool up;
            if (next > '5')
                up = true;
            else if (next < '5')
                up = false;
            else
            {
                bool anyAfter = false;
                for (int i = m + 1; i < digits.Length; i++)
                {
                    if (digits[i] != '0')
                    {
                        anyAfter = true;
                        break;
                    }
                }
                up = anyAfter || !head.IsEven;
            }
            if (up)
                head += 1;
            string rounded = head.ToString(CultureInfo.InvariantCulture);
            if (rounded.Length > m)
            {
                rounded = rounded.Substring(0, m);   // 999.. -> 1000.., trailing digit is 0
                decExp += 1;
            }
            return rounded;
        }

        // Cached non-negative powers of 5 and 10 (grow-only). Exact float rounding/formatting recompute
        // the same small-exponent powers on every call — half the cost of round(float, n) was these two
        // BigInteger.Pow calls. Fast path = a volatile read + index; the lock is taken only to grow, which
        // stops happening once the largest exponent a program uses has been seen. Callers pass k >= 0.
        private static volatile BigInteger[] _pow5 = { BigInteger.One };
        private static volatile BigInteger[] _pow10 = { BigInteger.One };
        private static readonly object _powLock = new object();

        internal static BigInteger Pow5(int k)
        {
            BigInteger[] t = _pow5;
            return k < t.Length ? t[k] : GrowPow5(k);
        }

        internal static BigInteger Pow10(int k)
        {
            BigInteger[] t = _pow10;
            return k < t.Length ? t[k] : GrowPow10(k);
        }

        private static BigInteger GrowPow5(int k)
        {
            lock (_powLock)
            {
                BigInteger[] t = _pow5;
                if (k < t.Length)
                    return t[k];
                var grown = new BigInteger[k + 1];
                System.Array.Copy(t, grown, t.Length);
                for (int i = t.Length; i <= k; i++)
                    grown[i] = grown[i - 1] * 5;
                _pow5 = grown;
                return grown[k];
            }
        }

        private static BigInteger GrowPow10(int k)
        {
            lock (_powLock)
            {
                BigInteger[] t = _pow10;
                if (k < t.Length)
                    return t[k];
                var grown = new BigInteger[k + 1];
                System.Array.Copy(t, grown, t.Length);
                for (int i = t.Length; i <= k; i++)
                    grown[i] = grown[i - 1] * 10;
                _pow10 = grown;
                return grown[k];
            }
        }

        // 10^n is exact as a double for n <= 22 (5^22 < 2^53).
        internal static readonly double[] ExactPow10 =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
            1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
        };

        private const double TwoPow52 = 4503599627370496.0;
        private const double TwoPow53 = 9007199254740992.0;

        // Dekker two-product error term: v*p == hi + result exactly (no FMA on net472; exact while
        // the 2^27-split intermediates neither overflow nor go subnormal — the caller's range
        // guards ensure that).
        private static double TwoProductLow(double a, double b, double p)
        {
            const double split = 134217729.0;   // 2^27 + 1
            double a1 = a * split, ah = a1 - (a1 - a), al = a - ah;
            double b1 = b * split, bh = b1 - (b1 - b), bl = b - bh;
            return ((ah * bh - p) + ah * bl + al * bh) + al * bl;
        }

        // round(float, n): for 0 <= n <= 22 and moderate |v| the answer is round-half-even of the
        // EXACT product v*10^n (Dekker gives it as hi+lo), then ONE correctly-rounded IEEE division
        // by the exact double 10^n — mathematically identical to the exact-decimal path. Anything
        // within one rounding error of the midpoint (incl. true ties, which need half-even) falls
        // back, so results stay bit-identical while the common case skips BigInteger+string+Parse.
        public static double RoundToDigits(EvalContext ctx, double v, int ndigits)
        {
            if (ndigits >= 0 && ndigits <= 22 && !double.IsNaN(v) && !double.IsInfinity(v))
            {
                double av = v < 0 ? -v : v;
                // Below 1e-290 the split could hit subnormals; at 2^52+ v is an integer anyway.
                if (av >= 1e-290 && av < TwoPow52)
                {
                    double p = ExactPow10[ndigits];
                    double hi = v * p;
                    if (hi > -TwoPow53 && hi < TwoPow53)   // else the rounded integer may not fit a double
                    {
                        double lo = TwoProductLow(v, p, hi);
                        double f = System.Math.Floor(hi);
                        double frac = (hi - f) + lo;       // hi-f exact (|hi| < 2^53); one rounding from +lo
                        // True frac is in [-0.5, 1): negative when hi is integral and lo < 0, and for
                        // |hi| >= 2^52 (ulp 1, lo up to 0.5) a TRUE TIE hides at -0.5 too — both
                        // midpoints get the near-tie window. 2^-52 = twice the worst-case error.
                        const double t = 2.220446049250313e-16;
                        if (frac - 0.5 > t)
                        {
                            double q = f + 1.0;
                            return q == 0.0 ? (v < 0 ? -0.0 : 0.0) : q / p;
                        }
                        if (frac - 0.5 < -t && frac + 0.5 > t)
                        {
                            return f == 0.0 ? (v < 0 ? -0.0 : 0.0) : f / p;
                        }
                        // Near a midpoint (incl. exact ties, which need half-even): decide via
                        // BigInteger, but FINISH with the same IEEE division — net472 double.Parse
                        // can misround by 1 ulp, so the string path must not settle in-band values.
                        return RoundTieInBand(v, ndigits, p);
                    }
                }
            }
            return RoundToDigitsExact(ctx, v, ndigits);
        }

        // Exact half-even of v*10^n for a fast-band near-tie: v = mant*2^-k (k >= 1 since |v| < 2^52),
        // so the scaled value is mant*5^n / 2^(k-n) — one shift-divide in BigInteger, no strings. The
        // caller's |hi| < 2^53 guard bounds q at 2^53, so (double)q and q/p are both exact/IEEE-rounded.
        private static double RoundTieInBand(double v, int ndigits, double p)
        {
            bool neg;
            ulong mant;
            int e2;
            Decompose(v, out neg, out mant, out e2);
            int k = -e2;
            BigInteger scaled = new BigInteger(mant) * Pow5(ndigits);
            BigInteger q = ndigits >= k
                ? scaled << (ndigits - k)
                : DivmodNearHalfEven(scaled, BigInteger.One << (k - ndigits));
            if (q.IsZero)
                return neg ? -0.0 : 0.0;
            double r = (double)q / p;
            return neg ? -r : r;
        }

        // The exact-decimal path (shortest-repr would misround 2.675).
        internal static double RoundToDigitsExact(EvalContext ctx, double v, int ndigits)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                return v;
            if (ndigits > 323)
                return v;
            if (ndigits < -308)
                return IsNegative(v) ? -0.0 : 0.0;
            if (v == 0.0)
                return v;

            bool neg;
            ulong mant;
            int e2;
            Decompose(v, out neg, out mant, out e2);
            string str;
            if (e2 >= 0)
            {
                if (ndigits >= 0)
                    return v;
                BigInteger n = new BigInteger(mant) << e2;
                BigInteger pow = Pow10(-ndigits);
                BigInteger r = DivmodNearHalfEven(n, pow) * pow;
                str = (neg ? "-" : "") + r.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                int k = -e2;
                if (ndigits >= k)
                    return v;
                ctx.Budget.Step(1 + k / 8);
                ctx.Values.PreCharge(2L * (k + 40));
                BigInteger d = new BigInteger(mant) * Pow5(k);
                if (ndigits >= 0)
                {
                    BigInteger r = DivmodNearHalfEven(d, Pow10(k - ndigits));
                    str = (neg ? "-" : "") + BuildFixed(r, ndigits);
                }
                else
                {
                    BigInteger r = DivmodNearHalfEven(d, Pow10(k - ndigits)) * Pow10(-ndigits);
                    str = (neg ? "-" : "") + r.ToString(CultureInfo.InvariantCulture);
                }
            }
            double result;
            try
            {
                result = double.Parse(str, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                throw Raise.Overflow(ctx, "rounded value too large to represent");
            }
            if (double.IsInfinity(result))
                throw Raise.Overflow(ctx, "rounded value too large to represent");
            if (result == 0.0 && neg)
                return -0.0;   // net472 double.Parse drops the sign of "-0.0"
            return result;
        }

        // Render a magnitude R (where R / 10^p is the value) with exactly p fractional digits, no sign.
        private static string BuildFixed(BigInteger r, int p)
        {
            string mag = r.ToString(CultureInfo.InvariantCulture);
            if (p == 0)
                return mag;
            if (mag.Length <= p)
                mag = new string('0', p - mag.Length + 1) + mag;
            int split = mag.Length - p;
            return mag.Substring(0, split) + "." + mag.Substring(split);
        }

        // 'f'/'F': fixed with `precision` fractional digits (sign kept, including -0.0).
        public static string FormatFixed(EvalContext ctx, double v, int precision, bool altForm)
        {
            if (double.IsNaN(v))
                return "nan";
            if (double.IsInfinity(v))
                return IsNegative(v) ? "-inf" : "inf";
            // The result is ~precision chars and the exact path builds a Pow10(precision)-sized BigInteger;
            // charge it up front so a pathological precision (e.g. '.999999999f') aborts on the string budget.
            ctx.Values.EnsureStrLen((long)precision + 32);
            bool neg = IsNegative(v);
            if (v == 0.0)
            {
                string zero = precision == 0 ? "0" + (altForm ? "." : "") : "0." + new string('0', precision);
                return (neg ? "-" : "") + zero;
            }

            ulong mant;
            int e2;
            bool sign;
            Decompose(v, out sign, out mant, out e2);
            BigInteger num;
            int den10;
            ExactMagnitude(ctx, mant, e2, out num, out den10);
            BigInteger r;
            if (precision >= den10)
                r = num * Pow10(precision - den10);
            else
                r = DivmodNearHalfEven(num, Pow10(den10 - precision));
            string body = BuildFixed(r, precision);
            if (precision == 0 && altForm)
                body += ".";
            return (neg ? "-" : "") + body;
        }

        // 'e'/'E': scientific with `precision` fractional digits.
        public static string FormatScientific(EvalContext ctx, double v, int precision, bool upperE, bool altForm)
        {
            if (double.IsNaN(v))
                return "nan";
            if (double.IsInfinity(v))
                return IsNegative(v) ? "-inf" : "inf";
            ctx.Values.EnsureStrLen((long)precision + 32);   // bound the ~precision-char result before building it
            bool neg = IsNegative(v);
            char e = upperE ? 'E' : 'e';
            if (v == 0.0)
            {
                string frac0 = precision > 0 ? "." + new string('0', precision) : (altForm ? "." : "");
                return (neg ? "-" : "") + "0" + frac0 + e + "+00";
            }

            ulong mant;
            int e2;
            bool sign;
            Decompose(v, out sign, out mant, out e2);
            int decExp;
            string digits = ExactDigits(ctx, mant, e2, out decExp);
            int m = precision + 1;
            string rounded = RoundSignificant(digits, m, ref decExp);
            int exp = decExp - 1;

            var sb = new StringBuilder();
            if (neg)
                sb.Append('-');
            sb.Append(rounded[0]);
            if (precision > 0)
            {
                sb.Append('.');
                sb.Append(rounded, 1, precision);
            }
            else if (altForm)
            {
                sb.Append('.');
            }
            sb.Append(e);
            sb.Append(exp >= 0 ? '+' : '-');
            sb.Append(Math.Abs(exp).ToString("D2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // 'g'/'G': general — fixed or scientific by exponent, trailing zeros trimmed unless altForm/keep.
        public static string FormatGeneral(EvalContext ctx, double v, int precision, bool upperE, bool altForm, bool keepTrailingZeros)
        {
            if (double.IsNaN(v))
                return "nan";
            if (double.IsInfinity(v))
                return IsNegative(v) ? "-inf" : "inf";
            int p = precision <= 0 ? 1 : precision;

            int exp;
            if (v == 0.0)
            {
                exp = 0;
            }
            else
            {
                ulong mant;
                int e2;
                bool sign;
                Decompose(v, out sign, out mant, out e2);
                int decExp;
                string digits = ExactDigits(ctx, mant, e2, out decExp);
                RoundSignificant(digits, p, ref decExp);   // updates decExp on carry
                exp = decExp - 1;
            }

            string body;
            if (-4 <= exp && exp < p)
                body = FormatFixed(ctx, v, p - 1 - exp, altForm);
            else
                body = FormatScientific(ctx, v, p - 1, upperE, altForm);

            if (!altForm && !keepTrailingZeros)
                body = StripTrailingZeros(body);
            return body;
        }

        private static string StripTrailingZeros(string s)
        {
            int eIdx = s.IndexOfAny(new[] { 'e', 'E' });
            string mant = eIdx < 0 ? s : s.Substring(0, eIdx);
            string exp = eIdx < 0 ? "" : s.Substring(eIdx);
            int dot = mant.IndexOf('.');
            if (dot >= 0)
            {
                int end = mant.Length;
                while (end > dot + 1 && mant[end - 1] == '0')
                    end--;
                if (end == dot + 1)
                    end = dot;   // drop the now-empty point
                mant = mant.Substring(0, end);
            }
            return mant + exp;
        }
    }
}
