using System;
using System.Globalization;
using System.Threading;
using Outbridge.PyLite.Modules.Support;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class FloatReprTests
    {
        [Theory]
        [InlineData(0.1, "0.1")]
        [InlineData(2.0, "2.0")]
        [InlineData(-2.0, "-2.0")]
        [InlineData(1e16, "1e+16")]
        [InlineData(9999999999999998.0, "9999999999999998.0")]
        [InlineData(0.0001, "0.0001")]
        [InlineData(0.00001, "1e-05")]
        [InlineData(123456789.123, "123456789.123")]
        [InlineData(1e299, "1e+299")]
        [InlineData(1.7976931348623157e308, "1.7976931348623157e+308")]
        [InlineData(9.999e-5, "9.999e-05")]
        public void Cases(double d, string expected) => Assert.Equal(expected, FloatRepr.Repr(d));

        [Fact] public void PointOnePlusPointTwo() => Assert.Equal("0.30000000000000004", FloatRepr.Repr(0.1 + 0.2));
        [Fact] public void NegativeZero() => Assert.Equal("-0.0", FloatRepr.Repr(-0.0));
        [Fact] public void PositiveZero() => Assert.Equal("0.0", FloatRepr.Repr(0.0));

        [Fact]
        public void Specials()
        {
            Assert.Equal("inf", FloatRepr.Repr(double.PositiveInfinity));
            Assert.Equal("-inf", FloatRepr.Repr(double.NegativeInfinity));
            Assert.Equal("nan", FloatRepr.Repr(double.NaN));
        }

        [Fact] public void SmallestSubnormal() => Assert.Equal("5e-324", FloatRepr.Repr(double.Epsilon));

        [Fact]
        public void CultureInvariant()
        {
            var prev = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
                Assert.Equal("0.1", FloatRepr.Repr(0.1));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = prev;
            }
        }

        [Fact]
        public void RoundTripProperty()
        {
            var rnd = new Random(20260707);
            var buf = new byte[8];
            for (int i = 0; i < 100_000; i++)
            {
                rnd.NextBytes(buf);
                double d = BitConverter.Int64BitsToDouble(BitConverter.ToInt64(buf, 0));
                if (double.IsNaN(d) || double.IsInfinity(d))
                    continue;
                double back;   // read back as CPython does (.NET Framework's double.Parse can miss by an ulp)
                Assert.True(NearestDouble.TryParse(FloatRepr.Repr(d), out back));
                Assert.Equal(BitConverter.DoubleToInt64Bits(d), BitConverter.DoubleToInt64Bits(back));
            }
        }

        // The power caches are process-wide: a script-chosen precision past the cached range is computed per
        // call and never kept (format(1.5, ".20000f") used to leave 84 MB behind for every later run, B7).
        [Fact]
        public void APrecisionPastTheCacheIsNotKept()
        {
            Assert.Equal(System.Numerics.BigInteger.Pow(10, 5000), FloatRepr.Pow10(5000));
            Assert.Equal(System.Numerics.BigInteger.Pow(5, 5000), FloatRepr.Pow5(5000));
            var f = typeof(FloatRepr).GetField("_pow10", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.True(((System.Numerics.BigInteger[])f.GetValue(null)).Length <= 1401);
            string s = Outbridge.PyLite.Tests.Evaluator.Ev.Run("r = format(1.5, '.3000f')\n").Repr("r");
            Assert.Equal(3000 + 4, s.Length);
            Assert.StartsWith("'1.5000", s);
        }

        // Past a float's own decimal digits the fixed form is zeros: padded, not computed as 10**precision
        // (format(1.0, '.1000000f') used to run on past its deadline).
        [Theory]
        [InlineData(0.1, 60, "0.100000000000000005551115123125782702118158340454101562500000")]
        [InlineData(1152921504606846976.0, 3, "1152921504606846976.000")]
        [InlineData(-0.5, 5, "-0.50000")]
        [InlineData(5e-324, 1080, null)]
        [InlineData(1.0, 1000000, null)]
        public void FixedPastTheExactDigitsPadsZeros(double v, int precision, string expected)
        {
            string s = FloatRepr.FormatFixed(Rt.Ctx(), v, precision, false);
            if (expected != null)
                Assert.Equal(expected, s);
            Assert.Equal(precision, s.Length - s.IndexOf('.') - 1);
        }

        // ReprDigits gives the digits an exact search gives, over random bit patterns of every exponent,
        // subnormals, short decimals and the range ends.
        [Fact]
        public void ShortestDigitsMatchThePrecisionSearch()
        {
            var rnd = new Random(20261028);
            var buf = new byte[8];
            int mismatches = 0;
            string first = null;
            for (int i = 0; i < 60000; i++)
            {
                double d;
                switch (i % 4)
                {
                    case 0:
                        rnd.NextBytes(buf);
                        d = BitConverter.ToDouble(buf, 0);
                        break;
                    case 1:
                        d = rnd.Next(-1000000, 1000000) / Math.Pow(10, rnd.Next(0, 12));
                        break;
                    case 2:
                        d = BitConverter.Int64BitsToDouble(rnd.Next(1, int.MaxValue) * (long)rnd.Next(1, 3));   // subnormal
                        break;
                    default:
                        d = (rnd.NextDouble() - 0.5) * Math.Pow(10, rnd.Next(-300, 300));
                        break;
                }
                if (double.IsNaN(d) || double.IsInfinity(d) || d == 0.0)
                    continue;
                bool neg;
                int exp;
                string got = FloatRepr.ShortestDigits(d, out neg, out exp);
                int oexp;
                string want = SearchDigits(d, out oexp);
                if (got != want || exp != oexp)
                {
                    mismatches++;
                    first = first ?? d.ToString("R", CultureInfo.InvariantCulture) + ": " + got + "e" + exp + " vs " + want + "e" + oexp;
                }
            }
            foreach (double d in new[] { double.MaxValue, double.Epsilon, 2.2250738585072014e-308, 0.1, 1.0 / 3, 9007199254740993.0, 5e-324 })
            {
                bool neg;
                int exp, oexp;
                Assert.Equal(SearchDigits(d, out oexp), FloatRepr.ShortestDigits(d, out neg, out exp));
                Assert.Equal(oexp, exp);
            }
            Assert.True(mismatches == 0, mismatches + " mismatches, first " + first);
        }

        // The short-decimal fast path (k <= 8 places, at most 15 digits) and its edges answer as the search does.
        [Fact]
        public void ShortDecimalsMatchThePrecisionSearch()
        {
            var rnd = new Random(20261101);
            var values = new System.Collections.Generic.List<double>
            {
                1e-4, 9.999999999999999e-5, 1.0000000000000001e-4, 999999999999999.0, 1e15, 0.1 + 0.2, 123456789012345.6,
                99999999.99999999, 0.30000000000000004, 1234567.890123, 5e-5, 7.0, 1200.0, 0.001,
            };
            for (int i = 0; i < 40000; i++)
            {
                long n = (long)(rnd.NextDouble() * Math.Pow(10, rnd.Next(1, 16)));
                values.Add((i % 2 == 0 ? n : -n) / Math.Pow(10, rnd.Next(0, 10)));
            }
            foreach (double d in values)
            {
                if (d == 0.0)
                    continue;
                bool neg;
                int exp, oexp;
                string want = SearchDigits(d, out oexp);
                string got = FloatRepr.ShortestDigits(d, out neg, out exp);
                Assert.True(want == got && oexp == exp, d.ToString("R", CultureInfo.InvariantCulture) + ": " + got + "e" + exp + " vs " + want + "e" + oexp);
            }
        }

        // The reference, CPython's rule from first principles: from one digit up, the digits of the EXACT
        // decimal expansion truncated and one unit up, each read back; the nearer when both read back, the
        // even one on an exact tie. No .NET rendering involved (.NET Framework rounds its 17 digits again).
        private static string SearchDigits(double d, out int decExp)
        {
            long bits = BitConverter.DoubleToInt64Bits(d) & long.MaxValue;
            long field = bits >> 52;
            long frac = bits & 0xFFFFFFFFFFFFFL;
            System.Numerics.BigInteger m = field == 0 ? frac : frac | (1L << 52);
            int e = field == 0 ? -1074 : (int)field - 1075;
            string exact = (e >= 0 ? m << e : m * System.Numerics.BigInteger.Pow(5, -e)).ToString(CultureInfo.InvariantCulture);
            int firstExp = exact.Length - 1 + Math.Min(e, 0);
            for (int p = 1; p <= 17; p++)
            {
                System.Numerics.BigInteger t = System.Numerics.BigInteger.Parse(
                    p <= exact.Length ? exact.Substring(0, p) : exact + new string('0', p - exact.Length), CultureInfo.InvariantCulture);
                string tail = p < exact.Length ? exact.Substring(p) : "";
                int q = firstExp - (p - 1);
                bool down = ReadsBack(t, q, bits), up = ReadsBack(t + 1, q, bits);
                if (!down && !up)
                    continue;
                if (down && up)
                {
                    int c = tail.Length == 0 ? -1 : tail[0].CompareTo('5');
                    if (c == 0)
                        c = tail.Substring(1).Trim('0').Length > 0 ? 1 : (t.IsEven ? -1 : 1);
                    up = c > 0;
                }
                string s = (up ? t + 1 : t).ToString(CultureInfo.InvariantCulture);
                decExp = firstExp + (s.Length - p);
                return s.TrimEnd('0');
            }
            throw new InvalidOperationException("17 digits always read back");
        }

        private static bool ReadsBack(System.Numerics.BigInteger t, int q, long bits)
        {
            double v;
            return NearestDouble.TryParse(t.ToString(CultureInfo.InvariantCulture) + "e" + q.ToString(CultureInfo.InvariantCulture), out v)
                && BitConverter.DoubleToInt64Bits(v) == bits;
        }
    }
}
