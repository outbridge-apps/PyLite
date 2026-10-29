using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // calendar: the functions over CalendarMath, Monday-first as the module's default.
    [Trait("Category", "Compatibility")]
    public class CalendarTests
    {
        private const string Imp = "import calendar\nfrom calendar import isleap, leapdays, weekday, monthrange, monthcalendar, timegm, month_name, month_abbr, day_name, day_abbr\n";

        [Theory]
        [InlineData("(isleap(2024), isleap(1900), isleap(2000), isleap(2023))", "(True, False, True, False)")]
        [InlineData("(leapdays(2000, 2025), leapdays(1900, 1901), leapdays(2024, 2024))", "(7, 0, 0)")]
        [InlineData("(weekday(2024, 5, 6), weekday(2024, 12, 29), weekday(1, 1, 1))", "(0, 6, 0)")]
        [InlineData("(monthrange(2024, 2), monthrange(2023, 2), monthrange(2024, 12))", "((3, 29), (2, 28), (6, 31))")]
        [InlineData("monthcalendar(2024, 2)", "[[0, 0, 0, 1, 2, 3, 4], [5, 6, 7, 8, 9, 10, 11], [12, 13, 14, 15, 16, 17, 18], [19, 20, 21, 22, 23, 24, 25], [26, 27, 28, 29, 0, 0, 0]]")]
        [InlineData("len(monthcalendar(2024, 9))", "6")]
        [InlineData("(month_name[2], month_name[0], month_abbr[12], day_name[0], day_abbr[6], len(month_name), len(day_name))", "('February', '', 'Dec', 'Monday', 'Sun', 13, 7)")]
        [InlineData("(calendar.MONDAY, calendar.SUNDAY, calendar.JANUARY, calendar.DECEMBER, calendar.firstweekday())", "(0, 6, 1, 12, 0)")]
        [InlineData("(timegm((1970, 1, 2, 0, 0, 0, 0, 0, 0)), timegm((2024, 1, 1, 0, 0, 0)), timegm((2024, 2, 30, 0, 0, 0)) == timegm((2024, 3, 1, 0, 0, 0)))", "(86400, 1704067200, True)")]
        [InlineData("import time\nr = timegm(time.gmtime(1700000000))", "1700000000")]
        [InlineData("calendar.weekheader(2)", "'Mo Tu We Th Fr Sa Su'")]
        [InlineData("calendar.weekheader(3)", "'Mon Tue Wed Thu Fri Sat Sun'")]
        [InlineData("calendar.month(2024, 2)", "'   February 2024\\nMo Tu We Th Fr Sa Su\\n          1  2  3  4\\n 5  6  7  8  9 10 11\\n12 13 14 15 16 17 18\\n19 20 21 22 23 24 25\\n26 27 28 29\\n'")]
        [InlineData("calendar.month(2024, 2, w=3).split('\\n')[1]", "'Mon Tue Wed Thu Fri Sat Sun'")]
        [InlineData("calendar.month(2025, 1, w=4).split('\\n')[3]", "'  6    7    8    9   10   11   12'")]   // formatday centers '%2i' in w
        [InlineData("issubclass(calendar.IllegalMonthError, ValueError)", "True")]
        public void Surface(string expr, string expected)
        {
            string src = expr.StartsWith("import ") ? Imp + expr + "\n" : Imp + "r = (" + expr + ")\n";
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("monthrange(2024, 13)", "IllegalMonthError", "bad month number 13; must be 1-12")]
        [InlineData("monthrange(2024, 0)", "IllegalMonthError", "bad month number 0; must be 1-12")]
        [InlineData("weekday(2024, 2, 30)", "ValueError", "day is out of range for month")]
        [InlineData("monthrange('2024', 1)", "TypeError", "'str' object cannot be interpreted as an integer")]
        [InlineData("isleap()", "TypeError", "isleap() takes exactly one argument (0 given)")]
        [InlineData("timegm((2024, 13, 1, 0, 0, 0))", "ValueError", "month must be in 1..12")]
        public void Errors(string expr, string exc, string message)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + "r = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
            Assert.Equal(message, ex.Value.GetMessageText(Rt.Ctx()));
        }

        // mdays holds February at 28: monthrange is what accounts for a leap year. error is the
        // ValueError the two Illegal* errors derive from.
        [Theory]
        [InlineData("calendar.mdays[2]", "28")]
        [InlineData("calendar.mdays[1]", "31")]
        [InlineData("len(calendar.mdays)", "13")]
        [InlineData("calendar.mdays[0]", "0")]
        [InlineData("sum(calendar.mdays)", "365")]
        public void MDaysTable(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run(Imp + "r = " + expr + "\n").Repr("r"));
        }

        [Fact]
        public void ErrorCatchesTheIllegalMonth()
        {
            string src = Imp + "ok = False\ntry:\n    monthrange(2026, 13)\nexcept calendar.error:\n    ok = True\n";
            Assert.Equal("True", Ev.Run(src).Repr("ok"));
        }

        // The errors are real classes: a script can make one, type(e) is the class, error is ValueError (B13).
        [Fact]
        public void TheErrorsAreClasses()
        {
            string src = Imp + "e1 = calendar.IllegalMonthError(13)\ntry:\n    monthrange(2026, 13)\nexcept calendar.IllegalMonthError as e:\n    e2 = e\n"
                + "r = (str(e1), type(e2) is calendar.IllegalMonthError, calendar.error is ValueError)\n";
            Assert.Equal("('13', True, True)", Ev.Run(src).Repr("r"));
        }
    }
}
