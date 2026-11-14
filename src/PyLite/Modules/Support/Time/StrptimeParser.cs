using System;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Modules.Support
{
    // A hand-written strptime scanner, a port of the LOGIC of Lib/_strptime.py WITHOUT any
    // Regex (our re engine has no business on this critical path). O(len(fmt)+len(data)); Budget.Step per code
    // and per 64 input characters. Error texts, verbatim.
    internal sealed class StrptimeResult
    {
        public int Year = 1900;
        public int Month = 1;
        public int Day = 1;
        public int Hour;
        public int Minute;
        public int Second;
        public int Microsecond;
        public int? WeekdayMon0;
        public int? DayOfYear;
        public int? WeekOfYear;
        public bool WeekStartsMonday;
        public int? OffsetSeconds;
        public int OffsetMicros;     // %z fraction, signed like OffsetSeconds
        public int TmIsdst = -1;
        public int? Meridiem;        // 0 = AM, 1 = PM
        public bool SawHour12;
        public bool SawYear;
        public int? IsoYear;         // %G
        public int? IsoWeek;         // %V
    }

    internal static class StrptimeParser
    {
        // Valid strptime directive letters; %c/%x/%X expand to constant sub-formats.
        private const string ValidCodes = "YymdHIMSfjUWwaAbBpZzcxXGVu%";

        public static StrptimeResult Parse(EvalContext ctx, string data, string fmt)
        {
            // CPython validates the format (stray %, bad directive) at compile time, before touching the data.
            ValidateFormat(ctx, fmt);
            var r = new StrptimeResult();
            int di = 0;
            Scan(ctx, data, ref di, fmt, r, data, fmt);
            if (di != data.Length)
                throw Raise.ValueError(ctx, "unconverted data remains: " + data.Substring(di));
            IsoDate(ctx, r);
            // CPython's _strptime builds datetime_date(year, month, day), which rejects Feb 29 in a common year.
            if (r.DayOfYear == null && (r.Day < 1 || r.Day > CalendarMath.DaysInMonth(r.Year, r.Month)))
                throw Raise.ValueError(ctx, "day is out of range for month");
            ResolveDate(ctx, r);
            return r;
        }

        private static void ValidateFormat(EvalContext ctx, string fmt)
        {
            int i = 0;
            while (i < fmt.Length)
            {
                if (fmt[i] != '%')
                {
                    i++;
                    continue;
                }
                if (i == fmt.Length - 1)
                    throw Raise.ValueError(ctx, "stray % in format '" + fmt + "'");
                char code = fmt[i + 1];
                if (ValidCodes.IndexOf(code) < 0)
                    throw Raise.ValueError(ctx, "'" + code + "' is a bad directive in format '" + fmt + "'");
                i += 2;
            }
        }

        private static void Scan(EvalContext ctx, string data, ref int di, string fmt, StrptimeResult r, string topData, string topFmt)
        {
            int fi = 0;
            int since = 0;
            while (fi < fmt.Length)
            {
                if (++since >= 64)
                {
                    since = 0;
                    ctx.Budget.Step();
                }
                char fc = fmt[fi];

                if (PyUnicode.IsPythonSpace(fc))
                {
                    int f0 = fi, d0 = di;
                    while (fi < fmt.Length && PyUnicode.IsPythonSpace(fmt[fi]))
                        fi++;
                    if (di >= data.Length || !PyUnicode.IsPythonSpace(data[di]))
                        throw NoMatch(ctx, topData, topFmt);
                    while (di < data.Length && PyUnicode.IsPythonSpace(data[di]))
                        di++;
                    ctx.Budget.ChargeLinear((fi - f0) + (di - d0));   // a 15M-char run was 3 s on ~150 steps
                    continue;
                }

                if (fc == '%')
                {
                    if (fi == fmt.Length - 1)
                        throw Raise.ValueError(ctx, "stray % in format '" + topFmt + "'");
                    char code = fmt[fi + 1];
                    fi += 2;
                    HandleCode(ctx, code, data, ref di, r, topData, topFmt);
                    continue;
                }

                if (di >= data.Length || !EqCi(data[di], fc))
                    throw NoMatch(ctx, topData, topFmt);
                di++;
                fi++;
            }
        }

        private static void HandleCode(EvalContext ctx, char code, string data, ref int di, StrptimeResult r, string topData, string topFmt)
        {
            ctx.Budget.Step();
            switch (code)
            {
                case 'Y': r.Year = Num(ctx, data, ref di, 4, 4, 0, 9999, topData, topFmt); r.SawYear = true; break;
                case 'G': r.IsoYear = Num(ctx, data, ref di, 4, 4, 0, 9999, topData, topFmt); break;
                case 'V':
                {
                    int at = di;
                    r.IsoWeek = Num(ctx, data, ref di, 1, 2, 0, 53, topData, topFmt);
                    if (r.IsoWeek == 0 && di - at == 2)
                        di--;   // CPython's V regex has no '00': it takes one '0', and the rest no longer matches
                    break;
                }
                case 'u': r.WeekdayMon0 = Num(ctx, data, ref di, 1, 1, 1, 7, topData, topFmt) - 1; break;
                case 'y':
                {
                    int yy = Num(ctx, data, ref di, 2, 2, 0, 99, topData, topFmt);
                    r.Year = yy <= 68 ? 2000 + yy : 1900 + yy;
                    r.SawYear = true;
                    break;
                }
                case 'm': r.Month = Num(ctx, data, ref di, 1, 2, 1, 12, topData, topFmt); break;
                case 'd':
                    if (di + 1 < data.Length && data[di] == ' ' && data[di + 1] >= '1' && data[di + 1] <= '9')
                        di++;   // TimeRE's " [1-9]"
                    r.Day = Num(ctx, data, ref di, 1, 2, 1, 31, topData, topFmt);
                    break;
                case 'H': r.Hour = Num(ctx, data, ref di, 1, 2, 0, 23, topData, topFmt); break;
                case 'I': r.Hour = Num(ctx, data, ref di, 1, 2, 1, 12, topData, topFmt); r.SawHour12 = true; break;
                case 'M': r.Minute = Num(ctx, data, ref di, 1, 2, 0, 59, topData, topFmt); break;
                case 'S': r.Second = Num(ctx, data, ref di, 1, 2, 0, 61, topData, topFmt); break;
                case 'f': r.Microsecond = Fraction(ctx, data, ref di, topData, topFmt); break;
                case 'j': r.DayOfYear = Num(ctx, data, ref di, 1, 3, 1, 366, topData, topFmt); break;
                case 'U': r.WeekOfYear = Num(ctx, data, ref di, 1, 2, 0, 53, topData, topFmt); r.WeekStartsMonday = false; break;
                case 'W': r.WeekOfYear = Num(ctx, data, ref di, 1, 2, 0, 53, topData, topFmt); r.WeekStartsMonday = true; break;
                case 'w':
                {
                    int w = Num(ctx, data, ref di, 1, 1, 0, 6, topData, topFmt);
                    r.WeekdayMon0 = w == 0 ? 6 : w - 1;
                    break;
                }
                case 'a': r.WeekdayMon0 = Name(ctx, data, ref di, DtNames.AbbrDays, topData, topFmt); break;
                case 'A': r.WeekdayMon0 = Name(ctx, data, ref di, DtNames.FullDays, topData, topFmt); break;
                case 'b': r.Month = Name(ctx, data, ref di, DtNames.AbbrMonths, topData, topFmt) + 1; break;
                case 'B': r.Month = Name(ctx, data, ref di, DtNames.FullMonths, topData, topFmt) + 1; break;
                case 'p': r.Meridiem = Name(ctx, data, ref di, Meridiems, topData, topFmt); break;
                case 'Z': Name(ctx, data, ref di, Zones, topData, topFmt); r.TmIsdst = 0; break;
                case 'z': r.OffsetSeconds = Tz(ctx, data, ref di, out r.OffsetMicros, topData, topFmt); break;
                case 'c': Scan(ctx, data, ref di, "%a %b %d %H:%M:%S %Y", r, topData, topFmt); break;
                case 'x': Scan(ctx, data, ref di, "%m/%d/%y", r, topData, topFmt); break;
                case 'X': Scan(ctx, data, ref di, "%H:%M:%S", r, topData, topFmt); break;
                case '%':
                    if (di >= data.Length || data[di] != '%')
                        throw NoMatch(ctx, topData, topFmt);
                    di++;
                    break;
                default:
                    throw Raise.ValueError(ctx, "'" + code + "' is a bad directive in format '" + topFmt + "'");
            }
        }

        private static readonly string[] Meridiems = { "AM", "PM" };
        private static readonly string[] Zones = { "UTC", "GMT" };

        // Greedy numeric field: read up to maxDigits digits, then shrink toward minDigits until the value is in
        // [lo..hi] (the TimeRE alternation rule, e.g. '13' with %m -> 1 leaving '3').
        private static int Num(EvalContext ctx, string data, ref int di, int minDigits, int maxDigits, int lo, int hi, string topData, string topFmt)
        {
            int count = 0;
            while (count < maxDigits && di + count < data.Length && IsDigit(data[di + count]))
                count++;
            if (count < minDigits)
                throw NoMatch(ctx, topData, topFmt);
            for (int k = count; k >= minDigits; k--)
            {
                int val = ParseDigits(data, di, k);
                if (val >= lo && val <= hi)
                {
                    di += k;
                    return val;
                }
            }
            throw NoMatch(ctx, topData, topFmt);
        }

        private static int Fraction(EvalContext ctx, string data, ref int di, string topData, string topFmt)
        {
            int count = 0;
            while (count < 6 && di + count < data.Length && IsDigit(data[di + count]))
                count++;
            if (count < 1)
                throw NoMatch(ctx, topData, topFmt);
            int val = ParseDigits(data, di, count);
            di += count;
            for (int i = count; i < 6; i++)
                val *= 10;   // right-pad with zeros to 6
            return val;
        }

        // %z: [+-]HH[:]MM[[:]SS[.f{1,6}]] or a literal Z (3.7+). The colon is optional but must be used
        // consistently (CPython's "Inconsistent use of :"); the hour range is the timezone() ctor's job.
        private static int Tz(EvalContext ctx, string data, ref int di, out int micros, string topData, string topFmt)
        {
            micros = 0;
            if (di < data.Length && data[di] == 'Z')
            {
                di++;
                return 0;
            }
            if (di >= data.Length || (data[di] != '+' && data[di] != '-'))
                throw NoMatch(ctx, topData, topFmt);
            int start = di;
            int sign = data[di] == '-' ? -1 : 1;
            int p = di + 1;
            int hh = TwoDigits(data, p);
            if (hh < 0)
                throw NoMatch(ctx, topData, topFmt);
            p += 2;
            bool colon = p < data.Length && data[p] == ':';
            if (colon)
                p++;
            int mm = TwoDigits(data, p);
            if (mm < 0 || mm > 59)
                throw NoMatch(ctx, topData, topFmt);
            p += 2;
            int ss = 0;
            int q = p;
            bool colon2 = q < data.Length && data[q] == ':';
            if (colon2)
                q++;
            int sec = TwoDigits(data, q);
            if (sec >= 0 && sec <= 59)
            {
                if (colon2 != colon)
                    throw Raise.ValueError(ctx, "Inconsistent use of : in " + data.Substring(start, q + 2 - start));
                ss = sec;
                p = q + 2;
                if (p + 1 < data.Length && data[p] == '.' && IsDigit(data[p + 1]))
                {
                    int f = p + 1;
                    int n = 0;
                    while (n < 6 && f + n < data.Length && IsDigit(data[f + n]))
                        n++;
                    micros = ParseDigits(data, f, n);
                    for (int k = n; k < 6; k++)
                        micros *= 10;
                    p = f + n;
                }
            }
            di = p;
            micros *= sign;
            return sign * (hh * 3600 + mm * 60 + ss);
        }

        private static int TwoDigits(string data, int at)
        {
            if (at + 1 >= data.Length || !IsDigit(data[at]) || !IsDigit(data[at + 1]))
                return -1;
            return ParseDigits(data, at, 2);
        }

        // Case-insensitive longest-match against a name table; returns the index.
        private static int Name(EvalContext ctx, string data, ref int di, string[] table, string topData, string topFmt)
        {
            int bestLen = -1;
            int bestIdx = -1;
            for (int i = 0; i < table.Length; i++)
            {
                string cand = table[i];
                if (cand.Length > bestLen
                    && di + cand.Length <= data.Length
                    && string.Compare(data, di, cand, 0, cand.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    bestLen = cand.Length;
                    bestIdx = i;
                }
            }
            if (bestIdx < 0)
                throw NoMatch(ctx, topData, topFmt);
            di += bestLen;
            return bestIdx;
        }

        // ---- post-processing: %U/%W reconstruction, weekday, %p ----

        // _strptime's ISO week date: %G with %V and a weekday, and its texts for the other mixes
        private static void IsoDate(EvalContext ctx, StrptimeResult r)
        {
            const string Weekday = " and a weekday directive ('%A', '%a', '%w', or '%u').";
            if (r.IsoYear != null)
            {
                if (r.DayOfYear != null)
                    throw Raise.ValueError(ctx, "Day of the year directive '%j' is not compatible with ISO year directive '%G'. Use '%Y' instead.");
                if (r.IsoWeek == null || r.WeekdayMon0 == null)
                    throw Raise.ValueError(ctx, "ISO year directive '%G' must be used with the ISO week directive '%V'" + Weekday);
                if (r.IsoYear.Value < 1)
                    throw Raise.ValueError(ctx, "Year is out of range: " + r.IsoYear.Value);   // fromisocalendar's text; year 0 has no ordinal
                int week = r.IsoWeek.Value;
                if (week < 1 || week > 53 || (week == 53 && !CalendarMath.HasIsoWeek53(r.IsoYear.Value)))
                    throw Raise.ValueError(ctx, "Invalid week: " + week);   // fromisocalendar: 2024 has no week 53
                int jan4 = CalendarMath.Ymd2Ord(r.IsoYear.Value, 1, 4);
                int ordinal = jan4 - CalendarMath.Weekday(jan4) + (r.IsoWeek.Value - 1) * 7 + r.WeekdayMon0.Value;
                CalendarMath.Ord2Ymd(ordinal, out r.Year, out r.Month, out r.Day);
                PastYear9999(ctx, r.Year);
            }
            else if (r.IsoWeek != null)
            {
                if (!r.SawYear || r.WeekdayMon0 == null)
                    throw Raise.ValueError(ctx, "ISO week directive '%V' must be used with the ISO year directive '%G'" + Weekday);
                throw Raise.ValueError(ctx, "ISO week directive '%V' is incompatible with the year directive '%Y'. Use the ISO year '%G' instead.");
            }
        }

        public static void ResolveDate(EvalContext ctx, StrptimeResult r)
        {
            if (r.DayOfYear == null && r.WeekOfYear != null && r.WeekdayMon0 != null)
                r.DayOfYear = CalcJulianFromUW(r.Year, r.WeekOfYear.Value, r.WeekdayMon0.Value, r.WeekStartsMonday);

            int ordinal;
            if (r.DayOfYear == null)
            {
                ordinal = CalendarMath.Ymd2Ord(r.Year, r.Month, r.Day);
            }
            else
            {
                ordinal = CalendarMath.Ymd2Ord(r.Year, 1, 1) + r.DayOfYear.Value - 1;
                if (r.Year < 1 || ordinal < 1)
                    throw Raise.ValueError(ctx, "year 0 is out of range");   // %Y 0000, or %W week 0 of year 1
                CalendarMath.Ord2Ymd(ordinal, out r.Year, out r.Month, out r.Day);
                PastYear9999(ctx, r.Year);
            }
            if (r.WeekdayMon0 == null)
                r.WeekdayMon0 = CalendarMath.Weekday(ordinal);

            if (r.SawHour12 && r.Meridiem != null)
            {
                if (r.Meridiem.Value == 1 && r.Hour != 12)
                    r.Hour += 12;
                else if (r.Meridiem.Value == 0 && r.Hour == 12)
                    r.Hour = 0;
            }
        }

        // A week or day-of-year date past 9999-12-31: CPython's date() refuses it.
        private static void PastYear9999(EvalContext ctx, int year)
        {
            if (year > 9999)
                throw Raise.ValueError(ctx, "year must be in 1..9999, not " + year.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static int CalcJulianFromUW(int year, int week, int dowMon0, bool startsMon)
        {
            int firstWeekday = CalendarMath.Weekday(CalendarMath.Ymd2Ord(year, 1, 1));   // Mon=0
            int dow = dowMon0;
            if (!startsMon)
            {
                firstWeekday = (firstWeekday + 1) % 7;   // shift to Sun=0
                dow = (dow + 1) % 7;
            }
            int week0Length = (7 - firstWeekday) % 7;
            if (week == 0)
                return 1 + dow - firstWeekday;
            return 1 + week0Length + 7 * (week - 1) + dow;
        }

        // ---- helpers ----

        private static ScriptException NoMatch(EvalContext ctx, string data, string fmt)
        {
            return Raise.ValueError(ctx, "time data " + Repr(ctx, data) + " does not match format " + Repr(ctx, fmt));
        }

        private static string Repr(EvalContext ctx, string s) { return ctx.Values.Str(s).Repr(ctx); }

        private static bool IsDigit(char c) { return c >= '0' && c <= '9'; }

        private static int ParseDigits(string s, int start, int count)
        {
            int v = 0;
            for (int i = 0; i < count; i++)
                v = v * 10 + (s[start + i] - '0');
            return v;
        }

        private static bool EqCi(char a, char b)
        {
            return char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
        }
    }
}
