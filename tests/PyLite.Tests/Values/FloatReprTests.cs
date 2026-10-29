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
                double back = double.Parse(FloatRepr.Repr(d), CultureInfo.InvariantCulture);
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

        // P11: three conversions (E14, E15, E16) give the digits the 1..17 precision search gave, over random
        // bit patterns of every exponent, subnormals, short decimals and the range ends.
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

        // The pre-P11 search: the smallest precision whose E-format string parses back to the same bits.
        private static string SearchDigits(double d, out int decExp)
        {
            long bits = BitConverter.DoubleToInt64Bits(d);
            string sci = null;
            for (int prec = 1; prec <= 17 && sci == null; prec++)
            {
                string cand = d.ToString("E" + (prec - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                double parsed;
                if (double.TryParse(cand, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                    && BitConverter.DoubleToInt64Bits(parsed) == bits)
                    sci = cand;
            }
            sci = sci ?? d.ToString("E16", CultureInfo.InvariantCulture);
            bool neg = sci[0] == '-';
            int eIdx = sci.IndexOf('E');
            decExp = int.Parse(sci.Substring(eIdx + 1), CultureInfo.InvariantCulture);
            return sci.Substring(neg ? 1 : 0, eIdx - (neg ? 1 : 0)).Replace(".", "");
        }
    }
}
