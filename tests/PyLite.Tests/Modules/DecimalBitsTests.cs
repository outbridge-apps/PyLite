using System;
using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // The overlay that reads a decimal's words without decimal.GetBits, and the 32-bit rounding built on it.
    [Trait("Category", "Compatibility")]
    public class DecimalBitsTests
    {
        [Fact]
        public void OverlayIsInUseOnThisRuntime()
        {
            Assert.True(DecimalBits.UsesOverlay);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("1")]
        [InlineData("-1")]
        [InlineData("1.5")]
        [InlineData("1234.5678")]
        [InlineData("0.0000000000000000000000000000")]
        [InlineData("-0.0000000000000000000000000001")]
        [InlineData("79228162514264337593543950335")]
        [InlineData("-79228162514264337593543950335")]
        [InlineData("4294967296")]
        [InlineData("18446744073709551616")]
        public void ReadAgreesWithGetBits(string text)
        {
            decimal d = decimal.Parse(text, CultureInfo.InvariantCulture);
            int[] b = decimal.GetBits(d);
            uint lo, mid, hi;
            int scale;
            bool neg;
            DecimalBits.Read(d, out lo, out mid, out hi, out scale, out neg);
            Assert.Equal(unchecked((uint)b[0]), lo);
            Assert.Equal(unchecked((uint)b[1]), mid);
            Assert.Equal(unchecked((uint)b[2]), hi);
            Assert.Equal((b[3] >> 16) & 0xFF, scale);
            Assert.Equal(b[3] < 0, neg);
        }

        [Theory]
        [InlineData(0UL, 0U, 0U, 5, true)]
        [InlineData(99999UL, 0U, 0U, 5, true)]
        [InlineData(100000UL, 0U, 0U, 5, false)]
        [InlineData(0UL, 0U, 1U, 28, true)]                // 2^64 has 20 digits
        [InlineData(0x10000000UL, 0x3E250261U, 0x204FCE5EU, 28, false)]   // exactly 10^28
        [InlineData(0x0FFFFFFFUL, 0x3E250261U, 0x204FCE5EU, 28, true)]    // 10^28 - 1
        public void FitsPrecCountsMantissaDigits(ulong lo, uint mid, uint hi, int prec, bool fits)
        {
            Assert.Equal(fits, DecimalBits.FitsPrec((uint)lo, mid, hi, prec));
        }

        // The hand-rolled 96-bit rounding must agree with the BigInteger engine in every mode, for every
        // drop it accepts, on mantissas of every width - so it is checked against it at random.
        [Fact]
        public void TryRoundMatchesTheBigIntegerReference()
        {
            var modes = (RoundingMode[])Enum.GetValues(typeof(RoundingMode));
            var rnd = new Random(20260909);
            var bytes = new byte[13];   // 12 words + a zero sign byte keeps the BigInteger positive
            for (int i = 0; i < 3000; i++)
            {
                rnd.NextBytes(bytes);
                bytes[12] = 0;
                int width = i % 4;      // 0: full 96 bits, 1: 64, 2: 32, 3: tiny
                if (width >= 1)
                    Array.Clear(bytes, 8, 4);
                if (width >= 2)
                    Array.Clear(bytes, 4, 4);
                if (width == 3)
                    Array.Clear(bytes, 1, 3);
                var m = new BigInteger(bytes);
                int drop = 1 + rnd.Next(9);
                bool neg = rnd.Next(2) == 0;
                BigInteger divisor = BigInteger.Pow(10, drop);
                foreach (RoundingMode mode in modes)
                {
                    BigInteger r;
                    BigInteger q = BigInteger.DivRem(m, divisor, out r);
                    BigInteger expected = DecimalRound.Apply(q, r, divisor, neg, mode);
                    uint lo = (uint)(m & 0xFFFFFFFF), mid = (uint)((m >> 32) & 0xFFFFFFFF), hi = (uint)((m >> 64) & 0xFFFFFFFF);
                    bool ok = DecimalBits.TryRound(ref lo, ref mid, ref hi, drop, neg, mode);
                    Assert.True(ok, "drop " + drop + " mode " + mode + " m " + m);
                    BigInteger got = ((BigInteger)hi << 64) | ((BigInteger)mid << 32) | lo;
                    Assert.True(expected == got, "drop " + drop + " mode " + mode + " m " + m + ": " + got + " != " + expected);
                }
            }
        }

        [Fact]
        public void TryRoundRefusesWhatItCannotDo()
        {
            uint lo = 1, mid = 0, hi = 0;
            Assert.False(DecimalBits.TryRound(ref lo, ref mid, ref hi, 10, false, RoundingMode.HalfEven));
        }
    }
}
