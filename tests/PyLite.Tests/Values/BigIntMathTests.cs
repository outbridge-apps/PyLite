using System;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    // The one half-even division and floor division the engine keeps (review 3, §11: four copies disagreed on
    // negative operands no caller passed yet, e.g. -7 / 2 gave -3 in one of them).
    [Trait("Category", "Regression")]
    public class BigIntMathTests
    {
        [Fact]
        public void DivNearHalfEvenRoundsEveryTieToEven()
        {
            for (int a = -60; a <= 60; a++)
            {
                for (int b = -8; b <= 8; b++)
                {
                    if (b == 0)
                        continue;
                    double want = Math.Round(a / (double)b, MidpointRounding.ToEven);
                    Assert.True(want == (double)BigIntMath.DivNearHalfEven(a, b), a + " / " + b);
                    Assert.True(Math.Floor(a / (double)b) == (double)BigIntMath.FloorDiv(a, b), a + " // " + b);
                }
            }
        }

        [Fact]
        public void TheDecimalScaleRoundsAsTheDivision()
        {
            Assert.Equal(new BigInteger(-2), DecimalBits.RoundUnscaledToScale(-15, 1, 0));   // -1.5
            Assert.Equal(new BigInteger(-2), DecimalBits.RoundUnscaledToScale(-25, 1, 0));   // -2.5
            Assert.Equal(new BigInteger(2), DecimalBits.RoundUnscaledToScale(25, 1, 0));
            Assert.Equal(new BigInteger(-13), DecimalBits.RoundUnscaledToScale(-1251, 2, 0));
        }
    }
}
