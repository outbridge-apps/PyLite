using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/09 T-MDT-05: datetime.date.
    [Trait("Category", "Compatibility")]
    public class DateTests
    {
        private const string Prelude = "from datetime import date, timedelta\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(Prelude + body + "\n"));
        }

        [Theory]
        [InlineData("date(2002, 3, 11).toordinal()", "730920")]
        [InlineData("repr(date.fromordinal(730920))", "'datetime.date(2002, 3, 11)'")]
        [InlineData("date(2002,3,11).weekday()", "0")]
        [InlineData("date(2002,3,11).isoweekday()", "1")]
        [InlineData("date(2002,3,11).isocalendar()", "(2002, 11, 1)")]
        [InlineData("str(date(2, 3, 4))", "'0002-03-04'")]
        [InlineData("repr(date(2002, 3, 11))", "'datetime.date(2002, 3, 11)'")]
        [InlineData("date(2002,6,6).ctime()", "'Thu Jun  6 00:00:00 2002'")]
        [InlineData("repr(date(2002,3,11) + timedelta(days=1, seconds=86399))", "'datetime.date(2002, 3, 12)'")]
        [InlineData("repr(timedelta(days=1) + date(2002,3,11))", "'datetime.date(2002, 3, 12)'")]
        [InlineData("repr(date(2010,1,1) - date(2009,12,31))", "'datetime.timedelta(days=1)'")]
        [InlineData("date(2011,1,1).timetuple() == (2011,1,1,0,0,0,5,1,-1)", "True")]
        [InlineData("date(2011,1,1) == 5", "False")]
        [InlineData("date(2011,1,1) < date(2011,1,2)", "True")]
        [InlineData("date(True, True, True) == date(1,1,1)", "True")]
        [InlineData("repr(date.min)", "'datetime.date(1, 1, 1)'")]
        [InlineData("repr(date.max)", "'datetime.date(9999, 12, 31)'")]
        [InlineData("repr(date.resolution)", "'datetime.timedelta(days=1)'")]
        [InlineData("repr(date.fromtimestamp(0))", "'datetime.date(1970, 1, 1)'")]
        [InlineData("format(date(2002,3,11), '')", "'2002-03-11'")]
        public void DateBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("x = date(2011,2,29)", "ValueError")]           // day out of range
        [InlineData("x = date(0,1,1)", "ValueError")]               // year out of range
        [InlineData("x = date(10000,1,1)", "ValueError")]
        [InlineData("x = date(2011,0,1)", "ValueError")]            // month out of range
        [InlineData("x = date(2011,13,1)", "ValueError")]
        [InlineData("x = date.fromordinal(0)", "ValueError")]       // ordinal >= 1
        [InlineData("x = date(2011,1,1).replace(month=2, day=29)", "ValueError")]
        [InlineData("x = date.max + timedelta(days=1)", "OverflowError")]
        [InlineData("x = date(2011.0, 1, 1)", "TypeError")]         // float field
        public void DateErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void ValidationOrderYearFirst()
        {
            string src = Prelude + "msg = ''\ntry:\n    date(0, 0, 0)\nexcept ValueError as e:\n    msg = str(e)\n";
            Assert.Equal("'year is out of range'", Ev.Run(src).Repr("msg"));
        }

        [Fact]
        public void OverflowMessage()
        {
            string src = Prelude + "msg = ''\ntry:\n    date.max + timedelta(days=1)\nexcept OverflowError as e:\n    msg = str(e)\n";
            Assert.Equal("'date value out of range'", Ev.Run(src).Repr("msg"));
        }
    }
}
