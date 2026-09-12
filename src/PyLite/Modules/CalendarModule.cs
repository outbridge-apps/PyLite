using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // calendar: the functions over CalendarMath (Monday-first, as the module's default). The Calendar
    // classes, setfirstweekday (module state would leak between runs) and the locale variants are absent.
    public static class CalendarModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["isleap"] = BuiltinFunctionValue.Make("isleap", (s, a, kw, c) => c.Values.Bool(CalendarMath.IsLeap(IntArg(c, a, kw, "isleap", 1, 0)))),
                ["leapdays"] = BuiltinFunctionValue.Make("leapdays", Leapdays),
                ["weekday"] = BuiltinFunctionValue.Make("weekday", Weekday),
                ["monthrange"] = BuiltinFunctionValue.Make("monthrange", Monthrange),
                ["monthcalendar"] = BuiltinFunctionValue.Make("monthcalendar", Monthcalendar),
                ["month"] = BuiltinFunctionValue.Make("month", MonthText),
                ["weekheader"] = BuiltinFunctionValue.Make("weekheader", Weekheader),
                ["timegm"] = BuiltinFunctionValue.Make("timegm", Timegm),
                ["firstweekday"] = BuiltinFunctionValue.Make("firstweekday", (s, a, kw, c) => c.Values.Int(0)),
                ["month_name"] = Names(ctx, DtNames.FullMonths, true),
                ["month_abbr"] = Names(ctx, DtNames.AbbrMonths, true),
                ["day_name"] = Names(ctx, DtNames.FullDays, false),
                ["day_abbr"] = Names(ctx, DtNames.AbbrDays, false),
                ["IllegalMonthError"] = ctx.Values.Type("IllegalMonthError", null, null, null, PyExceptionTypes.IllegalMonthError),
                ["IllegalWeekdayError"] = ctx.Values.Type("IllegalWeekdayError", null, null, null, PyExceptionTypes.IllegalWeekdayError),
                // Lib/calendar.py exports both: February holds 28 here and leap years are handled by
                // monthrange, and `error` is the ValueError the two Illegal* errors derive from.
                ["mdays"] = MDays(ctx),
                ["error"] = ctx.Values.Type("error", null, null, null, PyExceptionTypes.ValueError),
            };
            string[] days = { "MONDAY", "TUESDAY", "WEDNESDAY", "THURSDAY", "FRIDAY", "SATURDAY", "SUNDAY" };
            for (int i = 0; i < days.Length; i++)
                m[days[i]] = ctx.Values.Int(i);
            for (int i = 0; i < 12; i++)
                m[DtNames.FullMonths[i].ToUpperInvariant()] = ctx.Values.Int(i + 1);
            return ctx.Values.Module("calendar", m);
        }

        // month_name/day_name are list-like sequences; the month ones carry '' at index 0 as in CPython.
        private static ListValue Names(EvalContext ctx, string[] names, bool leadingBlank)
        {
            ListValue l = ctx.Values.List(names.Length + 1);
            if (leadingBlank)
                l.Add(ctx.Values.Str(""), ctx);
            foreach (string n in names)
                l.Add(ctx.Values.Str(n), ctx);
            return l;
        }

        private static ListValue MDays(EvalContext ctx)
        {
            int[] days = { 0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
            ListValue l = ctx.Values.List(days.Length);
            foreach (int d in days)
                l.Add(ctx.Values.Int(d), ctx);
            return l;
        }

        private static int IntArg(EvalContext ctx, ScriptValue[] a, KwArgs kw, string fn, int arity, int idx)
        {
            Args.Exactly(ctx, a, kw, fn, arity);
            return IntOf(ctx, a[idx]);
        }

        private static int IntOf(EvalContext ctx, ScriptValue v)
        {
            if (v.Kind != ValueKind.Int && v.Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
            System.Numerics.BigInteger b = NumericOps.AsBigInteger(v);
            if (b < int.MinValue || b > int.MaxValue)
                throw Raise.Overflow(ctx, "Python int too large to convert to C int");
            return (int)b;
        }

        private static int MonthArg(EvalContext ctx, int month)
        {
            if (month < 1 || month > 12)
                throw Raise.Make(ctx, PyExceptionTypes.IllegalMonthError, "bad month number " + month + "; must be 1-12");
            return month;
        }

        private static void YearRange(EvalContext ctx, int year)
        {
            if (year < 1 || year > 9999)
                throw Raise.ValueError(ctx, "year " + year + " is out of range");
        }

        private static ScriptValue Leapdays(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, kw, "leapdays", 2);
            int y1 = IntOf(ctx, a[0]) - 1, y2 = IntOf(ctx, a[1]) - 1;
            int leaps = (CalendarMath.FloorDiv(y2, 4) - CalendarMath.FloorDiv(y1, 4))
                - (CalendarMath.FloorDiv(y2, 100) - CalendarMath.FloorDiv(y1, 100))
                + (CalendarMath.FloorDiv(y2, 400) - CalendarMath.FloorDiv(y1, 400));
            return ctx.Values.Int(leaps);
        }

        private static ScriptValue Weekday(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, kw, "weekday", 3);
            int y = IntOf(ctx, a[0]), mo = MonthArg(ctx, IntOf(ctx, a[1])), d = IntOf(ctx, a[2]);
            YearRange(ctx, y);
            if (d < 1 || d > CalendarMath.DaysInMonth(y, mo))
                throw Raise.ValueError(ctx, "day is out of range for month");
            return ctx.Values.Int(CalendarMath.Weekday(CalendarMath.Ymd2Ord(y, mo, d)));
        }

        private static ScriptValue Monthrange(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, kw, "monthrange", 2);
            int y = IntOf(ctx, a[0]), mo = MonthArg(ctx, IntOf(ctx, a[1]));
            YearRange(ctx, y);
            int first = CalendarMath.Weekday(CalendarMath.Ymd2Ord(y, mo, 1));
            return ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(first), ctx.Values.Int(CalendarMath.DaysInMonth(y, mo)) });
        }

        // Monday-first weeks; days outside the month are 0.
        private static ScriptValue Monthcalendar(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, kw, "monthcalendar", 2);
            int y = IntOf(ctx, a[0]), mo = MonthArg(ctx, IntOf(ctx, a[1]));
            YearRange(ctx, y);
            List<int[]> weeks = Weeks(y, mo);
            ListValue result = ctx.Values.List(weeks.Count);
            foreach (int[] w in weeks)
            {
                ListValue row = ctx.Values.List(7);
                foreach (int d in w)
                    row.Add(ctx.Values.Int(d), ctx);
                result.Add(row, ctx);
            }
            return result;
        }

        private static List<int[]> Weeks(int y, int mo)
        {
            int first = CalendarMath.Weekday(CalendarMath.Ymd2Ord(y, mo, 1));
            int ndays = CalendarMath.DaysInMonth(y, mo);
            var weeks = new List<int[]>();
            int day = 1 - first;
            while (day <= ndays)
            {
                var w = new int[7];
                for (int i = 0; i < 7; i++, day++)
                    w[i] = day >= 1 && day <= ndays ? day : 0;
                weeks.Add(w);
            }
            return weeks;
        }

        // weekheader(n): the day abbreviations cut/centered to n characters, space-joined.
        private static ScriptValue Weekheader(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            int n = IntArg(ctx, a, kw, "weekheader", 1, 0);
            return ctx.Values.Str(WeekHeader(n));
        }

        private static string WeekHeader(int n)
        {
            var parts = new string[7];
            for (int i = 0; i < 7; i++)
            {
                string name = n >= 9 ? DtNames.FullDays[i] : DtNames.AbbrDays[i];
                if (name.Length > n)
                    name = name.Substring(0, n);
                parts[i] = Center(name, n);
            }
            return string.Join(" ", parts);
        }

        private static string Center(string s, int width)
        {
            int pad = width - s.Length;
            if (pad <= 0)
                return s;
            int left = pad / 2 + (pad & width & 1);   // str.center's rounding
            return new string(' ', left) + s + new string(' ', pad - left);
        }

        // month(theyear, themonth, w=0, l=0): TextCalendar.formatmonth.
        private static ScriptValue MonthText(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, "month", 2, 4);
            int y = IntOf(ctx, a[0]), mo = MonthArg(ctx, IntOf(ctx, a[1]));
            YearRange(ctx, y);
            ScriptValue wv = a.Length > 2 ? a[2] : null, lv = a.Length > 3 ? a[3] : null;
            ScriptValue v;
            if (kw.TryGet("w", out v))
                wv = v;
            if (kw.TryGet("l", out v))
                lv = v;
            int w = Math.Max(2, wv == null ? 0 : IntOf(ctx, wv));
            int l = Math.Max(1, lv == null ? 0 : IntOf(ctx, lv));
            var sb = new StringBuilder();
            string title = DtNames.FullMonths[mo - 1] + " " + y.ToString(CultureInfo.InvariantCulture);
            sb.Append(Center(title, 7 * (w + 1) - 1).TrimEnd()).Append('\n');
            for (int i = 1; i < l; i++)
                sb.Append('\n');
            sb.Append(WeekHeader(w).TrimEnd()).Append('\n');
            for (int i = 1; i < l; i++)
                sb.Append('\n');
            foreach (int[] week in Weeks(y, mo))
            {
                var parts = new string[7];
                for (int i = 0; i < 7; i++)
                    parts[i] = week[i] == 0 ? new string(' ', w) : Center(week[i].ToString(CultureInfo.InvariantCulture).PadLeft(2), w);   // formatday: '%2i' centered
                sb.Append(string.Join(" ", parts).TrimEnd()).Append('\n');
                for (int i = 1; i < l; i++)
                    sb.Append('\n');
            }
            return ctx.Values.Str(sb.ToString());
        }

        // timegm(tuple): the inverse of time.gmtime over the first six fields of a struct_time or tuple.
        private static ScriptValue Timegm(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, kw, "timegm", 1);
            // a struct_time or any sequence: the first six fields are read through iteration
            IScriptIterator it = a[0].Kind == ValueKind.Str ? null : PyOps.TryGetIterator(a[0], ctx);
            var f = new List<ScriptValue>(9);
            ScriptValue item;
            while (it != null && f.Count < 6 && it.MoveNext(ctx, out item))
                f.Add(item);
            if (f.Count < 6)
                throw Raise.TypeError(ctx, "timegm() argument must be a struct_time or a tuple of at least 6 items");
            int y = IntOf(ctx, f[0]), mo = IntOf(ctx, f[1]), d = IntOf(ctx, f[2]);
            int h = IntOf(ctx, f[3]), mi = IntOf(ctx, f[4]), s = IntOf(ctx, f[5]);
            if (mo < 1 || mo > 12)
                throw Raise.ValueError(ctx, "month must be in 1..12");
            // CPython normalizes an out-of-range day through the ordinal arithmetic, so no day check here
            long epoch = (long)(CalendarMath.Ymd2Ord(y, mo, 1) - CalendarMath.EpochOrdinal + (d - 1)) * 86400L + h * 3600L + mi * 60L + s;
            return ctx.Values.Int(epoch);
        }
    }
}
