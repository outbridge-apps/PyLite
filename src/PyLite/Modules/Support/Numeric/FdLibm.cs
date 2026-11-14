using System;
using System.Globalization;
using System.Numerics;

namespace Outbridge.PyLite.Modules.Support
{
    // sin, cos, tan and expm1: fdlibm's kernels. The .NET Framework's Math.Sin loses digits from about 1e5 and returns
    // the argument itself from 2**63. The one change: a huge argument is reduced with exact integers, not __kernel_rem_pio2.
    // ====================================================
    // Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved.
    //
    // Developed at SunPro, a Sun Microsystems, Inc. business.
    // Permission to use, copy, modify, and distribute this
    // software is freely granted, provided that this notice
    // is preserved.
    // ====================================================
    internal static class FdLibm
    {
        private const double InvPio2 = 6.36619772367581382433e-01;
        private const double Pio2_1 = 1.57079632673412561417e+00, Pio2_1t = 6.07710050650619224932e-11;
        private const double Pio2_2 = 6.07710050630396597660e-11, Pio2_2t = 2.02226624879595063154e-21;
        private const double Pio2_3 = 2.02226624871116645580e-21, Pio2_3t = 8.47842766036889956997e-32;
        private const double ToInt = 1.5 / 2.22044604925031308085e-16;

        private const int ScaleBits = 1501;   // PiOver2Scaled = pi/2 * 2**1501; enough for 2**1024 and the digits left after it
        private static readonly BigInteger PiOver2Scaled = BigInteger.Parse("0" +
            "3243f6a8885a308d313198a2e03707344a4093822299f31d0082efa98ec4e6c89452821e638d01377be5466cf34e90c6cc0ac29b7c97c50dd3f8" +
            "4d5b5b54709179216d5d98979fb1bd1310ba698dfb5ac2ffd72dbd01adfb7b8e1afed6a267e96ba7c9045f12c7f9924a19947b3916cf70801f2e" +
            "2858efc16636920d871574e69a458fea3f4933d7e0d95748f728eb658718bcd5882154aee7b54a41dc25a59b59c30d5392af26013c5d1b023286" +
            "085f0ca417918b8db38ef8e79dcb", NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        private static int HighWord(double x)
        {
            return (int)(BitConverter.DoubleToInt64Bits(x) >> 32) & 0x7fffffff;
        }

        private static double Pow2(int k)   // normal range only
        {
            return BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
        }

        public static double Sin(double x)
        {
            int ix = HighWord(x);
            if (ix <= 0x3fe921fb)
                return ix < 0x3e500000 ? x : KernelSin(x, 0.0, false);
            if (ix >= 0x7ff00000)
                return double.NaN;
            double y0, y1;
            switch (RemPio2(x, ix, out y0, out y1) & 3)
            {
                case 0: return KernelSin(y0, y1, true);
                case 1: return KernelCos(y0, y1);
                case 2: return -KernelSin(y0, y1, true);
                default: return -KernelCos(y0, y1);
            }
        }

        public static double Cos(double x)
        {
            int ix = HighWord(x);
            if (ix <= 0x3fe921fb)
                return ix < 0x3e46a09e ? 1.0 : KernelCos(x, 0.0);
            if (ix >= 0x7ff00000)
                return double.NaN;
            double y0, y1;
            switch (RemPio2(x, ix, out y0, out y1) & 3)
            {
                case 0: return KernelCos(y0, y1);
                case 1: return -KernelSin(y0, y1, true);
                case 2: return -KernelCos(y0, y1);
                default: return KernelSin(y0, y1, true);
            }
        }

        public static double Tan(double x)
        {
            int ix = HighWord(x);
            if (ix <= 0x3fe921fb)
                return ix < 0x3e400000 ? x : KernelTan(x, 0.0, false);
            if (ix >= 0x7ff00000)
                return double.NaN;
            double y0, y1;
            int n = RemPio2(x, ix, out y0, out y1);
            return KernelTan(y0, y1, (n & 1) != 0);
        }

        // x = n*pi/2 + y0 + y1, |y0| <= pi/4.
        private static int RemPio2(double x, int ix, out double y0, out double y1)
        {
            if (ix >= 0x413921fb)   // |x| ~>= 2**20 * pi/2
                return RemPio2Huge(x, out y0, out y1);
            double fn = x * InvPio2 + ToInt - ToInt;
            double r = x - fn * Pio2_1;
            double w = fn * Pio2_1t;   // good to 85 bits
            y0 = r - w;
            int ex = ix >> 20;
            if (ex - (HighWord(y0) >> 20) > 16)
            {
                double t = r;
                w = fn * Pio2_2;
                r = t - w;
                w = fn * Pio2_2t - ((t - r) - w);   // good to 118 bits
                y0 = r - w;
                if (ex - (HighWord(y0) >> 20) > 49)
                {
                    t = r;
                    w = fn * Pio2_3;
                    r = t - w;
                    w = fn * Pio2_3t - ((t - r) - w);   // good to 151 bits: every case
                    y0 = r - w;
                }
            }
            y1 = (r - y0) - w;
            return (int)fn;
        }

        private static int RemPio2Huge(double x, out double y0, out double y1)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            long mantissa = (bits & 0xFFFFFFFFFFFFFL) | (1L << 52);   // |x| is far from subnormal here
            int exponent = (int)((bits >> 52) & 0x7FF) - 1075;
            BigInteger remainder;
            BigInteger q = BigInteger.DivRem(new BigInteger(mantissa) << (exponent + ScaleBits), PiOver2Scaled, out remainder);
            if (remainder + remainder > PiOver2Scaled)
            {
                q += BigInteger.One;
                remainder -= PiOver2Scaled;
            }
            bool negative = remainder.Sign < 0;
            BigInteger abs = BigInteger.Abs(remainder);
            int used = BitLength(abs);
            int shift = Math.Max(used - 53, 0);
            BigInteger top = abs >> shift;
            double hi = (double)(long)top * Pow2(shift - ScaleBits);
            BigInteger rest = abs - (top << shift);
            int restUsed = BitLength(rest);
            int restShift = Math.Max(restUsed - 53, 0);
            double lo = (double)(long)(rest >> restShift) * Pow2(restShift - ScaleBits);
            y0 = hi + lo;
            y1 = lo - (y0 - hi);
            int n = (int)(q & new BigInteger(3));
            if (negative)
            {
                y0 = -y0;
                y1 = -y1;
            }
            if (x < 0)
            {
                y0 = -y0;
                y1 = -y1;
                n = -n;
            }
            return n;
        }

        private static int BitLength(BigInteger v)
        {
            if (v.IsZero)
                return 0;
            byte[] b = v.ToByteArray();
            int top = b[b.Length - 1], n = 0;
            while (top != 0)
            {
                n++;
                top >>= 1;
            }
            return (b.Length - 1) * 8 + n;
        }

        private static double KernelSin(double x, double y, bool hasTail)
        {
            const double S1 = -1.66666666666666324348e-01, S2 = 8.33333333332248946124e-03, S3 = -1.98412698298579493134e-04,
                S4 = 2.75573137070700676789e-06, S5 = -2.50507602534068634195e-08, S6 = 1.58969099521155010221e-10;
            double z = x * x;
            double w = z * z;
            double r = S2 + z * (S3 + z * S4) + z * w * (S5 + z * S6);
            double v = z * x;
            return hasTail ? x - ((z * (0.5 * y - v * r) - y) - v * S1) : x + v * (S1 + z * r);
        }

        private static double KernelCos(double x, double y)
        {
            const double C1 = 4.16666666666666019037e-02, C2 = -1.38888888888741095749e-03, C3 = 2.48015872894767294178e-05,
                C4 = -2.75573143513906633035e-07, C5 = 2.08757232129817482790e-09, C6 = -1.13596475577881948265e-11;
            double z = x * x;
            double w = z * z;
            double r = z * (C1 + z * (C2 + z * C3)) + w * w * (C4 + z * (C5 + z * C6));
            double hz = 0.5 * z;
            w = 1.0 - hz;
            return w + (((1.0 - w) - hz) + (z * r - x * y));
        }

        private static double ClearLowWord(double x)
        {
            return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) & unchecked((long)0xFFFFFFFF00000000UL));
        }

        private static double KernelTan(double x, double y, bool odd)
        {
            const double T0 = 3.33333333333334091986e-01, T1 = 1.33333333333201242699e-01, T2 = 5.39682539762260521377e-02,
                T3 = 2.18694882948595424599e-02, T4 = 8.86323982359930005737e-03, T5 = 3.59207910759131235356e-03,
                T6 = 1.45620945432529025516e-03, T7 = 5.88041240820264096874e-04, T8 = 2.46463134818469906812e-04,
                T9 = 7.81794442939557092300e-05, T10 = 7.14072491382608190305e-05, T11 = -1.85586374855275456654e-05,
                T12 = 2.59073051863633712884e-05, Pio4 = 7.85398163397448278999e-01, Pio4lo = 3.06161699786838301793e-17;
            bool big = HighWord(x) >= 0x3FE59428;   // |x| >= 0.6744
            bool negative = BitConverter.DoubleToInt64Bits(x) < 0;
            if (big)
            {
                if (negative)
                {
                    x = -x;
                    y = -y;
                }
                x = (Pio4 - x) + (Pio4lo - y);
                y = 0.0;
            }
            double z = x * x;
            double w = z * z;
            double r = T1 + w * (T3 + w * (T5 + w * (T7 + w * (T9 + w * T11))));
            double v = z * (T2 + w * (T4 + w * (T6 + w * (T8 + w * (T10 + w * T12)))));
            double s = z * x;
            r = y + z * (s * (r + v) + y) + s * T0;
            w = x + r;
            if (big)
            {
                s = odd ? -1.0 : 1.0;
                v = s - 2.0 * (x + (r - w * w / (w + s)));
                return negative ? -v : v;
            }
            if (!odd)
                return w;
            // -1/(x+r) directly has up to 2 ulp of error
            double w0 = ClearLowWord(w);
            v = r - (w0 - x);
            double a = -1.0 / w;
            double a0 = ClearLowWord(a);
            return a0 + a * (1.0 + a0 * w0 + a0 * v);
        }

        public static double Expm1(double x)
        {
            const double OThreshold = 7.09782712893383973096e+02, Ln2Hi = 6.93147180369123816490e-01,
                Ln2Lo = 1.90821492927058770002e-10, InvLn2 = 1.44269504088896338700e+00,
                Q1 = -3.33333333333331316428e-02, Q2 = 1.58730158725481460165e-03, Q3 = -7.93650757867487942473e-05,
                Q4 = 4.00821782732936239552e-06, Q5 = -2.01099218183624371326e-07;
            int hx = HighWord(x);
            bool negative = BitConverter.DoubleToInt64Bits(x) < 0;
            if (hx >= 0x4043687A)   // |x| >= 56*ln2
            {
                if (double.IsNaN(x))
                    return x;
                if (negative)
                    return -1.0;
                if (x > OThreshold)
                    return double.PositiveInfinity;
            }
            double c = 0.0, t;
            int k;
            if (hx > 0x3fd62e42)   // |x| > 0.5 ln2
            {
                double hi, lo;
                if (hx < 0x3FF0A2B2)   // and |x| < 1.5 ln2
                {
                    hi = negative ? x + Ln2Hi : x - Ln2Hi;
                    lo = negative ? -Ln2Lo : Ln2Lo;
                    k = negative ? -1 : 1;
                }
                else
                {
                    k = (int)(InvLn2 * x + (negative ? -0.5 : 0.5));
                    t = k;
                    hi = x - t * Ln2Hi;
                    lo = t * Ln2Lo;
                }
                x = hi - lo;
                c = (hi - x) - lo;
            }
            else if (hx < 0x3c900000)   // |x| < 2**-54
            {
                return x;
            }
            else
            {
                k = 0;
            }
            double hfx = 0.5 * x;
            double hxs = x * hfx;
            double r1 = 1.0 + hxs * (Q1 + hxs * (Q2 + hxs * (Q3 + hxs * (Q4 + hxs * Q5))));
            t = 3.0 - r1 * hfx;
            double e = hxs * ((r1 - t) / (6.0 - x * t));
            if (k == 0)
                return x - (x * e - hxs);
            e = x * (e - c) - c;
            e -= hxs;
            if (k == -1)
                return 0.5 * (x - e) - 0.5;
            if (k == 1)
                return x < -0.25 ? -2.0 * (e - (x + 0.5)) : 1.0 + 2.0 * (x - e);
            double y;
            if (k < 0 || k > 56)
            {
                y = x - e + 1.0;
                y = k == 1024 ? y * 2.0 * 8.98846567431157953865e+307 : y * Pow2(k);
                return y - 1.0;
            }
            double twoMinusK = Pow2(-k);
            y = k < 20 ? (x - e + (1.0 - twoMinusK)) * Pow2(k) : (x - (e + twoMinusK) + 1.0) * Pow2(k);
            return y;
        }
    }
}
