using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    // BigKernel against net472's BigInteger, operation by operation: random operands across every size
    // boundary (schoolbook/Karatsuba, slices, decimal leaves), the limb patterns that reach the rare branches
    // (Knuth D's add-back, normalization by 0 bits, Lehmer's fallback to a full Euclid step), and signs.
    [Trait("Category", "Compatibility")]
    public class BigKernelTests
    {
        private static readonly int[] Sizes = { 1, 2, 3, 4, 31, 47, 48, 49, 63, 64, 65, 66, 96, 97, 100, 129, 200, 257, 513, 1000, 1500 };
        private static readonly uint[] Special = { 0u, 1u, 2u, 0x7FFFFFFFu, 0x80000000u, 0x80000001u, 0xFFFFFFFEu, 0xFFFFFFFFu };

        private static BigInteger FromLimbs(uint[] limbs, bool negative)
        {
            var b = new byte[4 * limbs.Length + 1];
            Buffer.BlockCopy(limbs, 0, b, 0, 4 * limbs.Length);
            var x = new BigInteger(b);
            return negative ? -x : x;
        }

        private static BigInteger Random(Random rng, int limbs, bool allowNegative = true)
        {
            var l = new uint[limbs];
            int shape = rng.Next(6);
            for (int i = 0; i < limbs; i++)
            {
                switch (shape)
                {
                    case 0:
                        l[i] = Special[rng.Next(Special.Length)];
                        break;
                    case 1:
                        l[i] = rng.Next(4) == 0 ? (uint)rng.Next() : 0u;   // sparse
                        break;
                    case 2:
                        l[i] = 0xFFFFFFFFu;                              // all ones
                        break;
                    default:
                        l[i] = (uint)rng.Next() ^ ((uint)rng.Next() << 16);
                        break;
                }
            }
            l[limbs - 1] |= 1u << rng.Next(32);   // a non-zero top limb at a random height
            return FromLimbs(l, allowNegative && rng.Next(2) == 0);
        }

        private static EvalContext Ctx()
        {
            return Rt.Ctx(ResourceLimits.Default.With(deadlineMs: 600000));
        }

        [Fact]
        public void MultiplyAndSquareMatchBigInteger()
        {
            var rng = new Random(20261030);
            var ctx = Ctx();
            foreach (int an in Sizes)
            {
                foreach (int bn in Sizes)
                {
                    for (int rep = 0; rep < 2; rep++)
                    {
                        BigInteger a = Random(rng, an), b = Random(rng, bn);
                        Assert.Equal(a * b, BigKernel.Multiply(ctx, a, b));
                    }
                }
                for (int rep = 0; rep < 4; rep++)
                {
                    BigInteger a = Random(rng, an);
                    Assert.Equal(a * a, BigKernel.Square(ctx, a));
                }
            }
            foreach (int n in new[] { 3000, 4097, 6143 })
            {
                BigInteger a = Random(rng, n), b = Random(rng, n + rng.Next(3) - 1);
                Assert.Equal(a * b, BigKernel.Multiply(ctx, a, b));
                Assert.Equal(a * a, BigKernel.Square(ctx, a));
            }
            Assert.Equal(BigInteger.Zero, BigKernel.Multiply(ctx, BigInteger.Zero, Random(rng, 100)));
            Assert.Equal(BigInteger.Zero, BigKernel.Square(ctx, BigInteger.Zero));
        }

        [Fact]
        public void DivRemMatchesBigInteger()
        {
            var rng = new Random(4113);
            var ctx = Ctx();
            foreach (int vn in Sizes)
            {
                foreach (int extra in new[] { 0, 1, 2, 5, 64, 300 })
                {
                    for (int rep = 0; rep < 3; rep++)
                    {
                        BigInteger u = Random(rng, vn + extra), v = Random(rng, vn);
                        AssertDivRem(ctx, u, v);
                    }
                }
            }
            AssertDivRem(ctx, Random(rng, 50), Random(rng, 60));   // |u| < |v|
            AssertDivRem(ctx, BigInteger.Zero, Random(rng, 3));
            Assert.Throws<DivideByZeroException>(() => { BigInteger r; BigKernel.DivRem(ctx, BigInteger.One, BigInteger.Zero, out r); });
        }

        // Every pair of 3- and 2-limb, and 4- and 3-limb, operands built from the special limbs: the patterns
        // that make qhat one too large (the add-back step) and normalize by every shift.
        [Fact]
        public void DivRemCoversTheRareBranches()
        {
            var ctx = Ctx();
            // qhat = 0xFFFFFFFF passes the two-limb test, and u - qhat * v goes negative: v is added back
            AssertDivRem(ctx, FromLimbs(new uint[] { 0, 0, 0x80000000u, 0x7FFFFFFFu }, false), FromLimbs(new uint[] { 1, 0, 0x80000000u }, false));
            var vs = new List<BigInteger>();
            foreach (uint a in Special)
            {
                foreach (uint b in Special)
                {
                    if (b != 0)
                        vs.Add(FromLimbs(new[] { a, b }, false));
                    foreach (uint c in new[] { 1u, 0x80000000u, 0xFFFFFFFFu })
                        vs.Add(FromLimbs(new[] { a, b, c }, false));
                }
            }
            var rng = new Random(7);
            foreach (BigInteger v in vs)
            {
                foreach (uint top in new[] { 0u, 1u, 0x7FFFFFFFu, 0x80000000u, 0xFFFFFFFFu })
                {
                    var limbs = new uint[v.ToByteArray().Length / 4 + 2];
                    for (int i = 0; i < limbs.Length - 1; i++)
                        limbs[i] = Special[rng.Next(Special.Length)];
                    limbs[limbs.Length - 1] = top == 0 ? 1u : top;
                    AssertDivRem(ctx, FromLimbs(limbs, false), v);
                    AssertDivRem(ctx, FromLimbs(limbs, true), v);
                }
            }
        }

        private static void AssertDivRem(EvalContext ctx, BigInteger u, BigInteger v)
        {
            BigInteger r0, r1;
            BigInteger q0 = BigInteger.DivRem(u, v, out r0);
            BigInteger q1 = BigKernel.DivRem(ctx, u, v, out r1);
            Assert.True(q0 == q1 && r0 == r1, "DivRem(" + u + ", " + v + ")");
        }

        [Fact]
        public void GcdMatchesBigInteger()
        {
            var rng = new Random(99);
            var ctx = Ctx();
            foreach (int an in Sizes)
            {
                foreach (int bn in new[] { 1, 2, 3, 4, 50, an, an + 1 })
                {
                    BigInteger a = Random(rng, an), b = Random(rng, bn), g = Random(rng, 1 + rng.Next(40));
                    Assert.Equal(BigInteger.GreatestCommonDivisor(a, b), BigKernel.Gcd(ctx, a, b));
                    Assert.Equal(BigInteger.GreatestCommonDivisor(a * g, b * g), BigKernel.Gcd(ctx, a * g, b * g));
                }
            }
            // consecutive Fibonacci numbers: Euclid's worst case, every quotient 1
            BigInteger f0 = 0, f1 = 1;
            for (int i = 0; i < 9000; i++)
            {
                BigInteger f2 = f0 + f1;
                f0 = f1;
                f1 = f2;
            }
            Assert.Equal(BigInteger.One, BigKernel.Gcd(ctx, f1, f0));
            Assert.Equal(f0, BigKernel.Gcd(ctx, f0 * 3, f0 * 5));
            BigInteger x = Random(rng, 700);
            Assert.Equal(BigInteger.Abs(x), BigKernel.Gcd(ctx, x, x));
            Assert.Equal(BigInteger.Abs(x), BigKernel.Gcd(ctx, x, BigInteger.Zero));
            Assert.Equal(BigInteger.Abs(x), BigKernel.Gcd(ctx, x * 7, x));
            Assert.Equal(BigInteger.Zero, BigKernel.Gcd(ctx, BigInteger.Zero, BigInteger.Zero));
        }

        [Fact]
        public void DecimalStringMatchesBigInteger()
        {
            var rng = new Random(1234);
            var ctx = Ctx();
            foreach (int n in Sizes)
            {
                for (int rep = 0; rep < 3; rep++)
                {
                    BigInteger a = Random(rng, n);
                    Assert.Equal(a.ToString(CultureInfo.InvariantCulture), BigKernel.ToDecimalString(ctx, a));
                }
            }
            // powers of ten around every split width (9 * 2^k digits): the zero padding of the low halves
            foreach (int k in new[] { 9, 17, 18, 19, 36, 72, 143, 144, 145, 288, 576, 1152, 2304, 4608, 9216 })
            {
                BigInteger p = BigInteger.Pow(10, k);
                foreach (BigInteger v in new[] { p, p - 1, p + 1, -p, p * 7 + 3, p * p + 1, p * p - 1 })
                    Assert.Equal(v.ToString(CultureInfo.InvariantCulture), BigKernel.ToDecimalString(ctx, v));
            }
            Assert.Equal("0", BigKernel.ToDecimalString(ctx, BigInteger.Zero));
            BigInteger big = Random(rng, 5000, false);
            Assert.Equal(big.ToString(CultureInfo.InvariantCulture), BigKernel.ToDecimalString(ctx, big));
        }

        // BigOps' square-and-multiply paths (forced long here) against BigInteger.Pow and ModPow.
        [Fact]
        public void PowAndModPowLoopsMatchBigInteger()
        {
            var rng = new Random(55);
            var ctx = Ctx();
            foreach (BigInteger a in new[] { BigInteger.Zero, BigInteger.One, BigInteger.MinusOne, new BigInteger(3), new BigInteger(-7), Random(rng, 5), Random(rng, 70) })
            {
                foreach (int n in new[] { 0, 1, 2, 3, 17, 64, 100 })
                    Assert.Equal(BigInteger.Pow(a, n), BigOps.Pow(ctx, a, n, 1e12));
            }
            foreach (int mn in new[] { 1, 2, 3, 40, 65, 130 })
            {
                for (int rep = 0; rep < 4; rep++)
                {
                    BigInteger b = Random(rng, 1 + rng.Next(mn + 2)), e = BigInteger.Abs(Random(rng, 1 + rng.Next(4))), m = Random(rng, mn);
                    Assert.Equal(BigInteger.ModPow(b, e, m), BigOps.ModPow(ctx, b, e, m, long.MaxValue));
                }
            }
            foreach (BigInteger m in new[] { BigInteger.One, BigInteger.MinusOne, new BigInteger(2), new BigInteger(-97) })
            {
                foreach (BigInteger b in new[] { BigInteger.Zero, new BigInteger(5), new BigInteger(-5) })
                {
                    foreach (int e in new[] { 0, 1, 2, 3 })
                        Assert.Equal(BigInteger.ModPow(b, e, m), BigOps.ModPow(ctx, b, e, m, long.MaxValue));
                }
            }
        }
    }
}
