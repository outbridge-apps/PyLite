using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/09 T-MDT-04: datetime.timezone (fixed-offset tzinfo).
    [Trait("Category", "Compatibility")]
    public class TimezoneTests
    {
        private const string Prelude = "from datetime import timezone, timedelta\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(Prelude + body + "\n"));
        }

        [Theory]
        [InlineData("timezone.utc.tzname(None)", "'UTC'")]
        [InlineData("str(timezone(timedelta(hours=5, minutes=30)))", "'UTC+05:30'")]
        [InlineData("str(timezone(timedelta(hours=-4, minutes=-30)))", "'UTC-04:30'")]
        [InlineData("timezone(timedelta(minutes=1439)).tzname(None)", "'UTC+23:59'")]
        [InlineData("repr(timezone.utc)", "'datetime.timezone.utc'")]
        [InlineData("repr(timezone(timedelta(0), 'EST'))", "\"datetime.timezone(datetime.timedelta(0), 'EST')\"")]
        [InlineData("timezone(timedelta(0)) == timezone.utc", "True")]
        [InlineData("timezone.utc.dst(None)", "None")]
        [InlineData("repr(timezone.utc.utcoffset(None))", "'datetime.timedelta(0)'")]
        [InlineData("timezone(timedelta(0), 'A') == timezone(timedelta(0), 'B')", "True")]
        [InlineData("timezone.min.tzname(None)", "'UTC-23:59'")]
        [InlineData("timezone.max.tzname(None)", "'UTC+23:59'")]
        public void TimezoneBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("timezone(timedelta(hours=24))")]        // out of range
        [InlineData("timezone(timedelta(hours=-24))")]       // out of range
        public void BadOffsetIsValueError(string expr)
        {
            Assert.Equal("ValueError", Raises("x = " + expr).Value.ExcType.Name);
        }

        [Fact]
        public void NonTimedeltaOffsetIsTypeError()
        {
            Assert.Equal("TypeError", Raises("timezone('x')").Value.ExcType.Name);
        }

        [Fact]
        public void RangeMessageHasTrailingPeriod()
        {
            string src = Prelude + "msg = ''\ntry:\n    timezone(timedelta(hours=24))\nexcept ValueError as e:\n    msg = str(e)\n";
            Assert.Equal("'offset must be a timedelta strictly between -timedelta(hours=24) and timedelta(hours=24).'",
                Ev.Run(src).Repr("msg"));
        }
    }
}
