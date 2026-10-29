using System;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules.Support
{
    // impl/10 T-MNM-01/02: FloatMath bit-level primitives + libm helpers.
    [Trait("Category", "Compatibility")]
    public class FloatMathTests
    {
        private static long Bits(double d) { return BitConverter.DoubleToInt64Bits(d); }

        [Fact]
        public void FrexpKnown()
        {
            double m; int e;
            FloatMath.Frexp(8.0, out m, out e);
            Assert.Equal((0.5, 4), (m, e));
            FloatMath.Frexp(0.5, out m, out e);
            Assert.Equal((0.5, 0), (m, e));
        }

        [Fact]
        public void FrexpNegZeroPreservesSign()
        {
            double m; int e;
            FloatMath.Frexp(-0.0, out m, out e);
            Assert.Equal(Bits(-0.0), Bits(m));
            Assert.Equal(0, e);
        }

        [Fact]
        public void FrexpSubnormalRoundTripBitExact()
        {
            double m; int e;
            FloatMath.Frexp(5e-324, out m, out e);
            Assert.Equal(Bits(5e-324), Bits(FloatMath.Ldexp(m, e)));
        }

        [Fact]
        public void FrexpInfinity()
        {
            double m; int e;
            FloatMath.Frexp(double.PositiveInfinity, out m, out e);
            Assert.True(double.IsPositiveInfinity(m));
            Assert.Equal(0, e);
        }

        [Fact]
        public void LdexpKnownAndOverflow()
        {
            Assert.Equal(8.0, FloatMath.Ldexp(0.5, 4));
            double r;
            Assert.True(FloatMath.LdexpOverflowed(1.0, 1024, out r));
            Assert.False(FloatMath.LdexpOverflowed(1.0, -1100, out r));
            Assert.Equal(0.0, r);
        }

        [Fact]
        public void LdexpFrexpRoundTrip()
        {
            var vals = new[] { 1.0, -3.5, 8.0, 0.1, 1e300, 1e-300, 5e-324, Math.PI, 123456.789, -2.5e-100 };
            for (int i = 0; i < 1000; i++)
            {
                double v = vals[i % vals.Length] * (1 + (i % 7));
                double m; int e;
                FloatMath.Frexp(v, out m, out e);
                Assert.Equal(Bits(v), Bits(FloatMath.Ldexp(m, e)));
            }
        }

        [Fact]
        public void CopySignAndNegZero()
        {
            Assert.Equal(-1.0, FloatMath.CopySign(1.0, -0.0));
            Assert.Equal(3.0, FloatMath.CopySign(-3.0, 2.0));
            Assert.True(FloatMath.IsNegZero(-0.0));
            Assert.False(FloatMath.IsNegZero(0.0));
        }

        [Fact]
        public void FmodUsesTruncatedRemainder()
        {
            Assert.Equal(2.0, FloatMath.Fmod(5.0, 3.0));
            Assert.Equal(-2.0, FloatMath.Fmod(-5.0, 3.0));
        }

        [Fact]
        public void HypotScaledNoOverflow()
        {
            double h = FloatMath.Hypot(1e308, 1e308);
            Assert.False(double.IsInfinity(h));
            Assert.Equal(Math.Sqrt(2.0) * 1e308, h, 3);
            Assert.True(double.IsPositiveInfinity(FloatMath.Hypot(double.PositiveInfinity, double.NaN)));
            Assert.Equal(5.0, FloatMath.Hypot(3.0, 4.0));
        }

        [Fact]
        public void Expm1SmallAndSpecial()
        {
            Assert.True(FloatMath.Expm1(1e-10) > 1e-10);   // not flushed to 0
            Assert.Equal(1e-10, FloatMath.Expm1(1e-10), 15);
            Assert.Equal(0L, Bits(FloatMath.Expm1(0.0)));
            Assert.Equal(-1.0, FloatMath.Expm1(double.NegativeInfinity));
        }

        [Fact]
        public void Log1pSmallAndSpecial()
        {
            Assert.True(FloatMath.Log1p(1e-10) > 0.0 && FloatMath.Log1p(1e-10) < 1e-9);
            Assert.True(double.IsNegativeInfinity(FloatMath.Log1p(-1.0)));
            Assert.Equal(Bits(-0.0), Bits(FloatMath.Log1p(-0.0)));
        }

        [Fact]
        public void InverseHyperbolics()
        {
            Assert.Equal(0.8813735870195430, FloatMath.Asinh(1.0), 15);
            Assert.True(double.IsNegativeInfinity(FloatMath.Asinh(double.NegativeInfinity)));
            Assert.Equal(0.0, FloatMath.Acosh(1.0));
            Assert.True(double.IsNaN(FloatMath.Acosh(0.5)));
            Assert.Equal(0.5493061443340548, FloatMath.Atanh(0.5), 15);
            Assert.True(double.IsNaN(FloatMath.Atanh(1.0)));
            Assert.Equal(Bits(-0.0), Bits(FloatMath.Atanh(-0.0)));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(255, 8)]
        [InlineData(256, 9)]
        public void BitLengthKnown(int n, int expected)
        {
            Assert.Equal(expected, NumericOps.BitLength(n));
        }

        [Fact]
        public void BitLengthLargePower()
        {
            Assert.Equal(401, NumericOps.BitLength(BigInteger.Pow(2, 400)));
            Assert.True(BigIntMath.IsPowerOfTwo(BigInteger.Pow(2, 400)));
            Assert.False(BigIntMath.IsPowerOfTwo(BigInteger.Pow(2, 400) + 1));
        }
    }
}
