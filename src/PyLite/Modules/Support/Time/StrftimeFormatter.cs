using System.Globalization;
using System.Text;
using Outbridge.PyLite.Runtime;

namespace Outbridge.PyLite.Modules.Support
{
    // The single date-formatting point in the engine. DateTime.ToString is FORBIDDEN
    // package-wide; one code path over broken-down fields serves date/time/datetime/struct_time. All numbers
    // are InvariantCulture; name tables are hardcoded (DtNames). Unknown codes and a trailing % are literal
    // (glibc parity). Years < 1000 format normally (%Y zero-pads to 4).
    internal struct BrokenTime
    {
        public int Year;            // 1..9999
        public int Month;           // 1..12
        public int Day;             // 1..31
        public int Hour, Minute, Second;
        public int Microsecond;     // 0..999999
        public int WeekdayMon0;     // Mon=0..Sun=6
        public int DayOfYear;       // 1..366
        public long? OffsetUs;      // null => %z/%Z produce ""
        public string TzName;       // null => %Z produces ""
        public bool HasMicroseconds;// false (time module) => %f is emitted literally as "%f"
    }

    internal static class StrftimeFormatter
    {
        public static string Format(EvalContext ctx, string fmt, BrokenTime t)
        {
            var sb = new StringBuilder(fmt.Length + 16);
            int i = 0;
            int sinceStep = 0;
            while (i < fmt.Length)
            {
                if (++sinceStep >= 64)
                {
                    sinceStep = 0;
                    ctx.Budget.Step();
                }
                char c = fmt[i];
                if (c != '%')
                {
                    sb.Append(c);
                    i += 1;
                    continue;
                }
                if (i == fmt.Length - 1)
                {
                    sb.Append('%');
                    break;
                }
                char code = fmt[i + 1];
                i += 2;
                // glibc flags: - (no padding), _ (space padding), 0 (zero padding), ^ (upper), # (swap case)
                char flag = '\0';
                if ((code == '-' || code == '_' || code == '0' || code == '^' || code == '#') && i < fmt.Length)
                {
                    flag = code;
                    code = fmt[i];
                    i += 1;
                }
                if (flag == '\0')
                {
                    Emit(ctx, sb, code, t);
                    continue;
                }
                var piece = new StringBuilder(8);
                Emit(ctx, piece, code, t);
                if (piece.Length >= 2 && piece[0] == '%')
                    sb.Append('%').Append(flag).Append(piece.ToString(1, piece.Length - 1));   // unknown code: literal
                else
                    ApplyFlag(sb, piece.ToString(), flag);
            }
            ctx.Values.EnsureStrLen(sb.Length);
            return sb.ToString();
        }

        private static void ApplyFlag(StringBuilder sb, string s, char flag)
        {
            int k = 0;
            switch (flag)
            {
                case '-':
                    while (k < s.Length - 1 && (s[k] == '0' || s[k] == ' '))
                        k++;
                    sb.Append(s, k, s.Length - k);
                    break;
                case '_':
                    while (k < s.Length - 1 && s[k] == '0')
                    {
                        sb.Append(' ');
                        k++;
                    }
                    sb.Append(s, k, s.Length - k);
                    break;
                case '0':
                    while (k < s.Length - 1 && s[k] == ' ')
                    {
                        sb.Append('0');
                        k++;
                    }
                    sb.Append(s, k, s.Length - k);
                    break;
                case '^':
                    sb.Append(s.ToUpperInvariant());
                    break;
                default:   // '#': a name swaps case (glibc: %#B -> JANUARY, %#p -> am)
                    sb.Append(s == s.ToUpperInvariant() ? s.ToLowerInvariant() : s.ToUpperInvariant());
                    break;
            }
        }

        private static void Emit(EvalContext ctx, StringBuilder sb, char code, BrokenTime t)
        {
            switch (code)
            {
                case 'Y': sb.Append(t.Year.ToString("0000", CultureInfo.InvariantCulture)); break;
                case 'y': sb.Append((t.Year % 100).ToString("00", CultureInfo.InvariantCulture)); break;
                case 'm': sb.Append(t.Month.ToString("00", CultureInfo.InvariantCulture)); break;
                case 'd': sb.Append(t.Day.ToString("00", CultureInfo.InvariantCulture)); break;
                case 'H': sb.Append(t.Hour.ToString("00", CultureInfo.InvariantCulture)); break;
                case 'I':
                {
                    int h12 = t.Hour % 12;
                    if (h12 == 0)
                        h12 = 12;
                    sb.Append(h12.ToString("00", CultureInfo.InvariantCulture));
                    break;
                }
                case 'M': sb.Append(t.Minute.ToString("00", CultureInfo.InvariantCulture)); break;
                case 'S': sb.Append(t.Second.ToString("00", CultureInfo.InvariantCulture)); break;
                case 'f':
                    if (t.HasMicroseconds)
                        sb.Append(t.Microsecond.ToString("000000", CultureInfo.InvariantCulture));
                    else
                        sb.Append("%f");
                    break;
                case 'p': sb.Append(t.Hour < 12 ? "AM" : "PM"); break;
                case 'z': AppendOffset(sb, t.OffsetUs); break;
                case 'Z': sb.Append(t.TzName ?? ""); break;
                case 'a': sb.Append(DtNames.AbbrDays[t.WeekdayMon0]); break;
                case 'A': sb.Append(DtNames.FullDays[t.WeekdayMon0]); break;
                case 'b': sb.Append(DtNames.AbbrMonths[t.Month - 1]); break;
                case 'B': sb.Append(DtNames.FullMonths[t.Month - 1]); break;
                case 'j': sb.Append(t.DayOfYear.ToString("000", CultureInfo.InvariantCulture)); break;
                case 'w': sb.Append(((t.WeekdayMon0 + 1) % 7).ToString(CultureInfo.InvariantCulture)); break;
                case 'U': sb.Append(WeekOfYear(t, false).ToString("00", CultureInfo.InvariantCulture)); break;
                case 'W': sb.Append(WeekOfYear(t, true).ToString("00", CultureInfo.InvariantCulture)); break;
                case 'c': AppendC(sb, t); break;
                case 'x':
                    sb.Append(t.Month.ToString("00", CultureInfo.InvariantCulture)).Append('/')
                      .Append(t.Day.ToString("00", CultureInfo.InvariantCulture)).Append('/')
                      .Append((t.Year % 100).ToString("00", CultureInfo.InvariantCulture));
                    break;
                case 'X': AppendHms(sb, t); break;
                // glibc's extra codes (CPython on Linux/macOS): space-padded numbers, lowercase am/pm, the
                // century, ISO 8601 week fields, epoch seconds, and the composite date/time forms.
                case 'e': sb.Append(t.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2)); break;
                case 'k': sb.Append(t.Hour.ToString(CultureInfo.InvariantCulture).PadLeft(2)); break;
                case 'l':
                {
                    int h12 = t.Hour % 12;
                    sb.Append((h12 == 0 ? 12 : h12).ToString(CultureInfo.InvariantCulture).PadLeft(2));
                    break;
                }
                case 'P': sb.Append(t.Hour < 12 ? "am" : "pm"); break;
                case 'C': sb.Append((t.Year / 100).ToString("00", CultureInfo.InvariantCulture)); break;
                case 'h': sb.Append(DtNames.AbbrMonths[t.Month - 1]); break;
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'G':
                case 'V':
                case 'u':
                {
                    int iy, iw, id;
                    CalendarMath.IsoCalendar(t.Year, t.Month, t.Day, out iy, out iw, out id);
                    if (code == 'G')
                        sb.Append(iy.ToString("0000", CultureInfo.InvariantCulture));
                    else if (code == 'V')
                        sb.Append(iw.ToString("00", CultureInfo.InvariantCulture));
                    else
                        sb.Append(id.ToString(CultureInfo.InvariantCulture));
                    break;
                }
                case 's':
                {
                    // mktime semantics: the wall-clock fields read as local time, whatever the tzinfo (glibc)
                    long secs = (long)(CalendarMath.Ymd2Ord(t.Year, t.Month, t.Day) - CalendarMath.EpochOrdinal) * 86400L
                        + t.Hour * 3600L + t.Minute * 60L + t.Second - (long)ctx.Clock.LocalOffset.TotalSeconds;
                    sb.Append(secs.ToString(CultureInfo.InvariantCulture));
                    break;
                }
                case 'D': Emit(ctx, sb, 'm', t); sb.Append('/'); Emit(ctx, sb, 'd', t); sb.Append('/'); Emit(ctx, sb, 'y', t); break;
                case 'F': Emit(ctx, sb, 'Y', t); sb.Append('-'); Emit(ctx, sb, 'm', t); sb.Append('-'); Emit(ctx, sb, 'd', t); break;
                case 'T': AppendHms(sb, t); break;
                case 'R': Emit(ctx, sb, 'H', t); sb.Append(':'); Emit(ctx, sb, 'M', t); break;
                case 'r': Emit(ctx, sb, 'I', t); sb.Append(':'); Emit(ctx, sb, 'M', t); sb.Append(':'); Emit(ctx, sb, 'S', t); sb.Append(' '); Emit(ctx, sb, 'p', t); break;
                case '%': sb.Append('%'); break;
                default:
                    sb.Append('%');
                    sb.Append(code);
                    break;
            }
        }

        private static void AppendOffset(StringBuilder sb, long? offsetUs)
        {
            if (offsetUs == null)
                return;
            sb.Append(Outbridge.PyLite.Runtime.Values.TimezoneValue.FormatOffset(offsetUs.Value, ""));
        }

        private static void AppendHms(StringBuilder sb, BrokenTime t)
        {
            sb.Append(t.Hour.ToString("00", CultureInfo.InvariantCulture)).Append(':')
              .Append(t.Minute.ToString("00", CultureInfo.InvariantCulture)).Append(':')
              .Append(t.Second.ToString("00", CultureInfo.InvariantCulture));
        }

        // "%a %b %e %H:%M:%S %Y" — day space-padded to width 2, year with the minimum digits.
        private static void AppendC(StringBuilder sb, BrokenTime t)
        {
            sb.Append(DtNames.AbbrDays[t.WeekdayMon0]).Append(' ')
              .Append(DtNames.AbbrMonths[t.Month - 1]).Append(' ')
              .Append(t.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2, ' ')).Append(' ');
            AppendHms(sb, t);
            sb.Append(' ').Append(t.Year.ToString(CultureInfo.InvariantCulture));
        }

        // glibc %U/%W: week 01 starts at the first Sunday (%U) / Monday (%W); earlier days are week 00.
        private static int WeekOfYear(BrokenTime t, bool mondayStart)
        {
            int yday0 = t.DayOfYear - 1;
            int wMon0 = t.WeekdayMon0;
            if (mondayStart)
                return (yday0 + 7 - wMon0) / 7;
            int wSun0 = (wMon0 + 1) % 7;
            return (yday0 + 7 - wSun0) / 7;
        }
    }
}
