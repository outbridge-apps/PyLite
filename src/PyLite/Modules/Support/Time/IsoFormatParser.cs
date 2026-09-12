using System;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // fromisoformat() for datetime/date/time: a port of CPython 3.11+'s _parse_isoformat_* helpers
    // (Lib/_pydatetime.py, digit-strict as the C module is). A grammar error is "Invalid isoformat
    // string: '...'"; an out-of-range field is left to the constructor, which names it as CPython does.
    internal static class IsoFormatParser
    {
        private sealed class BadFormat : Exception { }

        private static readonly int[] FractionCorrection = { 100000, 10000, 1000, 100, 10 };

        internal static DateTimeValue DateTime(EvalContext ctx, string s)
        {
            int y, mo, d;
            int[] t = { 0, 0, 0, 0 };
            TimezoneValue tz = null;
            bool nextDay = false, bad24 = false;
            try
            {
                if (s.Length < 7)
                    throw new BadFormat();
                int sep = FindSeparator(s);
                string dstr = s.Substring(0, Math.Min(sep, s.Length));
                string tstr = sep + 1 < s.Length ? s.Substring(sep + 1) : "";
                ParseDate(dstr, out y, out mo, out d);
                if (tstr.Length > 0)
                    tz = ParseTime(ctx, tstr, t, out nextDay, out bad24);
            }
            catch (BadFormat)
            {
                throw Invalid(ctx, s);
            }
            if (bad24)
                throw Raise.ValueError(ctx, "minute, second, and microsecond must be 0 when hour is 24");
            if (nextDay && y >= 1 && y <= 9999 && mo >= 1 && mo <= 12 && d >= 1 && d <= CalendarMath.DaysInMonth(y, mo))
                CalendarMath.Ord2Ymd(CalendarMath.Ymd2Ord(y, mo, d) + 1, out y, out mo, out d);
            return DateTimeValue.Construct(ctx, y, mo, d, t[0], t[1], t[2], t[3], tz);
        }

        internal static DateValue Date(EvalContext ctx, string s)
        {
            int y, mo, d;
            try
            {
                ParseDate(s, out y, out mo, out d);
            }
            catch (BadFormat)
            {
                throw Invalid(ctx, s);
            }
            return DateValue.Construct(ctx, y, mo, d);
        }

        internal static TimeValue Time(EvalContext ctx, string s)
        {
            int[] t = { 0, 0, 0, 0 };
            TimezoneValue tz;
            bool nextDay, bad24;
            try
            {
                string tstr = s.Length > 0 && s[0] == 'T' ? s.Substring(1) : s;
                tz = ParseTime(ctx, tstr, t, out nextDay, out bad24);
            }
            catch (BadFormat)
            {
                throw Invalid(ctx, s);
            }
            if (bad24)
                throw Raise.ValueError(ctx, "minute, second, and microsecond must be 0 when hour is 24");
            return TimeValue.Construct(ctx, t[0], t[1], t[2], t[3], tz);
        }

        private static ScriptException Invalid(EvalContext ctx, string s)
        {
            return Raise.ValueError(ctx, "Invalid isoformat string: " + ctx.Values.Str(s).Repr(ctx));
        }

        // Where the date part ends: the character there (any character) is the separator. Mirrors
        // _find_isoformat_datetime_separator, including its YYYY-Www-D-vs-time guess.
        private static int FindSeparator(string s)
        {
            int n = s.Length;
            if (n == 7)
                return 7;
            if (s[4] == '-')
            {
                if (s[5] != 'W')
                    return 10;
                if (n < 8)
                    throw new BadFormat();
                if (n > 8 && s[8] == '-')
                {
                    if (n == 9)
                        throw new BadFormat();
                    if (n > 10 && IsDigit(s[10]))
                        return 8;
                    return 10;
                }
                return 8;
            }
            if (s[4] == 'W')
            {
                int idx = 7;
                while (idx < n && IsDigit(s[idx]))
                    idx++;
                if (idx < 9)
                    return idx;
                return idx % 2 == 0 ? 7 : 8;
            }
            return 8;
        }

        // YYYY-MM-DD, YYYYMMDD, YYYY-Www[-D], YYYYWww[D]
        private static void ParseDate(string s, out int year, out int month, out int day)
        {
            int n = s.Length;
            if (n != 7 && n != 8 && n != 10)
                throw new BadFormat();
            year = Int(s, 0, 4);
            bool hasSep = s[4] == '-';
            int pos = hasSep ? 5 : 4;
            if (pos < n && s[pos] == 'W')
            {
                pos++;
                int week = Int(s, pos, 2);
                pos += 2;
                int weekday = 1;
                if (n > pos)
                {
                    if ((s[pos] == '-') != hasSep)
                        throw new BadFormat();
                    if (hasSep)
                        pos++;
                    weekday = Int(s, pos, 1);
                }
                IsoWeekToGregorian(year, week, weekday, out year, out month, out day);
                return;
            }
            month = Int(s, pos, 2);
            pos += 2;
            if ((pos < n && s[pos] == '-') != hasSep)
                throw new BadFormat();
            if (hasSep)
                pos++;
            day = Int(s, pos, 2);
        }

        private static void IsoWeekToGregorian(int year, int week, int weekday, out int y, out int m, out int d)
        {
            if (year < 1 || year > 9999)
                throw new BadFormat();
            if (week < 1 || week > 53)
                throw new BadFormat();
            if (week == 53)
            {
                // 53-week ISO years start on a Thursday, or on a Wednesday when leap
                int firstWeekday = CalendarMath.Ymd2Ord(year, 1, 1) % 7;
                if (!(firstWeekday == 4 || (firstWeekday == 3 && CalendarMath.IsLeap(year))))
                    throw new BadFormat();
            }
            if (weekday < 1 || weekday > 7)
                throw new BadFormat();
            int firstDay = CalendarMath.Ymd2Ord(year, 1, 1);
            int firstWd = (firstDay + 6) % 7;
            int week1Monday = firstDay - firstWd;
            if (firstWd > 3)
                week1Monday += 7;
            CalendarMath.Ord2Ymd(week1Monday + (week - 1) * 7 + (weekday - 1), out y, out m, out d);
        }

        // HH[:?MM[:?SS[{.,}f{1,6}...]]][(+|-)HH[:?MM[:?SS[.ffffff]]]|Z]; fills t = {h, m, s, us}.
        private static TimezoneValue ParseTime(EvalContext ctx, string tstr, int[] t, out bool nextDay, out bool bad24)
        {
            nextDay = false;
            bad24 = false;
            int n = tstr.Length;
            if (n < 2)
                throw new BadFormat();
            int tzPos = tstr.IndexOf('-') + 1;
            if (tzPos == 0)
                tzPos = tstr.IndexOf('+') + 1;
            if (tzPos == 0)
                tzPos = tstr.IndexOf('Z') + 1;
            string timestr = tzPos > 0 ? tstr.Substring(0, tzPos - 1) : tstr;
            ParseHhMmSsFf(timestr, t);
            if (t[0] == 24)
            {
                if (t[1] == 0 && t[2] == 0 && t[3] == 0)
                {
                    t[0] = 0;
                    nextDay = true;
                }
                else
                    bad24 = true;
            }
            if (tzPos == n && tstr[n - 1] == 'Z')
                return TimezoneValue.Utc;
            if (tzPos == 0)
                return null;
            string tzstr = tstr.Substring(tzPos);
            if (tzstr.Length == 0 || tzstr.Length == 1 || tzstr.Length == 3)
                throw new BadFormat();
            int[] z = { 0, 0, 0, 0 };
            ParseHhMmSsFf(tzstr, z);
            if (z[0] == 0 && z[1] == 0 && z[2] == 0 && z[3] == 0)
                return TimezoneValue.Utc;
            long sign = tstr[tzPos - 1] == '-' ? -1 : 1;
            long offsetUs = sign * ((z[0] * 3600L + z[1] * 60L + z[2]) * 1000000L + z[3]);
            if (offsetUs <= -86400000000L || offsetUs >= 86400000000L)
                throw Raise.ValueError(ctx, "offset must be a timedelta strictly between -timedelta(hours=24) and timedelta(hours=24).");
            return TimezoneValue.FromOffsetUs(ctx, offsetUs, null);
        }

        private static void ParseHhMmSsFf(string s, int[] t)
        {
            int n = s.Length;
            int pos = 0;
            bool hasSep = false;
            for (int comp = 0; comp < 3; comp++)
            {
                if (n - pos < 2)
                    throw new BadFormat();
                t[comp] = Int(s, pos, 2);
                pos += 2;
                char next = pos < n ? s[pos] : '\0';
                if (comp == 0)
                    hasSep = next == ':';
                if (next == '\0' || comp >= 2)
                    break;
                if (hasSep && next != ':')
                    throw new BadFormat();
                if (hasSep)
                    pos++;
            }
            if (pos >= n)
                return;
            if (s[pos] != '.' && s[pos] != ',')
                throw new BadFormat();
            pos++;
            int remainder = n - pos;
            int toParse = remainder >= 6 ? 6 : remainder;
            t[3] = Int(s, pos, toParse);
            if (toParse < 6)
                t[3] *= FractionCorrection[toParse - 1];
            for (int i = pos + toParse; i < n; i++)
                if (!IsDigit(s[i]))
                    throw new BadFormat();
        }

        private static int Int(string s, int start, int count)
        {
            if (count <= 0 || start + count > s.Length)
                throw new BadFormat();
            int v = 0;
            for (int i = 0; i < count; i++)
            {
                char c = s[start + i];
                if (!IsDigit(c))
                    throw new BadFormat();
                v = v * 10 + (c - '0');
            }
            return v;
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }
    }
}
