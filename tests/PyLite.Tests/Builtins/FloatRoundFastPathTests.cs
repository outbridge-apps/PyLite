using System;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Xunit;

namespace Outbridge.PyLite.Tests.Builtins
{
    // RoundToDigits' fast band (0 <= n <= 22, 1e-290 <= |v| < 2^52, |v*10^n| < 2^53) must be
    // bit-identical to an INDEPENDENT exact oracle: raw-bits decomposition, BigInteger half-even of
    // v*10^n, then one IEEE division by the exact double 10^n. (The oracle deliberately does not
    // reuse the engine's exact-decimal path: net472 double.Parse can misround by 1 ulp, which this
    // battery caught in the string-based path — in-band values must never be settled by Parse.)
    [Trait("Category", "Compatibility")]
    public class FloatRoundFastPathTests
    {
        // Half-even(v * 10^n) / 10^n, all decisions in BigInteger. Valid only inside the band.
        private static double Oracle(double v, int n)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            bool neg = bits < 0;
            int exp = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            BigInteger mant = exp == 0 ? frac : frac | (1L << 52);
            int e2 = exp == 0 ? -1074 : exp - 1075;   // |v| = mant * 2^e2
            BigInteger num = mant * BigInteger.Pow(5, n);
            int k = -e2;
            BigInteger q;
            if (n >= k)
                q = num << (n - k);
            else
            {
                BigInteger den = BigInteger.One << (k - n);
                BigInteger rem;
                q = BigInteger.DivRem(num, den, out rem);
                if (rem * 2 > den || (rem * 2 == den && !q.IsEven))
                    q += 1;
            }
            if (q.IsZero)
                return neg ? -0.0 : 0.0;
            double r = (double)q / (double)BigInteger.Pow(10, n);   // both exact; one IEEE rounding
            return neg ? -r : r;
        }

        // Mirror of the engine's fast-band guard; oracle comparisons only make sense inside it.
        private static bool InBand(double v, int n)
        {
            if (n < 0 || n > 22 || double.IsNaN(v) || double.IsInfinity(v))
                return false;
            double av = v < 0 ? -v : v;
            if (av < 1e-290 || av >= 4503599627370496.0)
                return false;
            double hi = v * FloatRepr.ExactPow10[n];
            return hi > -9007199254740992.0 && hi < 9007199254740992.0;
        }

        private static void AssertOracle(double v, int n)
        {
            if (!InBand(v, n))
                return;
            long got = BitConverter.DoubleToInt64Bits(FloatRepr.RoundToDigits(Rt.Ctx(), v, n));
            long want = BitConverter.DoubleToInt64Bits(Oracle(v, n));
            if (got != want)
                Assert.Fail("round(" + v.ToString("R") + ", " + n + "): engine "
                    + BitConverter.Int64BitsToDouble(got).ToString("R") + " != oracle "
                    + BitConverter.Int64BitsToDouble(want).ToString("R"));
        }

        // CPython-verified midpoint nasties (2.675 is really 2.67499...; 0.125/0.375 are exact ties).
        // The last case is the Parse-misround regression the oracle battery originally caught.
        [Theory]
        [InlineData(2.675, 2, 2.67)]
        [InlineData(-2.675, 2, -2.67)]
        [InlineData(0.125, 2, 0.12)]
        [InlineData(0.375, 2, 0.38)]
        [InlineData(-0.125, 2, -0.12)]
        [InlineData(1.005, 2, 1.0)]
        [InlineData(2.5, 0, 2.0)]
        [InlineData(-2.5, 0, -2.0)]
        [InlineData(1.5, 0, 2.0)]
        [InlineData(0.5, 0, 0.0)]
        [InlineData(2.6755, 3, 2.675)]   // like 2.675: the double sits just below the decimal
        [InlineData(1e15 + 0.5, 0, 1e15)]                  // exact .5 tie -> even neighbour
        [InlineData(0.30000000000000004, 1, 0.3)]
        [InlineData(0.41103349355122337, 16, 0.41103349355122337)]
        public void KnownValues(double v, int n, double expected)
        {
            Assert.Equal(expected, FloatRepr.RoundToDigits(Rt.Ctx(), v, n));
            AssertOracle(v, n);
        }

        [Fact]
        public void NegativeZeroSignSurvivesTheFastPath()
        {
            var ctx = Rt.Ctx();
            Assert.True(FloatRepr.IsNegativeZero(FloatRepr.RoundToDigits(ctx, -0.003, 2)));
            Assert.False(FloatRepr.IsNegativeZero(FloatRepr.RoundToDigits(ctx, 0.003, 2)));
            Assert.True(FloatRepr.IsNegativeZero(FloatRepr.RoundToDigits(ctx, -0.4, 0)));
        }

        // Random doubles from raw bit patterns (all magnitudes; out-of-band ones are skipped).
        [Fact]
        public void CrossCheckRandomBits()
        {
            var rnd = new Random(20260710);
            var buf = new byte[8];
            for (int i = 0; i < 60000; i++)
            {
                rnd.NextBytes(buf);
                AssertOracle(BitConverter.ToDouble(buf, 0), rnd.Next(0, 23));
            }
        }

        // Realistic magnitudes: uniform mantissa scaled across the ranges scripts actually use.
        [Fact]
        public void CrossCheckRealisticRanges()
        {
            var rnd = new Random(4711);
            double[] scales = { 1e-6, 1e-3, 1.0, 1e3, 1e6, 1e9, 1e12, 1e15 };
            foreach (double scale in scales)
                for (int i = 0; i < 20000; i++)
                    AssertOracle((rnd.NextDouble() * 2.0 - 1.0) * scale, rnd.Next(0, 23));
        }

        // Adversarial: values ON or within a few ulps of a midpoint — exact ties must go half-even
        // (the in-band BigInteger tie path), off-by-ulps neighbours must round to the correct side.
        [Fact]
        public void CrossCheckNearMidpoints()
        {
            var rnd = new Random(1729);
            for (int i = 0; i < 20000; i++)
            {
                int n = rnd.Next(0, 18);
                long k = (long)(rnd.NextDouble() * 4503599627370496.0);   // < 2^52
                double mid = (k + 0.5) / FloatRepr.ExactPow10[n];
                for (int ulps = -3; ulps <= 3; ulps++)
                {
                    long bits = BitConverter.DoubleToInt64Bits(mid);
                    double v = BitConverter.Int64BitsToDouble(bits + ulps);
                    if (double.IsNaN(v) || double.IsInfinity(v))
                        continue;
                    AssertOracle(v, n);
                    AssertOracle(-v, n);
                }
            }
        }

        // hi = v*10^n in [2^52, 2^53): hi is integral, ulp(hi)=1, the true tie hides at frac = -0.5.
        [Fact]
        public void CrossCheckLargeMagnitudeBand()
        {
            var rnd = new Random(853);
            for (int i = 0; i < 20000; i++)
            {
                int n = rnd.Next(1, 23);
                double v = 4503599627370496.0 * (1.0 + rnd.NextDouble()) / FloatRepr.ExactPow10[n];
                AssertOracle(rnd.Next(2) == 0 ? v : -v, n);
            }
        }
    }
}
