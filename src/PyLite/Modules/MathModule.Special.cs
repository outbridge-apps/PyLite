using System;
using System.Collections.Generic;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // erf, erfc, gamma, lgamma. gamma and lgamma are CPython's own (Modules/mathmodule.c m_tgamma/m_lgamma:
    // Lanczos, N=13, g=6.0246..., PSF License Version 2), which CPython keeps to avoid the platform libm's;
    // erf and erfc come from the platform libm in CPython, here from fdlibm's s_erf.c (the one glibc derives
    // from). Errors follow CPython's math_1a: a domain error is ValueError, an overflow OverflowError.
    internal static partial class MathModule
    {
        private enum MathErr { None, Domain, Range }

        private delegate double SpecialFn(double x, out MathErr err);

        private static void AddSpecial(Dictionary<string, ScriptValue> m)
        {
            m["erf"] = Special("erf", Erf);
            m["erfc"] = Special("erfc", Erfc);
            m["gamma"] = Special("gamma", TGamma, "expected a noninteger or positive integer, got ");
            m["lgamma"] = Special("lgamma", LGamma, "expected a noninteger or positive integer, got ");
        }

        private static BuiltinFunctionValue Special(string name, SpecialFn f, string domain = null)
        {
            return BuiltinFunctionValue.Make(name, (self, args, kw, c) =>
            {
                double x = AsDouble(c, Args.One(c, args, kw, name));
                MathErr err;
                double r = f(x, out err);
                if (err == MathErr.Domain)
                    throw Domain(c, domain, x);
                if (err == MathErr.Range && Math.Abs(r) >= 1.5)   // is_error: an underflow is no error
                    throw Raise.Overflow(c, "math range error");
                return c.Values.Float(r);
            });
        }

        // ---- gamma, lgamma: CPython's m_tgamma and m_lgamma ----

        private const double LogPi = 1.144729885849400174143427351353058711647;
        private const double LanczosG = 6.024680040776729583740234375;
        private const double LanczosGMinusHalf = 5.524680040776729583740234375;

        private static readonly double[] LanczosNum =
        {
            23531376880.410759688572007674451636754734846804940,
            42919803642.649098768957899047001988850926355848959,
            35711959237.355668049440185451547166705960488635843,
            17921034426.037209699919755754458931112671403265390,
            6039542586.3520280050642916443072979210699388420708,
            1439720407.3117216736632230727949123939715485786772,
            248874557.86205415651146038641322942321632125127801,
            31426415.585400194380614231628318205362874684987640,
            2876370.6289353724412254090516208496135991145378768,
            186056.26539522349504029498971604569928220784236328,
            8071.6720023658162106380029022722506138218516325024,
            210.82427775157934587250973392071336271166969580291,
            2.5066282746310002701649081771338373386264310793408,
        };

        // x*(x+1)*...*(x+11): the unsigned Stirling numbers of the first kind s(12, k)
        private static readonly double[] LanczosDen =
        {
            0.0, 39916800.0, 120543840.0, 150917976.0, 105258076.0, 45995730.0,
            13339535.0, 2637558.0, 357423.0, 32670.0, 1925.0, 66.0, 1.0,
        };

        private static readonly double[] GammaIntegral =
        {
            1.0, 1.0, 2.0, 6.0, 24.0, 120.0, 720.0, 5040.0, 40320.0, 362880.0,
            3628800.0, 39916800.0, 479001600.0, 6227020800.0, 87178291200.0,
            1307674368000.0, 20922789888000.0, 355687428096000.0,
            6402373705728000.0, 121645100408832000.0, 2432902008176640000.0,
            51090942171709440000.0, 1124000727777607680000.0,
        };

        // Lanczos' sum L_g(x) for x > 0; past 5 as a rational function in 1/x, which cannot overflow.
        private static double LanczosSum(double x)
        {
            double num = 0.0, den = 0.0;
            if (x < 5.0)
            {
                for (int i = LanczosNum.Length; --i >= 0;)
                {
                    num = num * x + LanczosNum[i];
                    den = den * x + LanczosDen[i];
                }
            }
            else
            {
                for (int i = 0; i < LanczosNum.Length; i++)
                {
                    num = num / x + LanczosNum[i];
                    den = den / x + LanczosDen[i];
                }
            }
            return num / den;
        }

        // sin(pi*x) for finite x, exact at the integers and half-integers.
        private static double SinPi(double x)
        {
            double y = Math.Abs(x) % 2.0;
            int n = (int)Math.Round(2.0 * y, MidpointRounding.AwayFromZero);   // C's round()
            double r;
            switch (n)
            {
                case 0: r = Math.Sin(Math.PI * y); break;
                case 1: r = Math.Cos(Math.PI * (y - 0.5)); break;
                case 2: r = Math.Sin(Math.PI * (1.0 - y)); break;   // not -sin(pi*(y-1)): that is -0.0 at 1
                case 3: r = -Math.Cos(Math.PI * (y - 1.5)); break;
                default: r = Math.Sin(Math.PI * (y - 2.0)); break;
            }
            return FloatMath.CopySign(1.0, x) * r;
        }

        private static double TGamma(double x, out MathErr err)
        {
            err = MathErr.None;
            if (double.IsNaN(x) || double.IsInfinity(x))
            {
                if (double.IsNaN(x) || x > 0.0)
                    return x;
                err = MathErr.Domain;   // gamma(-inf)
                return double.NaN;
            }
            if (x == 0.0)
            {
                err = MathErr.Domain;
                return FloatMath.CopySign(double.PositiveInfinity, x);
            }
            if (x == Math.Floor(x))
            {
                if (x < 0.0)
                {
                    err = MathErr.Domain;   // a pole at every negative integer
                    return double.NaN;
                }
                if (x <= GammaIntegral.Length)
                    return GammaIntegral[(int)x - 1];
            }
            double absx = Math.Abs(x);
            double r;
            if (absx < 1e-20)   // gamma(x) ~ 1/x near 0
            {
                r = 1.0 / x;
                if (double.IsInfinity(r))
                    err = MathErr.Range;
                return r;
            }
            if (absx > 200.0)   // overflows past 171.6; underflows to +-0 below -200
            {
                if (x < 0.0)
                    return 0.0 / SinPi(x);
                err = MathErr.Range;
                return double.PositiveInfinity;
            }
            double y = absx + LanczosGMinusHalf;
            double z;
            if (absx > LanczosGMinusHalf)   // the rounding error of y, recovered exactly
            {
                double q = y - absx;
                z = q - LanczosGMinusHalf;
            }
            else
            {
                double q = y - LanczosGMinusHalf;
                z = q - absx;
            }
            z = z * LanczosG / y;
            if (x < 0.0)
            {
                r = -Math.PI / SinPi(absx) / absx * Math.Exp(y) / LanczosSum(absx);
                r -= z * r;
                if (absx < 140.0)
                {
                    r /= Math.Pow(y, absx - 0.5);
                }
                else
                {
                    double sqrtpow = Math.Pow(y, absx / 2.0 - 0.25);
                    r /= sqrtpow;
                    r /= sqrtpow;
                }
            }
            else
            {
                r = LanczosSum(absx) / Math.Exp(y);
                r += z * r;
                if (absx < 140.0)
                {
                    r *= Math.Pow(y, absx - 0.5);
                }
                else
                {
                    double sqrtpow = Math.Pow(y, absx / 2.0 - 0.25);
                    r *= sqrtpow;
                    r *= sqrtpow;
                }
            }
            if (double.IsInfinity(r))
                err = MathErr.Range;
            return r;
        }

        private static double LGamma(double x, out MathErr err)
        {
            err = MathErr.None;
            if (double.IsNaN(x))
                return x;
            if (double.IsInfinity(x))
                return double.PositiveInfinity;
            if (x == Math.Floor(x) && x <= 2.0)
            {
                if (x <= 0.0)
                {
                    err = MathErr.Domain;   // a pole at 0 and every negative integer
                    return double.PositiveInfinity;
                }
                return 0.0;   // lgamma(1) = lgamma(2) = 0
            }
            double absx = Math.Abs(x);
            if (absx < 1e-20)
                return -Math.Log(absx);
            double r = Math.Log(LanczosSum(absx)) - LanczosG;
            r += (absx - 0.5) * (Math.Log(absx + LanczosG - 0.5) - 1);
            if (x < 0.0)
                r = LogPi - Math.Log(Math.Abs(SinPi(absx))) - Math.Log(absx) - r;   // the reflection formula
            if (double.IsInfinity(r))
                err = MathErr.Range;
            return r;
        }

        // ---- erf, erfc: fdlibm s_erf.c ----
        // ====================================================
        // Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved.
        //
        // Developed at SunSoft, a Sun Microsystems, Inc. business.
        // Permission to use, copy, modify, and distribute this
        // software is freely granted, provided that this notice
        // is preserved.
        // ====================================================
        // The constants are fdlibm's, from their bit patterns.

        private static double D(uint hi, uint lo) { return BitConverter.Int64BitsToDouble(((long)hi << 32) | lo); }

        private const double Tiny = 1e-300;
        private static readonly double Erx = D(0x3FEB0AC1, 0x60000000);   // (float)0.84506291151
        // erf on [0, 0.84375]
        private static readonly double Efx = D(0x3FC06EBA, 0x8214DB69);
        private static readonly double Efx8 = D(0x3FF06EBA, 0x8214DB69);
        private static readonly double Pp0 = D(0x3FC06EBA, 0x8214DB68);
        private static readonly double Pp1 = D(0xBFD4CD7D, 0x691CB913);
        private static readonly double Pp2 = D(0xBF9D2A51, 0xDBD7194F);
        private static readonly double Pp3 = D(0xBF77A291, 0x236668E4);
        private static readonly double Pp4 = D(0xBEF8EAD6, 0x120016AC);
        private static readonly double Qq1 = D(0x3FD97779, 0xCDDADC09);
        private static readonly double Qq2 = D(0x3FB0A54C, 0x5536CEBA);
        private static readonly double Qq3 = D(0x3F74D022, 0xC4D36B0F);
        private static readonly double Qq4 = D(0x3F215DC9, 0x221C1A10);
        private static readonly double Qq5 = D(0xBED09C43, 0x42A26120);
        // erf on [0.84375, 1.25]
        private static readonly double Pa0 = D(0xBF6359B8, 0xBEF77538);
        private static readonly double Pa1 = D(0x3FDA8D00, 0xAD92B34D);
        private static readonly double Pa2 = D(0xBFD7D240, 0xFBB8C3F1);
        private static readonly double Pa3 = D(0x3FD45FCA, 0x805120E4);
        private static readonly double Pa4 = D(0xBFBC6398, 0x3D3E28EC);
        private static readonly double Pa5 = D(0x3FA22A36, 0x599795EB);
        private static readonly double Pa6 = D(0xBF61BF38, 0x0A96073F);
        private static readonly double Qa1 = D(0x3FBB3E66, 0x18EEE323);
        private static readonly double Qa2 = D(0x3FE14AF0, 0x92EB6F33);
        private static readonly double Qa3 = D(0x3FB2635C, 0xD99FE9A7);
        private static readonly double Qa4 = D(0x3FC02660, 0xE763351F);
        private static readonly double Qa5 = D(0x3F8BEDC2, 0x6B51DD1C);
        private static readonly double Qa6 = D(0x3F888B54, 0x5735151D);
        // erfc on [1.25, 1/0.35]
        private static readonly double Ra0 = D(0xBF843412, 0x600D6435);
        private static readonly double Ra1 = D(0xBFE63416, 0xE4BA7360);
        private static readonly double Ra2 = D(0xC0251E04, 0x41B0E726);
        private static readonly double Ra3 = D(0xC04F300A, 0xE4CBA38D);
        private static readonly double Ra4 = D(0xC0644CB1, 0x84282266);
        private static readonly double Ra5 = D(0xC067135C, 0xEBCCABB2);
        private static readonly double Ra6 = D(0xC0545265, 0x57E4D2F2);
        private static readonly double Ra7 = D(0xC023A0EF, 0xC69AC25C);
        private static readonly double Sa1 = D(0x4033A6B9, 0xBD707687);
        private static readonly double Sa2 = D(0x4061350C, 0x526AE721);
        private static readonly double Sa3 = D(0x407B290D, 0xD58A1A71);
        private static readonly double Sa4 = D(0x40842B19, 0x21EC2868);
        private static readonly double Sa5 = D(0x407AD021, 0x57700314);
        private static readonly double Sa6 = D(0x405B28A3, 0xEE48AE2C);
        private static readonly double Sa7 = D(0x401A47EF, 0x8E484A93);
        private static readonly double Sa8 = D(0xBFAEEFF2, 0xEE749A62);
        // erfc on [1/0.35, 28]
        private static readonly double Rb0 = D(0xBF843412, 0x39E86F4A);
        private static readonly double Rb1 = D(0xBFE993BA, 0x70C285DE);
        private static readonly double Rb2 = D(0xC031C209, 0x555F995A);
        private static readonly double Rb3 = D(0xC064145D, 0x43C5ED98);
        private static readonly double Rb4 = D(0xC083EC88, 0x1375F228);
        private static readonly double Rb5 = D(0xC0900461, 0x6A2E5992);
        private static readonly double Rb6 = D(0xC07E384E, 0x9BDC383F);
        private static readonly double Sb1 = D(0x403E568B, 0x261D5190);
        private static readonly double Sb2 = D(0x40745CAE, 0x221B9F0A);
        private static readonly double Sb3 = D(0x409802EB, 0x189D5118);
        private static readonly double Sb4 = D(0x40A8FFB7, 0x688C246A);
        private static readonly double Sb5 = D(0x40A3F219, 0xCEDF3BE6);
        private static readonly double Sb6 = D(0x407DA874, 0xE79FE763);
        private static readonly double Sb7 = D(0xC03670E2, 0x42712D62);

        private static int HighWord(double x) { return (int)(BitConverter.DoubleToInt64Bits(x) >> 32); }

        // x with the low 32 bits of its mantissa cleared: s*s is exact, so exp(-x*x) loses nothing.
        private static double ClearLow(double x)
        {
            return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) & unchecked((long)0xFFFFFFFF00000000UL));
        }

        // exp(-x*x - 0.5625 + R/S) for x >= 1.25, the tail both functions share; nearTable for |x| < 1/0.35
        // (s_erf.c draws that line one word apart in erf and erfc, so each caller decides).
        private static double ErfcTail(double x, bool nearTable)
        {
            double s = 1.0 / (x * x);
            double r, ss;
            if (nearTable)
            {
                r = Ra0 + s * (Ra1 + s * (Ra2 + s * (Ra3 + s * (Ra4 + s * (Ra5 + s * (Ra6 + s * Ra7))))));
                ss = 1.0 + s * (Sa1 + s * (Sa2 + s * (Sa3 + s * (Sa4 + s * (Sa5 + s * (Sa6 + s * (Sa7 + s * Sa8)))))));
            }
            else
            {
                r = Rb0 + s * (Rb1 + s * (Rb2 + s * (Rb3 + s * (Rb4 + s * (Rb5 + s * Rb6)))));
                ss = 1.0 + s * (Sb1 + s * (Sb2 + s * (Sb3 + s * (Sb4 + s * (Sb5 + s * (Sb6 + s * Sb7))))));
            }
            double z = ClearLow(x);
            return Math.Exp(-z * z - 0.5625) * Math.Exp((z - x) * (z + x) + r / ss);
        }

        private static double Erf(double x, out MathErr err)
        {
            err = MathErr.None;
            int hx = HighWord(x);
            int ix = hx & 0x7fffffff;
            if (ix >= 0x7ff00000)   // erf(nan) = nan, erf(+-inf) = +-1
                return (double)(1 - ((int)((uint)hx >> 31) << 1)) + 1.0 / x;
            if (ix < 0x3feb0000)   // |x| < 0.84375
            {
                if (ix < 0x3e300000)   // |x| < 2**-28
                {
                    if (ix < 0x00800000)
                        return 0.125 * (8.0 * x + Efx8 * x);   // avoids underflow
                    return x + Efx * x;
                }
                double z = x * x;
                double r = Pp0 + z * (Pp1 + z * (Pp2 + z * (Pp3 + z * Pp4)));
                double s = 1.0 + z * (Qq1 + z * (Qq2 + z * (Qq3 + z * (Qq4 + z * Qq5))));
                return x + x * (r / s);
            }
            if (ix < 0x3ff40000)   // 0.84375 <= |x| < 1.25
            {
                double s = Math.Abs(x) - 1.0;
                double p = Pa0 + s * (Pa1 + s * (Pa2 + s * (Pa3 + s * (Pa4 + s * (Pa5 + s * Pa6)))));
                double q = 1.0 + s * (Qa1 + s * (Qa2 + s * (Qa3 + s * (Qa4 + s * (Qa5 + s * Qa6)))));
                return hx >= 0 ? Erx + p / q : -Erx - p / q;
            }
            if (ix >= 0x40180000)   // |x| >= 6
                return hx >= 0 ? 1.0 - Tiny : Tiny - 1.0;
            double ax = Math.Abs(x);
            double t = ErfcTail(ax, ix < 0x4006DB6E);
            return hx >= 0 ? 1.0 - t / ax : t / ax - 1.0;
        }

        private static double Erfc(double x, out MathErr err)
        {
            err = MathErr.None;
            int hx = HighWord(x);
            int ix = hx & 0x7fffffff;
            if (ix >= 0x7ff00000)   // erfc(nan) = nan, erfc(+inf) = 0, erfc(-inf) = 2
                return (double)((int)((uint)hx >> 31) << 1) + 1.0 / x;
            if (ix < 0x3feb0000)   // |x| < 0.84375
            {
                if (ix < 0x3c700000)   // |x| < 2**-56
                    return 1.0 - x;
                double z = x * x;
                double r = Pp0 + z * (Pp1 + z * (Pp2 + z * (Pp3 + z * Pp4)));
                double s = 1.0 + z * (Qq1 + z * (Qq2 + z * (Qq3 + z * (Qq4 + z * Qq5))));
                double y = r / s;
                if (hx < 0x3fd00000)   // x < 1/4
                    return 1.0 - (x + x * y);
                double rr = x * y;
                rr += x - 0.5;
                return 0.5 - rr;
            }
            if (ix < 0x3ff40000)   // 0.84375 <= |x| < 1.25
            {
                double s = Math.Abs(x) - 1.0;
                double p = Pa0 + s * (Pa1 + s * (Pa2 + s * (Pa3 + s * (Pa4 + s * (Pa5 + s * Pa6)))));
                double q = 1.0 + s * (Qa1 + s * (Qa2 + s * (Qa3 + s * (Qa4 + s * (Qa5 + s * Qa6)))));
                if (hx >= 0)
                    return (1.0 - Erx) - p / q;
                return 1.0 + (Erx + p / q);
            }
            if (ix < 0x403c0000)   // |x| < 28
            {
                if (hx < 0 && ix >= 0x40180000)   // x <= -6
                    return 2.0 - Tiny;
                double ax = Math.Abs(x);
                double t = ErfcTail(ax, ix < 0x4006DB6D);
                return hx > 0 ? t / ax : 2.0 - t / ax;
            }
            return hx > 0 ? Tiny * Tiny : 2.0 - Tiny;
        }
    }
}
