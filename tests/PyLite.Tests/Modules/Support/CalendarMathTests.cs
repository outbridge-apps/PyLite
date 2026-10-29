using Outbridge.PyLite.Modules.Support;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules.Support
{
    // impl/09 T-MDT-01: proleptic Gregorian calendar port.
    [Trait("Category", "Compatibility")]
    public class CalendarMathTests
    {
        [Theory]
        [InlineData(1, 1, 1, 1)]
        [InlineData(1970, 1, 1, 719163)]
        [InlineData(9999, 12, 31, 3652059)]
        [InlineData(2002, 3, 11, 730920)]
        public void Ymd2OrdKnown(int y, int m, int d, int ord)
        {
            Assert.Equal(ord, CalendarMath.Ymd2Ord(y, m, d));
        }

        [Theory]
        [InlineData(1, 1, 1, 1)]
        [InlineData(719163, 1970, 1, 1)]
        [InlineData(730920, 2002, 3, 11)]
        [InlineData(3652059, 9999, 12, 31)]
        public void Ord2YmdKnown(int ord, int y, int m, int d)
        {
            int yy, mm, dd;
            CalendarMath.Ord2Ymd(ord, out yy, out mm, out dd);
            Assert.Equal((y, m, d), (yy, mm, dd));
        }

        [Theory]
        [InlineData(1999)]
        [InlineData(2000)]
        [InlineData(2001)]
        [InlineData(1899)]
        [InlineData(1900)]
        [InlineData(1901)]
        [InlineData(2099)]
        [InlineData(2100)]
        [InlineData(2101)]
        public void OrdinalRoundtrip(int year)
        {
            for (int month = 1; month <= 12; month++)
            {
                int dim = CalendarMath.DaysInMonth(year, month);
                for (int day = 1; day <= dim; day++)
                {
                    int ord = CalendarMath.Ymd2Ord(year, month, day);
                    int yy, mm, dd;
                    CalendarMath.Ord2Ymd(ord, out yy, out mm, out dd);
                    Assert.Equal((year, month, day), (yy, mm, dd));
                }
            }
        }

        [Theory]
        [InlineData(2000, true)]
        [InlineData(1900, false)]
        [InlineData(2024, true)]
        [InlineData(2026, false)]
        public void IsLeapKnown(int year, bool leap)
        {
            Assert.Equal(leap, CalendarMath.IsLeap(year));
        }

        [Theory]
        [InlineData(2024, 2, 29)]
        [InlineData(2025, 2, 28)]
        public void DaysInMonthKnown(int y, int m, int days)
        {
            Assert.Equal(days, CalendarMath.DaysInMonth(y, m));
        }

        [Fact]
        public void WeekdayMonday()
        {
            Assert.Equal(0, CalendarMath.Weekday(CalendarMath.Ymd2Ord(2002, 3, 11)));
        }

        [Theory]
        [InlineData(2003, 12, 29, 2004, 1, 1)]
        [InlineData(2004, 1, 4, 2004, 1, 7)]
        [InlineData(2016, 1, 1, 2015, 53, 5)]
        [InlineData(2019, 12, 31, 2020, 1, 2)]
        [InlineData(2015, 12, 31, 2015, 53, 4)]
        public void IsoCalendarKnown(int y, int m, int d, int iy, int iw, int id)
        {
            int isoY, isoW, isoD;
            CalendarMath.IsoCalendar(y, m, d, out isoY, out isoW, out isoD);
            Assert.Equal((iy, iw, id), (isoY, isoW, isoD));
        }

        [Fact]
        public void DayOfYearEndOfLeap()
        {
            Assert.Equal(366, CalendarMath.DayOfYear(2024, 12, 31));
        }

        [Fact]
        public void FlooredDivMod()
        {
            Assert.Equal(-1, CalendarMath.FloorDiv(-1, 7));
            Assert.Equal(6, CalendarMath.FloorMod(-1, 7));
        }
    }
}
