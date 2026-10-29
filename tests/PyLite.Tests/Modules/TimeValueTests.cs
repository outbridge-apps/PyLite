using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/09 T-MDT-06: datetime.time (naive/aware time-of-day).
    [Trait("Category", "Compatibility")]
    public class TimeValueTests
    {
        private const string Prelude = "from datetime import time, timezone, timedelta\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(Prelude + body + "\n"));
        }

        [Theory]
        [InlineData("str(time(12, 30))", "'12:30:00'")]
        [InlineData("str(time(1, 2, 3, 4))", "'01:02:03.000004'")]
        [InlineData("str(time(12, 30, tzinfo=timezone(timedelta(hours=-4, minutes=-30))))", "'12:30:00-04:30'")]
        [InlineData("repr(time(1))", "'datetime.time(1, 0)'")]
        [InlineData("repr(time(1, 2, 3))", "'datetime.time(1, 2, 3)'")]
        [InlineData("repr(time(0, 0, tzinfo=timezone.utc))", "'datetime.time(0, 0, tzinfo=datetime.timezone.utc)'")]
        [InlineData("bool(time(0, 0))", "True")]
        [InlineData("bool(time(0, 0, 0, 1))", "True")]
        [InlineData("bool(time(1, 0, tzinfo=timezone(timedelta(hours=1))))", "True")]
        [InlineData("bool(time(0, 0, tzinfo=timezone(timedelta(hours=1))))", "True")]
        [InlineData("time(12) == time(12, tzinfo=timezone.utc)", "False")]
        [InlineData("time(5, tzinfo=timezone(timedelta(hours=-3))) == time(8, tzinfo=timezone.utc)", "True")]
        [InlineData("repr(time(23,59).replace(minute=0))", "'datetime.time(23, 0)'")]
        [InlineData("time(12, tzinfo=timezone.utc).replace(tzinfo=None).tzname()", "None")]
        [InlineData("time(12, tzinfo=timezone.utc).tzname()", "'UTC'")]
        [InlineData("time(12).tzinfo", "None")]
        [InlineData("repr(time(12, tzinfo=timezone.utc).utcoffset())", "'datetime.timedelta(0)'")]
        [InlineData("time(0,0).dst()", "None")]
        [InlineData("str(time.max)", "'23:59:59.999999'")]
        [InlineData("repr(time.resolution)", "'datetime.timedelta(microseconds=1)'")]
        public void TimeBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("x = time(25)", "ValueError")]                  // hour
        [InlineData("x = time(23, 59, 59, 1000000)", "ValueError")] // microsecond
        [InlineData("x = time(0, 60)", "ValueError")]               // minute
        [InlineData("x = time(0, 0, tzinfo=5)", "TypeError")]       // bad tzinfo
        public void TimeErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void NaiveAwareOrderingIsTypeError()
        {
            var ex = Raises("x = time(12) < time(12, tzinfo=timezone.utc)");
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void NaiveAwareOrderingMessage()
        {
            string src = Prelude + "msg = ''\ntry:\n    time(12) < time(12, tzinfo=timezone.utc)\nexcept TypeError as e:\n    msg = str(e)\n";
            Assert.Equal("\"can't compare offset-naive and offset-aware times\"", Ev.Run(src).Repr("msg"));
        }
    }
}
