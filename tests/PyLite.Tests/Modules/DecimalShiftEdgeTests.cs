using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // shift/scaleb take their amount from a Decimal or an int. A coefficient that fits the 28-digit
    // backend but not an int (10**20) used to escape as a C# OverflowException at the cast; the amount is
    // now clamped like Coerce.ToInt32 and the methods' own range checks answer. Found by the R1-R9 review.
    [Trait("Category", "Compatibility")]
    public class DecimalShiftEdgeTests
    {
        [Theory]
        [InlineData("Decimal(1).shift(Decimal(10**20))", "InvalidOperation")]
        [InlineData("Decimal(1).shift(Decimal(-10**20))", "InvalidOperation")]
        [InlineData("Decimal(1).shift(10**20)", "InvalidOperation")]
        [InlineData("Decimal(1).scaleb(Decimal(10**20))", "Overflow")]
        [InlineData("Decimal(1).scaleb(Decimal(2**31))", "Overflow")]
        [InlineData("Decimal(1).scaleb(2**31)", "Overflow")]
        [InlineData("Decimal(1).scaleb(Decimal(-10**20))", "Underflow")]
        [InlineData("Decimal(1).shift(Decimal(2))", "Decimal('100')")]
        [InlineData("Decimal(1).scaleb(Decimal(-2))", "Decimal('0.01')")]
        public void AnAmountPastIntRangeIsTheMethodsOwnError(string expr, string expected)
        {
            string src = "from decimal import Decimal\ntry:\n    r = " + expr + "\nexcept Exception as e:\n    r = type(e).__name__\n";
            string got = Outbridge.PyLite.Tests.Evaluator.Ev.Run(src).Repr("r");
            Assert.Equal(expected.StartsWith("Decimal") ? expected : "'" + expected + "'", got);
        }
    }
}
