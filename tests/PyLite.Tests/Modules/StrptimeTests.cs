using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/09 T-MDT-11: StrptimeParser (hand-written scanner, no Regex).
    [Trait("Category", "Compatibility")]
    public class StrptimeTests
    {
        private const string Prelude = "from datetime import datetime, timedelta, timezone\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(Prelude + body + "\n"));
        }

        [Theory]
        [InlineData("datetime.strptime('+04:0030', '%z')", "Inconsistent use of : in +04:0030")]
        [InlineData("datetime.strptime('+0430:15', '%z')", "Inconsistent use of : in +0430:15")]
        [InlineData("datetime.strptime('+2500', '%z')", "offset must be a timedelta strictly between -timedelta(hours=24) and timedelta(hours=24).")]
        [InlineData("datetime.strptime('z', '%z')", "time data 'z' does not match format '%z'")]
        [InlineData("datetime.strptime('+04', '%z')", "time data '+04' does not match format '%z'")]
        [InlineData("datetime.strptime('+04:60', '%z')", "time data '+04:60' does not match format '%z'")]
        public void OffsetErrors(string expr, string message)
        {
            var ex = Raises("r = " + expr);
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
            Assert.Equal(message, ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Theory]
        [InlineData("repr(datetime.strptime('2004-11-30 13:30:00', '%Y-%m-%d %H:%M:%S'))", "'datetime.datetime(2004, 11, 30, 13, 30)'")]
        [InlineData("repr(datetime.strptime('', ''))", "'datetime.datetime(1900, 1, 1, 0, 0)'")]
        [InlineData("repr(datetime.strptime('Tue Jun  1 13:30:00 2004', '%c'))", "'datetime.datetime(2004, 6, 1, 13, 30)'")]
        [InlineData("datetime.strptime('5', '%f').microsecond", "500000")]
        [InlineData("datetime.strptime('1 PM', '%I %p').hour", "13")]
        [InlineData("datetime.strptime('12 AM', '%I %p').hour", "0")]
        [InlineData("datetime.strptime('68', '%y').year", "2068")]
        [InlineData("datetime.strptime('69', '%y').year", "1969")]
        [InlineData("repr(datetime.strptime('2004 25 0', '%Y %U %w'))", "'datetime.datetime(2004, 6, 20, 0, 0)'")]
        [InlineData("repr(datetime.strptime('2004 25 1', '%Y %W %w'))", "'datetime.datetime(2004, 6, 21, 0, 0)'")]
        [InlineData("repr(datetime.strptime('2015 0 0', '%Y %U %w'))", "'datetime.datetime(2014, 12, 28, 0, 0)'")]
        [InlineData("datetime.strptime('+0430', '%z').utcoffset() == timedelta(hours=4, minutes=30)", "True")]
        [InlineData("datetime.strptime('+04:30', '%z').utcoffset() == timedelta(hours=4, minutes=30)", "True")]
        [InlineData("datetime.strptime('+04:30:15', '%z').utcoffset() == timedelta(hours=4, minutes=30, seconds=15)", "True")]
        [InlineData("datetime.strptime('+043015', '%z').utcoffset() == timedelta(hours=4, minutes=30, seconds=15)", "True")]
        [InlineData("datetime.strptime('-05:30:15.000250', '%z').utcoffset() == -timedelta(hours=5, minutes=30, seconds=15, microseconds=250)", "True")]
        [InlineData("datetime.strptime('+00:00:00.5', '%z').utcoffset() == timedelta(microseconds=500000)", "True")]
        [InlineData("repr(datetime.strptime('Z', '%z').tzinfo)", "'datetime.timezone.utc'")]
        [InlineData("repr(datetime.strptime('+00:00', '%z').tzinfo)", "'datetime.timezone.utc'")]
        [InlineData("datetime.strptime('2024-05-06T07:08:09+02:00', '%Y-%m-%dT%H:%M:%S%z').utcoffset() == timedelta(hours=2)", "True")]
        [InlineData("datetime.strptime('+0430 x', '%z x').utcoffset() == timedelta(hours=4, minutes=30)", "True")]
        [InlineData("timezone(timedelta(0)) is timezone.utc", "True")]
        [InlineData("timezone(timedelta(0), 'UTC') is timezone.utc", "False")]
        [InlineData("datetime.strptime('June', '%B').month", "6")]
        [InlineData("datetime.strptime('jUn', '%b').month", "6")]
        [InlineData("datetime.strptime('Thu Jan  1 00:00:00 1970', '%a %b %d %H:%M:%S %Y').year", "1970")]
        public void StrptimeBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("x = datetime.strptime('abc', '%Y')", "ValueError")]           // does not match
        [InlineData("x = datetime.strptime('999', '%Y')", "ValueError")]           // exactly 4 required
        [InlineData("x = datetime.strptime('2004-11-30 xx', '%Y-%m-%d ')", "ValueError")]  // unconverted
        [InlineData("x = datetime.strptime('32', '%d')", "ValueError")]            // unconverted remains: 2
        [InlineData("x = datetime.strptime('2004', '%Y %q')", "ValueError")]       // bad directive
        [InlineData("x = datetime.strptime('2004', '%Y %')", "ValueError")]        // stray %
        public void StrptimeErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Theory]
        [InlineData("datetime.strptime('abc', '%Y')", "\"time data 'abc' does not match format '%Y'\"")]
        [InlineData("datetime.strptime('2004-11-30 xx', '%Y-%m-%d ')", "'unconverted data remains: xx'")]
        [InlineData("datetime.strptime('32', '%d')", "'unconverted data remains: 2'")]
        [InlineData("datetime.strptime('2004', '%Y %q')", "\"'q' is a bad directive in format '%Y %q'\"")]
        [InlineData("datetime.strptime('2004', '%Y %')", "\"stray % in format '%Y %'\"")]
        public void StrptimeErrorMessage(string call, string expectedRepr)
        {
            string src = Prelude + "msg = ''\ntry:\n    " + call + "\nexcept ValueError as e:\n    msg = str(e)\n";
            Assert.Equal(expectedRepr, Ev.Run(src).Repr("msg"));
        }
    }
}
