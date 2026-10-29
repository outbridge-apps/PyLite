using System;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class NumericHashTests
    {
        [Fact]
        public void IntFloatBoolOneAgree()
        {
            var ctx = Rt.Ctx();
            Assert.Equal(1L, ctx.Values.Int(1).PyHash(ctx));
            Assert.Equal(1L, ctx.Values.Float(1.0).PyHash(ctx));
            Assert.Equal(1L, ctx.Values.Bool(true).PyHash(ctx));
        }

        [Fact]
        public void ZeroFamilyAgree()
        {
            var ctx = Rt.Ctx();
            Assert.Equal(0L, ctx.Values.Bool(false).PyHash(ctx));
            Assert.Equal(0L, ctx.Values.Int(0).PyHash(ctx));
            Assert.Equal(0L, ctx.Values.Float(0.0).PyHash(ctx));
            Assert.Equal(0L, ctx.Values.Float(-0.0).PyHash(ctx));
        }

        [Fact]
        public void MinusOneFoldsToMinusTwo()
        {
            var ctx = Rt.Ctx();
            Assert.Equal(-2L, ctx.Values.Int(-1).PyHash(ctx));
            Assert.Equal(-2L, ctx.Values.Float(-1.0).PyHash(ctx));
        }

        [Fact]
        public void MersenneWraps()
        {
            Assert.Equal(0L, NumericHash.HashBigInteger(BigInteger.Pow(2, 61) - 1));
            Assert.Equal(1L, NumericHash.HashBigInteger(BigInteger.Pow(2, 61)));
            Assert.Equal(2L, NumericHash.HashBigInteger(BigInteger.Pow(2, 62)));
        }

        [Fact]
        public void SpecialDoubles()
        {
            Assert.Equal(314159L, NumericHash.HashDouble(double.PositiveInfinity));
            Assert.Equal(-271828L, NumericHash.HashDouble(double.NegativeInfinity));
            Assert.Equal(0L, NumericHash.HashDouble(double.NaN));
        }

        [Fact] public void HalfHash() => Assert.Equal(1152921504606846976L, NumericHash.HashDouble(0.5));
        [Fact] public void OneAndHalfHash() => Assert.Equal(1152921504606846977L, NumericHash.HashDouble(1.5));

        [Fact]
        public void EmptyStringHashIsZeroRegardlessOfSeed()
        {
            Assert.Equal(0L, NumericHash.HashString("", 0u));
            Assert.Equal(0L, NumericHash.HashString("", 0xDEADBEEFu));
        }

        [Fact]
        public void StringHashSeedSensitiveAndStable()
        {
            long a = NumericHash.HashString("abc", 111u);
            long b = NumericHash.HashString("abc", 222u);
            Assert.NotEqual(a, b);
            Assert.Equal(a, NumericHash.HashString("abc", 111u));
        }

        [Fact]
        public void IntegralDoubleAgreesWithBigInteger()
        {
            var rnd = new Random(20260707);
            for (int i = 0; i < 10_000; i++)
            {
                long v = ((long)rnd.Next() << 21) ^ rnd.Next(); // magnitude < 2^52, exact in double
                v %= (1L << 52);
                double d = v;
                Assert.Equal(NumericHash.HashBigInteger(v), NumericHash.HashDouble(d));
            }
        }
    }
}
