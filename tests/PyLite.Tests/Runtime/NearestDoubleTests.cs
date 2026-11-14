using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // Text, int and Decimal to float, and float to repr, as CPython answers (every expected value below is
    // CPython 3.14's). .NET Framework's double.Parse missed by an ulp on 8.6% of a hard 760,000-text battery
    // (subnormals, 16+ digits, midpoints), its 15- and 16-digit renderings round the 17-digit one again,
    // so repr was a digit off for 2.3% of everyday doubles, and (double)BigInteger truncates.
    [Trait("Category", "Regression")]
    public class NearestDoubleTests
    {
        [Theory]
        [InlineData("1.1283791670955e-310", "0x0.014c5898977c4p-1022")]
        [InlineData("2.2250738585072011e-308", "0x0.fffffffffffffp-1022")]
        [InlineData("2.2250738585072012e-308", "0x1.0000000000000p-1022")]
        [InlineData("4.9406564584124654e-324", "0x0.0000000000001p-1022")]
        [InlineData("2.4703282292062327e-324", "0x0.0p+0")]
        [InlineData("2.4703282292062328e-324", "0x0.0000000000001p-1022")]
        [InlineData("1.7976931348623157e308", "0x1.fffffffffffffp+1023")]
        [InlineData("1.7976931348623158e308", "0x1.fffffffffffffp+1023")]
        [InlineData("1.7976931348623159e308", "inf")]
        [InlineData("9007199254740993", "0x1.0000000000000p+53")]
        [InlineData("9007199254740995", "0x1.0000000000002p+53")]
        [InlineData("123456789012345678901234567890e-30", "0x1.f9add3746f65fp-4")]
        [InlineData("1e23", "0x1.52d02c7e14af6p+76")]
        [InlineData("8.98846567431158e307", "0x1.0000000000000p+1023")]
        [InlineData("4.4501477170144023e-308", "0x1.fffffffffffffp-1022")]
        [InlineData("7.0e-10", "0x1.80d43de9cc603p-31")]
        [InlineData("0.1000000000000000055511151231257827021181583404541015625", "0x1.999999999999ap-4")]
        public void FloatOfTextIsTheNearestDouble(string text, string hex)
        {
            Assert.Equal("'" + hex + "'", Ev.Run("x = float('" + text + "')\nr = x.hex() if x == x and abs(x) != float('inf') else repr(x)\n").Repr("r"));
            string literal = text.IndexOfAny(new[] { '.', 'e' }) >= 0 ? text : text + ".0";   // a float literal, not an int
            Assert.Equal("'" + hex + "'", Ev.Run("x = " + literal + "\nr = x.hex() if abs(x) != float('inf') else repr(x)\n").Repr("r"));
        }

        [Theory]
        [InlineData("2**53+1", "9007199254740992.0")]
        [InlineData("2**53+3", "9007199254740996.0")]
        [InlineData("2**64+2**11", "1.8446744073709552e+19")]
        [InlineData("2**64+2**11+1", "1.8446744073709556e+19")]
        [InlineData("-(2**63+2**10)", "-9.223372036854776e+18")]
        [InlineData("10**23", "1e+23")]
        [InlineData("2**1023*3//2+1", "1.348269851146737e+308")]
        public void FloatOfIntRoundsToEven(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run("r = float(" + expr + ")\n").Repr("r"));
        }

        // Doubles whose repr was wrong (the old answer in the comment): a 16- or 15-digit rendering rounded
        // from the 17-digit one, a misread candidate accepted, an exact 17-digit tie rounded away from zero.
        [Theory]
        [InlineData("42EE7EEA0C7CFDD4", "268243531196398.62")]        // 268243531196398.63
        [InlineData("4313C4567D2788AD", "1390975075869227.2")]        // 1390975075869227.3
        [InlineData("42D204B42B240968", "79245647581221.62")]         // 79245647581221.63
        [InlineData("11929D3C0751C98A", "5.0288368436448605e-224")]   // 5.02883684364486e-224, another double
        [InlineData("28ACDCE893168DCD", "9.37631e-113")]              // 9.376309999999999e-113
        [InlineData("000000007795E590", "9.9125e-315")]               // 9.9125000005e-315
        [InlineData("0A23BD1DE8DC18E1", "8.023671229555707e-260")]    // 8.023671229555708e-260
        [InlineData("000000004EB2075B", "6.5231e-315")]               // 6.52310000223e-315
        [InlineData("42915FB69C22BF6C", "4775695812783.855")]         // 4775695812783.8555
        [InlineData("42634AF9322695F0", "662897791284.6855")]         // 662897791284.6856
        [InlineData("3E77B71AAE860E04", "8.834619403234075e-08")]     // 8.834619403234076e-08
        [InlineData("0000750F8AFE9A2C", "6.3591e-310")]               // 6.35910000000001e-310
        public void ReprIsCPythons(string bits, string expected)
        {
            double d = BitConverter.Int64BitsToDouble(long.Parse(bits, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            Assert.Equal(expected, FloatRepr.Repr(d));
        }

        [Theory]
        [InlineData("r = repr(1028400750439111.25)", "'1028400750439111.2'")]
        [InlineData("r = repr(31237655311000.0625)", "'31237655311000.062'")]
        [InlineData("r = repr(float.fromhex('0x0.014c5898977c4p-1022'))", "'1.1283791670955e-310'")]
        [InlineData("import json\nr = json.loads('[1.1283791670955e-310, 2.2250738585072011e-308]')", "[1.1283791670955e-310, 2.225073858507201e-308]")]
        [InlineData("from decimal import Decimal\nr = float(Decimal('0.30000000000000004'))", "0.30000000000000004")]
        [InlineData("r = (float('1e400'), float('-1e-400'), float(' 1_000.5 '))", "(inf, -0.0, 1000.5)")]
        public void TheConversionsAnswerAsCPython(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src + "\n").Repr("r"));
        }

        // Random texts (1 to 30 digits, the whole exponent range, subnormals, midpoints) against an exact
        // rational rounding written here, and every repr reading back as its double.
        [Fact]
        public void RandomTextsMatchExactRounding()
        {
            var rnd = new Random(1030);
            for (int n = 0; n < 4000; n++)
            {
                int len = rnd.Next(1, 31);
                var sb = new StringBuilder();
                sb.Append((char)('1' + rnd.Next(9)));
                for (int i = 1; i < len; i++)
                    sb.Append((char)('0' + rnd.Next(10)));
                int e = n % 3 == 0 ? rnd.Next(-345, -300) : rnd.Next(-330, 312);
                string text = sb + "e" + e.ToString(CultureInfo.InvariantCulture);
                double got;
                Assert.True(NearestDouble.TryParse(text, out got));
                Assert.True(BitConverter.DoubleToInt64Bits(got) == ExactBits(BigInteger.Parse(sb.ToString(), CultureInfo.InvariantCulture), e), text);
            }
        }

        [Fact]
        public void ReprReadsBack()
        {
            var rnd = new Random(31);
            var b = new byte[8];
            for (int n = 0; n < 20000; n++)
            {
                rnd.NextBytes(b);
                long bits = BitConverter.ToInt64(b, 0);
                if (n % 4 == 0)
                    bits &= 0x000FFFFFFFFFFFFFL;   // subnormals
                double d = BitConverter.Int64BitsToDouble(bits);
                if (double.IsNaN(d) || double.IsInfinity(d))
                    continue;
                double back;
                Assert.True(NearestDouble.TryParse(FloatRepr.Repr(d), out back));
                Assert.True(BitConverter.DoubleToInt64Bits(back) == bits, FloatRepr.Repr(d));
            }
        }

        // digits * 10^e to the nearest double, ties to even, through one exact quotient.
        private static long ExactBits(BigInteger digits, int e)
        {
            BigInteger num = digits, den = BigInteger.One;
            if (e >= 0)
                num *= BigInteger.Pow(10, e);
            else
                den = BigInteger.Pow(10, -e);
            int k = (int)Math.Floor(BigInteger.Log(num, 2) - BigInteger.Log(den, 2)) - 52;   // 2^52 <= num/den/2^k, roughly
            k = Math.Max(k, -1074);
            BigInteger m, rem, div;
            while (true)
            {
                div = k >= 0 ? den << k : den;
                m = BigInteger.DivRem(k >= 0 ? num : num << -k, div, out rem);
                if (m >= BigInteger.One << 53)
                    k++;
                else if (m < BigInteger.One << 52 && k > -1074)
                    k--;
                else
                    break;
            }
            int c = (rem * 2).CompareTo(div);
            if (c > 0 || (c == 0 && !m.IsEven))
                m++;
            if (m == BigInteger.One << 53)
            {
                m >>= 1;
                k++;
            }
            if (k > 971)
                return BitConverter.DoubleToInt64Bits(double.PositiveInfinity);
            long mant = (long)m;
            if (mant < 1L << 52)
                return mant;
            return ((long)(k + 1075) << 52) | (mant & ((1L << 52) - 1));
        }
    }
}
