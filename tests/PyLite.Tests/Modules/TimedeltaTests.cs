using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/09 T-MDT-02/03: datetime.timedelta construction, normalization, arithmetic, str/repr.
    [Trait("Category", "Compatibility")]
    public class TimedeltaTests
    {
        private const string Prelude = "from datetime import timedelta\n";

        // (days, seconds, microseconds) of a timedelta expression.
        private static string Triple(string expr)
        {
            string src = Prelude + "_t = (" + expr + ")\nr = (_t.days, _t.seconds, _t.microseconds)\n";
            return Ev.Run(src).Repr("r");
        }

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(Prelude + body + "\n"));
        }

        [Theory]
        // ---- construction / normalization (T-MDT-02) ----
        [InlineData("timedelta()", "(0, 0, 0)")]
        [InlineData("timedelta(microseconds=-1)", "(-1, 86399, 999999)")]
        [InlineData("timedelta(seconds=-1)", "(-1, 86399, 0)")]
        [InlineData("timedelta(days=1, seconds=-86401)", "(-1, 86399, 0)")]
        [InlineData("timedelta(weeks=1)", "(7, 0, 0)")]
        [InlineData("timedelta(hours=25)", "(1, 3600, 0)")]
        [InlineData("timedelta(minutes=90.5)", "(0, 5430, 0)")]
        [InlineData("timedelta(days=0.5)", "(0, 43200, 0)")]
        [InlineData("timedelta(days=-0.25)", "(-1, 64800, 0)")]
        [InlineData("timedelta(seconds=0.5, microseconds=500000)", "(0, 1, 0)")]
        [InlineData("timedelta(microseconds=0.5)", "(0, 0, 0)")]     // half-even
        [InlineData("timedelta(microseconds=1.5)", "(0, 0, 2)")]     // half-even
        [InlineData("timedelta(milliseconds=1.5)", "(0, 0, 1500)")]
        // ---- arithmetic (T-MDT-03) ----
        [InlineData("timedelta(1) + timedelta(seconds=1)", "(1, 1, 0)")]
        [InlineData("timedelta.max - timedelta.max", "(0, 0, 0)")]
        [InlineData("-timedelta(0, 0, 1)", "(-1, 86399, 999999)")]
        [InlineData("abs(timedelta(microseconds=-1))", "(0, 0, 1)")]
        [InlineData("timedelta(1) * 3", "(3, 0, 0)")]
        [InlineData("3 * timedelta(1)", "(3, 0, 0)")]
        [InlineData("timedelta(microseconds=1) * 0.5", "(0, 0, 0)")]
        [InlineData("timedelta(microseconds=3) * 0.5", "(0, 0, 2)")]
        [InlineData("timedelta(2) / 3", "(0, 57600, 0)")]
        [InlineData("timedelta(hours=25) % timedelta(days=1)", "(0, 3600, 0)")]
        [InlineData("+timedelta(hours=1)", "(0, 3600, 0)")]
        public void TimedeltaTriple(string expr, string expected)
        {
            Assert.Equal(expected, Triple(expr));
        }

        [Theory]
        // ---- scalars / str / repr / comparisons ----
        [InlineData("timedelta(1) / timedelta(hours=6)", "4.0")]
        [InlineData("timedelta(1) // timedelta(hours=7)", "3")]
        [InlineData("timedelta(days=1, microseconds=1).total_seconds()", "86400.000001")]
        [InlineData("divmod(timedelta(minutes=-5), timedelta(minutes=2))", "(-3, datetime.timedelta(seconds=60))")]
        [InlineData("str(timedelta(days=-1, seconds=1))", "'-1 day, 0:00:01'")]
        [InlineData("str(timedelta(microseconds=-1))", "'-1 day, 23:59:59.999999'")]
        [InlineData("str(timedelta(days=2))", "'2 days, 0:00:00'")]
        [InlineData("repr(timedelta(1, 2, 3))", "'datetime.timedelta(days=1, seconds=2, microseconds=3)'")]
        [InlineData("repr(timedelta(1))", "'datetime.timedelta(days=1)'")]
        [InlineData("timedelta(1) == 1", "False")]
        [InlineData("timedelta(0) == timedelta(seconds=0)", "True")]
        [InlineData("bool(timedelta(0))", "False")]
        [InlineData("bool(timedelta(microseconds=1))", "True")]
        [InlineData("timedelta(hours=1) < timedelta(hours=2)", "True")]
        [InlineData("timedelta.min.days", "-999999999")]
        [InlineData("timedelta.resolution.microseconds", "1")]
        public void TimedeltaScalar(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void DaysOverflowIsOverflowError()
        {
            var ex = Raises("timedelta(days=10**9)");
            Assert.Equal("OverflowError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void DaysOverflowMessage()
        {
            string src = Prelude + "msg = ''\ntry:\n    timedelta(days=10**9)\nexcept OverflowError as e:\n    msg = str(e)\n";
            Assert.Equal("'days=1000000000; must have magnitude <= 999999999'", Ev.Run(src).Repr("msg"));
        }

        [Fact]
        public void StrComponentIsTypeError()
        {
            Assert.Equal("TypeError", Raises("timedelta(seconds='1')").Value.ExcType.Name);
        }

        [Fact]
        public void NanComponentIsValueError()
        {
            Assert.Equal("ValueError", Raises("timedelta(days=float('nan'))").Value.ExcType.Name);
        }

        [Fact]
        public void OrderingAgainstStrIsTypeError()
        {
            Assert.Equal("TypeError", Raises("timedelta(1) < 'x'").Value.ExcType.Name);
        }

        [Fact]
        public void TrueDivByZeroIsZeroDivision()
        {
            Assert.Equal("ZeroDivisionError", Raises("timedelta(1) / timedelta(0)").Value.ExcType.Name);
        }
    }
}
