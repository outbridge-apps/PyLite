using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modern
{
    // Boundary fixes from the broad module/method fuzz: a huge size/count/exponent/index once reached an
    // unchecked (int)BigInteger cast or an uncharged BCL allocation and leaked as an EngineFault. Each is
    // now a clean OverflowError or an (uncatchable) budget abort, while normal inputs are unchanged.
    [Trait("Category", "Regression")]
    public class ModuleBoundaryTests
    {
        private static string R(string src)
        {
            return Ev.Run(src).Repr("r");
        }

        private static string ErrType(string src)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(src)).Value.ExcType.Name;
        }

        // round(int, n<0) — normal, and the huge-ndigits cases that overflowed -int.MinValue.
        [Theory]
        [InlineData("r = round(12345, -2)", "12300")]
        [InlineData("r = round(12345, -3)", "12000")]
        [InlineData("r = round(12345, -6)", "0")]
        [InlineData("r = round(60, -2)", "100")]
        [InlineData("r = round(50, -2)", "0")]
        [InlineData("r = round(12345, -(10 ** 100))", "0")]
        [InlineData("r = round(10 ** 40, -(10 ** 100))", "0")]
        public void RoundNegDigits(string src, string expected)
        {
            Assert.Equal(expected, R(src + "\n"));
        }

        // itertools combinatorics — normal results, and OverflowError (not a fault) for an oversized r/repeat.
        [Theory]
        [InlineData("import itertools\nr = list(itertools.combinations([1,2,3], 2))", "[(1, 2), (1, 3), (2, 3)]")]
        [InlineData("import itertools\nr = list(itertools.combinations([1,2,3], 5))", "[]")]
        [InlineData("import itertools\nr = list(itertools.permutations([1,2], 5))", "[]")]
        [InlineData("import itertools\nr = list(itertools.combinations_with_replacement([1,2], 2))", "[(1, 1), (1, 2), (2, 2)]")]
        [InlineData("import itertools\nr = list(itertools.product([1,2], repeat=2))", "[(1, 1), (1, 2), (2, 1), (2, 2)]")]
        public void ItertoolsNormal(string src, string expected)
        {
            Assert.Equal(expected, R(src + "\n"));
        }

        [Theory]
        [InlineData("import itertools\nr = list(itertools.combinations([1,2,3], 10 ** 100))")]
        [InlineData("import itertools\nr = list(itertools.permutations([1,2,3], 10 ** 100))")]
        [InlineData("import itertools\nr = list(itertools.combinations_with_replacement([1,2], 10 ** 100))")]
        [InlineData("import itertools\nr = list(itertools.product([1,2], repeat=10 ** 100))")]
        public void ItertoolsOversizedRIsOverflow(string src)
        {
            Assert.Equal("OverflowError", ErrType(src + "\n"));
        }

        // bytes(n) / expandtabs / deque maxlen / random choices k — huge -> OverflowError or budget abort.
        [Theory]
        [InlineData("r = bytes(3)", "b'\\x00\\x00\\x00'")]
        [InlineData("r = 'a\\tb'.expandtabs(4)", "'a   b'")]
        public void SizeArgsNormal(string src, string expected)
        {
            Assert.Equal(expected, R(src + "\n"));
        }

        [Theory]
        [InlineData("import collections\nr = collections.deque([1,2,3], 10 ** 100)", "OverflowError")]
        [InlineData("import random\nr = random.choices([1,2,3], k=10 ** 100)", "OverflowError")]
        public void SizeArgsHugeIsOverflow(string src, string excType)
        {
            Assert.Equal(excType, ErrType(src + "\n"));
        }

        // math.factorial — normal small values still exact after the est-bytes overflow guard.
        [Theory]
        [InlineData("import math\nr = math.factorial(0)", "1")]
        [InlineData("import math\nr = math.factorial(10)", "3628800")]
        [InlineData("import math\nr = math.comb(5, 2)", "10")]
        [InlineData("import math\nr = math.perm(5, 2)", "20")]
        public void MathNormal(string src, string expected)
        {
            Assert.Equal(expected, R(src + "\n"));
        }

        // Huge slice stride: (int)step once overflowed; a stride past the sequence takes just the first
        // element (direction by sign), matching CPython, for get / assign across list/str/tuple.
        [Theory]
        [InlineData("r = [1,2,3][::10 ** 100]", "[1]")]
        [InlineData("r = [1,2,3][::-(10 ** 100)]", "[3]")]
        [InlineData("r = 'abc'[::10 ** 100]", "'a'")]
        [InlineData("r = (1,2,3)[::10 ** 100]", "(1,)")]
        [InlineData("r = [1,2,3,4,5][::2]", "[1, 3, 5]")]        // normal stride unchanged
        [InlineData("r = 'hello'[::-1]", "'olleh'")]
        public void HugeSliceStride(string src, string expected)
        {
            Assert.Equal(expected, R(src + "\n"));
        }

        [Fact]
        public void HugeSliceStrideAssign()
        {
            Assert.Equal("[99, 2, 3, 4]", R("a = [1,2,3,4]\na[::10 ** 100] = [99]\nr = a\n"));
        }

        // The full never-faults / never-throws contract for these huge-arg module cases (incl. the
        // pathological regex quantifier that OOMs the .NET engine) is pinned exhaustively through the real
        // host boundary in HostBoundaryTests.RunNeverThrowsAndNeverFaults.
    }
}
