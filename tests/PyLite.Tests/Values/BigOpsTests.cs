using System.Diagnostics;
using System.Numerics;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    // BigOps: a native BigInteger call past LongWork runs on a helper thread while the run thread polls the
    // deadline. The answers are the direct call's; an abort ends the run on time instead of after the call.
    [Collection("TimeSensitive")]   // asserts wall-clock bounds
    [Trait("Category", "ResourceLimits")]
    [Trait("Category", "MachineSensitive")]
    public class BigOpsTests
    {
        // ~136k and ~138k bits (2130 and 2150 words): every operation below is just past LongWork (2^22).
        private static readonly BigInteger A = BigInteger.Pow(3, 86000) + 12345;
        private static readonly BigInteger B = BigInteger.Pow(7, 49000) - 1;

        // ~951k and ~954k bits: a gcd or a decimal conversion takes seconds natively.
        private static readonly BigInteger HugeA = BigInteger.Pow(3, 600000) + 1;
        private static readonly BigInteger HugeB = BigInteger.Pow(7, 340000) + 3;

        [Fact]
        public void LongPathsMatchDirectCalls()
        {
            var ctx = Rt.Ctx(ResourceLimits.Default.With(deadlineMs: 120000));
            BigInteger ab = A * B + 777;
            Assert.Equal(A * B, BigOps.Multiply(ctx, A, B));
            BigInteger r0, r1;
            Assert.Equal(BigInteger.DivRem(ab, B, out r0), BigOps.DivRem(ctx, ab, B, out r1));
            Assert.Equal(r0, r1);
            Assert.Equal(BigInteger.GreatestCommonDivisor(A, B), BigOps.Gcd(ctx, A, B));
            Assert.Equal(BigInteger.Pow(7, 49000), BigOps.Pow(ctx, 7, 49000, 49000 * 2.81));
            Assert.Equal(A.ToString(System.Globalization.CultureInfo.InvariantCulture), BigOps.ToDecimalString(ctx, A));
            Assert.Equal(BigInteger.ModPow(A, 65537, B), BigOps.ModPow(ctx, A, 65537, B, 17 * 2150L * 2150));
        }

        [Fact]
        public void LongGcdEndsAtTheDeadline()
        {
            AssertEndsNearDeadline(ctx => BigOps.Gcd(ctx, HugeA, HugeB));
        }

        [Fact]
        public void LongDecimalStringEndsAtTheDeadline()
        {
            AssertEndsNearDeadline(ctx => BigOps.ToDecimalString(ctx, HugeA));
        }

        [Fact]
        public void LongIntParseEndsAtTheDeadline()
        {
            var sw = Stopwatch.StartNew();
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run("x = int('z' * 190000, 36)\n", ResourceLimits.Default.With(deadlineMs: 500)));
            Assert.Equal("DeadlineMs", ex.LimitName);
            Assert.True(sw.ElapsedMilliseconds < 1500, sw.ElapsedMilliseconds + " ms");   // the parse alone takes seconds
        }

        // A huge exponent and modulus, or the inverse of a huge modulus, end on the budget, not past the deadline.
        [Theory]
        [InlineData("x = pow(3, (1 << 100000) - 1, (1 << 100000) + 1)\n")]
        [InlineData("a = 7 ** 100000\nx = pow(a, -1, (1 << 300000) + 1)\n")]
        public void HugeModPowEndsOnBudget(string src)
        {
            var sw = Stopwatch.StartNew();
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src, ResourceLimits.Default.With(deadlineMs: 1000)));
            Assert.Contains(ex.LimitName, new[] { "DeadlineMs", "MaxSteps" });
            Assert.True(sw.ElapsedMilliseconds < 4000, sw.ElapsedMilliseconds + " ms");
        }

        private static void AssertEndsNearDeadline(System.Action<Outbridge.PyLite.Runtime.EvalContext> op)
        {
            Assert.False(HugeA.IsZero || HugeB.IsZero);   // build the operands before the deadline clock starts
            var ctx = Rt.Ctx(ResourceLimits.Default.With(deadlineMs: 200));
            var sw = Stopwatch.StartNew();
            var ex = Assert.Throws<EngineAbort>(() => op(ctx));
            Assert.Equal("DeadlineMs", ex.LimitName);
            Assert.True(sw.ElapsedMilliseconds < 1000, sw.ElapsedMilliseconds + " ms");
        }
    }
}
