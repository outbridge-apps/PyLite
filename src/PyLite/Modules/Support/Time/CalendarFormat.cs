using System;
using System.Collections.Generic;
using System.Globalization;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // The texts, tuples and day methods date, datetime, time and timedelta build the same way.
    internal static class CalendarFormat
    {
        internal static string Num(int n) { return n.ToString(CultureInfo.InvariantCulture); }

        internal static string Pad2(int n) { return n.ToString("00", CultureInfo.InvariantCulture); }

        internal static string IsoDate(int year, int month, int day)
        {
            return year.ToString("0000", CultureInfo.InvariantCulture) + "-" + Pad2(month) + "-" + Pad2(day);
        }

        // ctime(): "Wed Jun  9 04:26:40 1993".
        internal static string Ctime(int year, int month, int day, int hour, int minute, int second)
        {
            int wd = CalendarMath.Weekday(CalendarMath.Ymd2Ord(year, month, day));
            return DtNames.AbbrDays[wd] + " " + DtNames.AbbrMonths[month - 1] + " "
                + Num(day).PadLeft(2, ' ') + " " + Pad2(hour) + ":" + Pad2(minute) + ":" + Pad2(second) + " " + Num(year);
        }

        // The strftime input; time() formats as 1900-01-01, as CPython's timetuple does.
        internal static BrokenTime Broken(int year, int month, int day, int hour, int minute, int second,
            int microsecond, long? offsetUs, string tzName)
        {
            return new BrokenTime
            {
                Year = year, Month = month, Day = day,
                Hour = hour, Minute = minute, Second = second, Microsecond = microsecond,
                WeekdayMon0 = CalendarMath.Weekday(CalendarMath.Ymd2Ord(year, month, day)),
                DayOfYear = CalendarMath.DayOfYear(year, month, day),
                OffsetUs = offsetUs, TzName = tzName, HasMicroseconds = true,
            };
        }

        internal static ScriptValue TimeTuple(EvalContext ctx, int year, int month, int day, int hour, int minute,
            int second, int isdst)
        {
            return StructTimeValue.Create(ctx, year, month, day, hour, minute, second,
                CalendarMath.Weekday(CalendarMath.Ymd2Ord(year, month, day)), CalendarMath.DayOfYear(year, month, day), isdst);
        }

        // toordinal/weekday/isoweekday/isocalendar of a date or datetime, given its ordinal.
        internal static void AddDaySlots(IDictionary<string, SlotDescriptor> d, Func<ScriptValue, int> ordinal)
        {
            d["toordinal"] = SlotDescriptor.MakeMethod("toordinal", Args.Positional("toordinal", 0, 0,
                (self, a, kw, c) => c.Values.Int(ordinal(self))));
            d["weekday"] = SlotDescriptor.MakeMethod("weekday", Args.Positional("weekday", 0, 0,
                (self, a, kw, c) => c.Values.Int(CalendarMath.Weekday(ordinal(self)))));
            d["isoweekday"] = SlotDescriptor.MakeMethod("isoweekday", Args.Positional("isoweekday", 0, 0,
                (self, a, kw, c) => c.Values.Int(CalendarMath.Weekday(ordinal(self)) + 1)));
            d["isocalendar"] = SlotDescriptor.MakeMethod("isocalendar", Args.Positional("isocalendar", 0, 0, (self, a, kw, c) =>
            {
                int y, m, day, iy, iw, id;
                CalendarMath.Ord2Ymd(ordinal(self), out y, out m, out day);
                CalendarMath.IsoCalendar(y, m, day, out iy, out iw, out id);
                return c.Values.Tuple(new ScriptValue[] { c.Values.Int(iy), c.Values.Int(iw), c.Values.Int(id) });
            }));
        }
    }
}
