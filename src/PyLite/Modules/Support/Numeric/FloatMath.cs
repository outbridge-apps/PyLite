using System;

namespace Outbridge.PyLite.Modules.Support
{
    // Shared double bit-magic and libm formulas. Pure statics: math/random/
    // statistics/decimal reuse these without duplicating the tricks. Domain checks (NaN/Inf -> ValueError/
    // OverflowError) live in the calling module wrapper, so these return the raw (possibly NaN) result.
    internal static class FloatMath
    {
        private const double TwoPow54 = 18014398509481984.0;   // 2^54
        private const double Ln2 = 0.6931471805599453;

        // 2^k for -1022 <= k <= 1023 (normal exponent field).
        private static double Two(int k) { return BitConverter.Int64BitsToDouble(((long)(1023 + k)) << 52); }

        // frexp: x = m * 2^e with 0.5 <= |m| < 1. Signed zero / inf / nan pass through with e = 0.
        public static void Frexp(double x, out double mantissa, out int exponent)
        {
            if (x == 0.0)
            {
                mantissa = x;   // preserve -0.0
                exponent = 0;
                return;
            }
            if (double.IsNaN(x) || double.IsInfinity(x))
            {
                mantissa = x;
                exponent = 0;
                return;
            }
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            int eAdjust;
            if (exp == 0)
            {
                double x2 = x * TwoPow54;   // normalize a subnormal
                bits = BitConverter.DoubleToInt64Bits(x2);
                exp = (int)((bits >> 52) & 0x7FF);
                eAdjust = -54;
            }
            else
            {
                eAdjust = 0;
            }
            exponent = exp - 1022 + eAdjust;
            long mBits = (bits & unchecked((long)0x800FFFFFFFFFFFFF)) | (1022L << 52);
            mantissa = BitConverter.Int64BitsToDouble(mBits);
        }

        // x * 2^n without precision loss (net472 has no Math.ScaleB). Underflow -> 0.0, no exception.
        public static double ScaleB(double x, int n)
        {
            if (x == 0.0 || double.IsNaN(x) || double.IsInfinity(x))
                return x;
            while (n > 1023)
            {
                x *= Two(1023);
                n -= 1023;
                if (double.IsInfinity(x))
                    return x;
            }
            while (n < -1074)
            {
                x *= double.Epsilon;   // 2^-1074
                n += 1074;
                if (x == 0.0)
                    return x;
            }
            if (n >= -1022)
                return x * Two(n);
            // n in [-1074, -1023]: 2^n is subnormal (mantissa bit at position n+1074).
            return x * BitConverter.Int64BitsToDouble(1L << (n + 1074));
        }

        public static double Ldexp(double x, int exp)
        {
            double r;
            LdexpOverflowed(x, exp, out r);
            return r;
        }

        // true => overflow (caller raises OverflowError "math range error"); underflow is not an error.
        public static bool LdexpOverflowed(double x, int exp, out double result)
        {
            if (x == 0.0 || double.IsNaN(x) || double.IsInfinity(x))
            {
                result = x;
                return false;
            }
            result = ScaleB(x, exp);
            return double.IsInfinity(result);
        }

        public static double CopySign(double x, double y)
        {
            long xb = BitConverter.DoubleToInt64Bits(x);
            long yb = BitConverter.DoubleToInt64Bits(y);
            return BitConverter.Int64BitsToDouble((xb & 0x7FFFFFFFFFFFFFFFL) | (yb & unchecked((long)0x8000000000000000L)));
        }

        // C# %, NOT Math.IEEERemainder: fmod(5,3)==2.0, fmod(-5,3)==-2.0.
        public static double Fmod(double x, double y) { return x % y; }

        public static double Hypot(double x, double y)
        {
            if (double.IsInfinity(x) || double.IsInfinity(y))
                return double.PositiveInfinity;
            x = Math.Abs(x);
            y = Math.Abs(y);
            if (x < y)
            {
                double t = x;
                x = y;
                y = t;
            }
            if (x == 0.0)
                return 0.0;
            double r = y / x;
            return x * Math.Sqrt(1.0 + r * r);
        }

        public static bool IsNegZero(double x) { return BitConverter.DoubleToInt64Bits(x) == long.MinValue; }

        // ---- part 2: expm1/log1p/asinh/acosh/atanh (net472 BCL lacks these) ----

        public static double Expm1(double x)
        {
            if (x == 0.0)
                return x;   // preserve -0.0
            if (Math.Abs(x) < 1e-5)
                return x + 0.5 * x * x + x * x * x / 6.0;
            return Math.Exp(x) - 1.0;
        }

        public static double Log1p(double x)
        {
            if (x == 0.0)
                return x;   // preserve -0.0
            double u = 1.0 + x;
            if (u == 1.0)
                return x;
            return Math.Log(u) * (x / (u - 1.0));
        }

        public static double Asinh(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || x == 0.0)
                return x;
            double ax = Math.Abs(x);
            double w;
            if (ax > 1e8)
                w = Math.Log(ax) + Ln2;
            else if (ax > 2.0)
                w = Math.Log(2.0 * ax + 1.0 / (Math.Sqrt(ax * ax + 1.0) + ax));
            else
            {
                double t = ax * ax;
                w = Log1p(ax + t / (1.0 + Math.Sqrt(1.0 + t)));
            }
            return CopySign(w, x);
        }

        public static double Acosh(double x)
        {
            if (double.IsNaN(x))
                return x;
            if (x < 1.0)
                return double.NaN;   // domain -> caller ValueError
            if (x == 1.0)
                return 0.0;
            if (x > 1e8)
                return Math.Log(x) + Ln2;
            if (x > 2.0)
                return Math.Log(2.0 * x - 1.0 / (x + Math.Sqrt(x * x - 1.0)));
            double t = x - 1.0;
            return Log1p(t + Math.Sqrt(2.0 * t + t * t));
        }

        public static double Atanh(double x)
        {
            if (double.IsNaN(x))
                return x;
            double ax = Math.Abs(x);
            if (ax >= 1.0)
                return double.NaN;   // atanh(±1) and |x|>1 -> caller ValueError
            if (ax < 1e-4)
                return x;
            if (ax < 0.5)
                return 0.5 * Log1p(2.0 * x / (1.0 - x));
            double w = 0.5 * Log1p(2.0 * ax / (1.0 - ax));
            return CopySign(w, x);
        }
    }
}
