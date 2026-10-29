using System;
using System.Globalization;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Xunit;

namespace Outbridge.PyLite.Tests.Builtins
{
    // impl/06 T-BLT-03: exact-decimal round() and the fixed/scientific/general renderers.
    [Trait("Category", "Compatibility")]
    public class FloatReprFormatTests
    {
        [Theory]
        [InlineData(0.1, "0.1")]
        [InlineData(2.0, "2.0")]
        [InlineData(1e16, "1e+16")]
        [InlineData(1e15, "1000000000000000.0")]
        [InlineData(0.0001, "0.0001")]
        [InlineData(0.00001, "1e-05")]
        [InlineData(1.0 / 3.0, "0.3333333333333333")]
        public void Repr(double v, string expected)
        {
            Assert.Equal(expected, FloatRepr.Repr(v));
        }

        [Fact]
        public void ReprSpecials()
        {
            Assert.Equal("-0.0", FloatRepr.Repr(-0.0));
            Assert.Equal("inf", FloatRepr.Repr(double.PositiveInfinity));
            Assert.Equal("-inf", FloatRepr.Repr(double.NegativeInfinity));
            Assert.Equal("nan", FloatRepr.Repr(double.NaN));
            Assert.Equal("5e-324", FloatRepr.Repr(double.Epsilon));   // smallest subnormal
        }

        [Fact]
        public void Decompose()
        {
            bool neg;
            ulong mant;
            int e2;
            // IEEE decomposition uses the full 53-bit mantissa: 1.5 = (2^52 + 2^51) * 2^-52.
            FloatRepr.Decompose(1.5, out neg, out mant, out e2);
            Assert.False(neg);
            Assert.Equal(1.5, (double)mant * Math.Pow(2.0, e2));
            FloatRepr.Decompose(-0.0, out neg, out mant, out e2);
            Assert.True(neg);
            Assert.Equal(0UL, mant);
        }

        [Theory]
        [InlineData(2.675, 2, 2.67)]
        [InlineData(0.125, 2, 0.12)]
        [InlineData(2.5, 0, 2.0)]
        [InlineData(3.5, 0, 4.0)]
        [InlineData(1234.5678, -2, 1200.0)]
        public void RoundToDigits(double v, int n, double expected)
        {
            Assert.Equal(expected, FloatRepr.RoundToDigits(Rt.Ctx(), v, n));
        }

        [Fact]
        public void RoundNegativeZeroKeepsSign()
        {
            double r = FloatRepr.RoundToDigits(Rt.Ctx(), -0.001, 1);
            Assert.Equal(0.0, r);
            Assert.True(FloatRepr.IsNegativeZero(r));
        }

        [Fact]
        public void RoundInfNanPassThrough()
        {
            var ctx = Rt.Ctx();
            Assert.True(double.IsNaN(FloatRepr.RoundToDigits(ctx, double.NaN, 2)));
            Assert.True(double.IsPositiveInfinity(FloatRepr.RoundToDigits(ctx, double.PositiveInfinity, 2)));
        }

        [Fact]
        public void RoundOverflowThrows()
        {
            Assert.Throws<ScriptException>(() => FloatRepr.RoundToDigits(Rt.Ctx(), 1.7e308, -308));
        }

        [Theory]
        [InlineData(2.675, 2, "2.67")]
        [InlineData(0.0, 2, "0.00")]
        [InlineData(12.0, 0, "12")]
        [InlineData(3.14159, 4, "3.1416")]
        public void FormatFixed(double v, int p, string expected)
        {
            Assert.Equal(expected, FloatRepr.FormatFixed(Rt.Ctx(), v, p, false));
        }

        [Fact]
        public void FormatFixedNegativeZero()
        {
            Assert.Equal("-0.00", FloatRepr.FormatFixed(Rt.Ctx(), -0.0, 2, false));
        }

        [Fact]
        public void FormatFixedAltFormKeepsPoint()
        {
            Assert.Equal("12.", FloatRepr.FormatFixed(Rt.Ctx(), 12.0, 0, true));
        }

        [Theory]
        [InlineData(12345.678, 3, false, "1.235e+04")]
        [InlineData(999.95, 1, false, "1.0e+03")]
        [InlineData(12345.678, 3, true, "1.235E+04")]
        [InlineData(0.0, 2, false, "0.00e+00")]
        public void FormatScientific(double v, int p, bool upper, string expected)
        {
            Assert.Equal(expected, FloatRepr.FormatScientific(Rt.Ctx(), v, p, upper, false));
        }

        [Theory]
        [InlineData(0.00001234, 6, "1.234e-05")]
        [InlineData(100.0, 6, "100")]
        [InlineData(123456789.0, 6, "1.23457e+08")]
        public void FormatGeneral(double v, int p, string expected)
        {
            Assert.Equal(expected, FloatRepr.FormatGeneral(Rt.Ctx(), v, p, false, false, false));
        }

        [Fact]
        public void FormatGeneralAltFormKeepsZeros()
        {
            Assert.Equal("100.000", FloatRepr.FormatGeneral(Rt.Ctx(), 100.0, 6, false, true, false));
        }

        [Fact]
        public void ReprRoundTripFuzz()
        {
            var rng = new Random(12345);
            var buf = new byte[8];
            int chec0 = 0;
            for (int i = 0; i < 200000; i++)
            {
                rng.NextBytes(buf);
                double v = BitConverter.Int64BitsToDouble(BitConverter.ToInt64(buf, 0));
                if (double.IsNaN(v) || double.IsInfinity(v))
                    continue;
                double parsed = double.Parse(FloatRepr.Repr(v), NumberStyles.Float, CultureInfo.InvariantCulture);
                Assert.Equal(BitConverter.DoubleToInt64Bits(v), BitConverter.DoubleToInt64Bits(parsed));
                chec0++;
            }
            Assert.True(chec0 > 100000);
        }
    }
}
