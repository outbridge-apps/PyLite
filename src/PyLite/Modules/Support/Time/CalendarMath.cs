using System;

namespace Outbridge.PyLite.Modules.Support
{
    // Proleptic Gregorian calendar math, a literal port of the Lib/datetime.py functions.
    // Pure statics: int/long only, no BigInteger (every range fits in int — MaxOrdinal = 3_652_059), no
    // System.DateTime / ISOWeek / GregorianCalendar, allocation-free, recursion-free, checked arithmetic.
    internal static partial class CalendarMath
    {
        public const int EpochOrdinal = 719163;         // date(1970,1,1).toordinal()
        public const long SecondsPerDay = 86400;
        public const long UsPerSecond = 1000000;
        public const long UsPerDay = 86400000000;
        public const int MaxOrdinal = 3652059;          // date(9999,12,31).toordinal()

        // index 0 unused; DAYS_IN_MONTH[m] = days in month m of a non-leap year.
        private static readonly int[] DaysInMonthTable = { -1, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        // cumulative days before the 1st of month m in a non-leap year.
        private static readonly int[] DaysBeforeMonthTable = { -1, 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 };

        private const int DI400Y = 146097;   // days in 400 years
        private const int DI100Y = 36524;     // days in 100 years
        private const int DI4Y = 1461;        // days in 4 years

        // ---- floored division/modulo (C# / and % truncate toward zero; b > 0 always here) ----
        public static int FloorDiv(int a, int b)
        {
            return (a - FloorMod(a, b)) / b;
        }

        public static int FloorMod(int a, int b)
        {
            return ((a % b) + b) % b;
        }

        public static long FloorDiv(long a, long b)
        {
            return (a - FloorMod(a, b)) / b;
        }

        public static long FloorMod(long a, long b)
        {
            return ((a % b) + b) % b;
        }

        public static bool IsLeap(int year)
        {
            return year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
        }

        public static int DaysInMonth(int year, int month)
        {
            if (month == 2 && IsLeap(year))
                return 29;
            return DaysInMonthTable[month];
        }

        public static int DaysBeforeYear(int year)
        {
            int y = year - 1;
            return y * 365 + y / 4 - y / 100 + y / 400;
        }

        public static int DaysBeforeMonth(int year, int month)
        {
            return DaysBeforeMonthTable[month] + (month > 2 && IsLeap(year) ? 1 : 0);
        }

        // Ordinal of a proleptic Gregorian date. INPUT MUST ALREADY BE VALID.
        public static int Ymd2Ord(int year, int month, int day)
        {
            return DaysBeforeYear(year) + DaysBeforeMonth(year, month) + day;
        }

        // Port of Lib/datetime.py::_ord2ymd. n in 1..MaxOrdinal (all operands non-negative => / and % are floored).
        public static void Ord2Ymd(int n, out int year, out int month, out int day)
        {
            n -= 1;
            int n400 = n / DI400Y;
            n %= DI400Y;
            year = n400 * 400 + 1;

            int n100 = n / DI100Y;
            n %= DI100Y;
            int n4 = n / DI4Y;
            n %= DI4Y;
            int n1 = n / 365;
            n %= 365;

            year += n100 * 100 + n4 * 4 + n1;
            if (n1 == 4 || n100 == 4)
            {
                year -= 1;
                month = 12;
                day = 31;
                return;
            }

            bool leap = n1 == 3 && (n4 != 24 || n100 == 3);
            month = (n + 50) >> 5;
            int preceding = DaysBeforeMonthTable[month] + (month > 2 && leap ? 1 : 0);
            if (preceding > n)
            {
                month -= 1;
                preceding -= DaysInMonthTable[month] + (month == 2 && leap ? 1 : 0);
            }
            day = n - preceding + 1;
        }

        // Mon=0 .. Sun=6.
        public static int Weekday(int ordinal)
        {
            return FloorMod(ordinal + 6, 7);
        }

        // Day-of-year, 1..366.
        public static int DayOfYear(int year, int month, int day)
        {
            return Ymd2Ord(year, month, day) - Ymd2Ord(year, 1, 1) + 1;
        }

        // Ordinal of the Monday starting ISO week 1 of the given year (port of _isoweek1monday).
        private static int IsoWeek1Monday(int year)
        {
            int firstDay = Ymd2Ord(year, 1, 1);
            int firstWeekday = FloorMod(firstDay + 6, 7);   // Mon=0
            int week1Monday = firstDay - firstWeekday;
            if (firstWeekday > 3)                            // 3 = Thursday
                week1Monday += 7;
            return week1Monday;
        }

        // Port of date.isocalendar. isoYear/isoWeek/isoDay (isoDay: Mon=1..Sun=7).
        public static void IsoCalendar(int year, int month, int day, out int isoYear, out int isoWeek, out int isoDay)
        {
            isoYear = year;
            int today = Ymd2Ord(year, month, day);
            int w1m = IsoWeek1Monday(isoYear);
            int week = FloorDiv(today - w1m, 7);
            int weekday = FloorMod(today - w1m, 7);
            if (week < 0)
            {
                isoYear -= 1;
                w1m = IsoWeek1Monday(isoYear);
                week = FloorDiv(today - w1m, 7);
                weekday = FloorMod(today - w1m, 7);
            }
            else if (week >= 52 && today >= IsoWeek1Monday(isoYear + 1))
            {
                isoYear += 1;
                week = 0;
            }
            isoWeek = week + 1;
            isoDay = weekday + 1;
        }
    }
}
