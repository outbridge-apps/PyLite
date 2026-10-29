using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/09 T-MDT-10: StrftimeFormatter (shared date/time/datetime formatter). t = 1993-06-20 23:21:05.000123.
    [Trait("Category", "Compatibility")]
    public class StrftimeTests
    {
        private const string Prelude = "from datetime import datetime, date, time, timezone, timedelta\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr);
        }

        private const string DT = "datetime(1993,6,20,23,21,5,123)";

        [Theory]
        [InlineData(DT + ".strftime('%Y-%m-%d %H:%M:%S')", "'1993-06-20 23:21:05'")]
        [InlineData(DT + ".strftime('%y')", "'93'")]
        [InlineData(DT + ".strftime('%f')", "'000123'")]
        [InlineData("datetime(1993,6,20,23).strftime('%I %p')", "'11 PM'")]
        [InlineData("datetime(1993,6,20,0).strftime('%I %p')", "'12 AM'")]
        [InlineData("datetime(1993,6,20,12).strftime('%I %p')", "'12 PM'")]
        [InlineData(DT + ".strftime('%a %A %b %B')", "'Sun Sunday Jun June'")]
        [InlineData(DT + ".strftime('%j')", "'171'")]
        // glibc flags and extra codes (CPython on Linux/macOS)
        [InlineData("datetime(2024, 1, 2, 3, 4, 5).strftime('%-d.%-m.%Y %-H:%M')", "'2.1.2024 3:04'")]
        [InlineData("datetime(2005, 1, 2).strftime('%-y|%_d|%_j|%0e|%^b|%^A|%#B|%#p')", "'5| 2|  2|02|JAN|SUNDAY|JANUARY|am'")]
        [InlineData("datetime(2024, 1, 2, 13, 4, 5).strftime('%e|%k|%l|%P|%C|%h|%n|%t')", "' 2|13| 1|pm|20|Jan|\\n|\\t'")]
        [InlineData("datetime(2024, 1, 2, 13, 4, 5).strftime('%D %F %T %R %r')", "'01/02/24 2024-01-02 13:04:05 13:04 01:04:05 PM'")]
        [InlineData("datetime(2021, 1, 3).strftime('%G-W%V-%u'), datetime(2024, 12, 30).strftime('%G-W%V-%u')", "('2020-W53-7', '2025-W01-1')")]
        [InlineData("datetime(1970, 1, 2).strftime('%s')", "'86400'")]
        [InlineData("datetime(2024, 1, 2).strftime('%-')", "'%-'")]
        [InlineData("datetime(2024, 1, 2).strftime('%-Q')", "'%-Q'")]
        [InlineData("datetime(2024, 1, 2, tzinfo=timezone(timedelta(hours=1))).strftime('%s')", "'1704153600'")]
        [InlineData(DT + ".strftime('%w')", "'0'")]
        [InlineData("date(1993,6,20).strftime('%U')", "'25'")]
        [InlineData("date(1993,6,20).strftime('%W')", "'24'")]
        [InlineData("date(2012,1,1).strftime('%U')", "'01'")]
        [InlineData("date(2012,1,1).strftime('%W')", "'00'")]
        [InlineData("date(2015,1,1).strftime('%U')", "'00'")]
        [InlineData("datetime(1993,6,6,8,15,0).strftime('%c')", "'Sun Jun  6 08:15:00 1993'")]
        [InlineData(DT + ".strftime('%x %X')", "'06/20/93 23:21:05'")]
        [InlineData(DT + ".strftime('%%d')", "'%d'")]
        [InlineData(DT + ".strftime('%q%')", "'%q%'")]
        [InlineData(DT + ".strftime('%z%Z')", "''")]
        [InlineData("datetime(1993,6,20,23,21,5,tzinfo=timezone(timedelta(hours=-4,minutes=-30))).strftime('%z %Z')", "'-0430 UTC-04:30'")]
        [InlineData("datetime(1993,6,20,tzinfo=timezone.utc).strftime('%z')", "'+0000'")]
        [InlineData("date(999,1,1).strftime('%Y')", "'0999'")]                    // RFC-003 §4: no year>=1000 gate
        [InlineData("time(9,5,0).strftime('%f')", "'000000'")]                    // time has microseconds
        [InlineData("format(date(2002,3,11), '%Y/%m/%d')", "'2002/03/11'")]       // __format__ via strftime
        public void StrftimeBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void LongFormatChargedNoHang()
        {
            Assert.Equal("4000", R("len(date(2020,1,1).strftime('%Y'*1000))"));
        }
    }
}
