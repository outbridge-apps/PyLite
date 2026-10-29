using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    // seq * n with an empty receiver and a count past int range: the product passes the collection cap
    // (it is 0), so the count must not be cast before the empty answer. Found by the R1-R9 review; the
    // cast escaped as a C# OverflowException for list, tuple and str (bytes and deque already checked).
    [Trait("Category", "Compatibility")]
    public class RepeatEdgeTests
    {
        [Theory]
        [InlineData("[] * 10**30", "[]")]
        [InlineData("() * 2**40", "()")]
        [InlineData("'' * 10**30", "''")]
        [InlineData("b'' * 10**30", "b''")]
        [InlineData("[1, 2] * -10**30", "[]")]
        [InlineData("(1,) * 1", "(1,)")]
        [InlineData("'ab' * 2", "'abab'")]
        public void AnEmptySequenceTimesAHugeCountIsEmpty(string expr, string expected)
        {
            Assert.Equal(expected, Outbridge.PyLite.Tests.Evaluator.Ev.Run("r = " + expr + "\n").Repr("r"));
        }

        [Fact]
        public void AnEmptyListInPlaceTimesAHugeCountStaysEmpty()
        {
            Assert.Equal("[]", Outbridge.PyLite.Tests.Evaluator.Ev.Run("l = []\nl *= 10**30\nr = l\n").Repr("r"));
        }

        [Fact]
        public void ANonEmptySequenceTimesAHugeCountIsTheCollectionCap()
        {
            Assert.Throws<Outbridge.PyLite.Runtime.Errors.EngineAbort>(() => Outbridge.PyLite.Tests.Evaluator.Ev.Run("r = [0] * 2**31\n"));
        }
    }
}
