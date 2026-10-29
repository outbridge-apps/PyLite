using System;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // zoneinfo.ZoneInfo over the Windows time zone database through the CLDR map: conversions across
    // DST, the fold=0 rule at the ambiguous hour, aliases, the cache, and the not-found error.
    [Trait("Category", "Compatibility")]
    public class ZoneinfoTests
    {
        private const string Prelude = "from datetime import datetime, time, timedelta, timezone, tzinfo\n"
            + "from zoneinfo import ZoneInfo, available_timezones, ZoneInfoNotFoundError\n"
            + "ams = ZoneInfo('Europe/Amsterdam')\nutc = timezone.utc\n";

        // Frozen: UtcNow = 2026-07-06T10:20:30Z, local +02:00 'CET'.
        private static readonly IScriptClock Clock = new FrozenClock(
            new DateTime(2026, 7, 6, 10, 20, 30, DateTimeKind.Utc), TimeSpan.FromHours(2), "CET");

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr, clock: Clock);
        }

        [Theory]
        [InlineData("repr(ams)", "\"zoneinfo.ZoneInfo(key='Europe/Amsterdam')\"")]
        [InlineData("(str(ams), ams.key)", "('Europe/Amsterdam', 'Europe/Amsterdam')")]
        [InlineData("ZoneInfo('Europe/Amsterdam') is ams", "True")]
        [InlineData("ZoneInfo.no_cache('Europe/Amsterdam') is ams", "False")]
        [InlineData("ZoneInfo.no_cache('Europe/Amsterdam') == ams", "True")]
        [InlineData("isinstance(ams, tzinfo)", "True")]
        [InlineData("len({ams, ZoneInfo('Europe/Amsterdam')})", "1")]
        // UTC -> zone across the year
        [InlineData("datetime(2024, 1, 1, 12, tzinfo=utc).astimezone(ams).isoformat()", "'2024-01-01T13:00:00+01:00'")]
        [InlineData("datetime(2024, 7, 1, 12, tzinfo=utc).astimezone(ams).isoformat()", "'2024-07-01T14:00:00+02:00'")]
        // zone wall time -> UTC
        [InlineData("datetime(2024, 1, 1, 12, tzinfo=ams).astimezone(utc).isoformat()", "'2024-01-01T11:00:00+00:00'")]
        [InlineData("datetime(2024, 7, 1, 12, tzinfo=ams).astimezone(utc).isoformat()", "'2024-07-01T10:00:00+00:00'")]
        [InlineData("datetime(2024, 7, 1, 12, tzinfo=ams).isoformat()", "'2024-07-01T12:00:00+02:00'")]
        [InlineData("(datetime(2024, 7, 1, tzinfo=ams).utcoffset(), datetime(2024, 7, 1, tzinfo=ams).dst(), datetime(2024, 1, 1, tzinfo=ams).dst())", "(datetime.timedelta(seconds=7200), datetime.timedelta(seconds=3600), datetime.timedelta(0))")]
        // the spring transition instant and the autumn ambiguous hour (fold=0 = first occurrence, CEST)
        [InlineData("datetime(2024, 3, 31, 0, 59, 59, tzinfo=utc).astimezone(ams).isoformat()", "'2024-03-31T01:59:59+01:00'")]
        [InlineData("datetime(2024, 3, 31, 1, 0, tzinfo=utc).astimezone(ams).isoformat()", "'2024-03-31T03:00:00+02:00'")]
        [InlineData("datetime(2024, 10, 27, 2, 30, tzinfo=ams).utcoffset()", "datetime.timedelta(seconds=7200)")]
        [InlineData("datetime(2024, 3, 31, 2, 30, tzinfo=ams).utcoffset()", "datetime.timedelta(seconds=3600)")]   // the gap: pre-transition offset
        // other zones, aliases, UTC keys
        [InlineData("[datetime(2024, 7, 1, 12, tzinfo=utc).astimezone(ZoneInfo(k)).isoformat() for k in ['America/New_York', 'Asia/Kolkata', 'Australia/Sydney']]", "['2024-07-01T08:00:00-04:00', '2024-07-01T17:30:00+05:30', '2024-07-01T22:00:00+10:00']")]
        [InlineData("(ZoneInfo('Asia/Kolkata').key, ZoneInfo('Europe/Kyiv').key, ZoneInfo('Asia/Calcutta') == ZoneInfo('Asia/Kolkata'))", "('Asia/Kolkata', 'Europe/Kyiv', False)")]
        [InlineData("(ZoneInfo('UTC').key, datetime(2024, 1, 1, tzinfo=ZoneInfo('Etc/UTC')).utcoffset())", "('UTC', datetime.timedelta(0))")]
        // clock factories, arithmetic, comparisons, time objects
        [InlineData("datetime.now(ams).isoformat()", "'2026-07-06T12:20:30+02:00'")]
        [InlineData("datetime.fromtimestamp(1719835200, ams).isoformat()", "'2024-07-01T14:00:00+02:00'")]
        [InlineData("(datetime(2024, 3, 30, 12).replace(tzinfo=ams) + timedelta(days=1)).isoformat()", "'2024-03-31T12:00:00+02:00'")]
        [InlineData("datetime(2024, 7, 1, 14, tzinfo=ams) == datetime(2024, 7, 1, 12, tzinfo=utc)", "True")]
        [InlineData("datetime(2024, 7, 1, 14, tzinfo=ams).timestamp()", "1719835200.0")]
        [InlineData("time(12, 0, tzinfo=ams).utcoffset()", "None")]
        [InlineData("datetime(2024, 7, 1, 12, tzinfo=ams).strftime('%z')", "'+0200'")]
        [InlineData("datetime(2024, 1, 1, 12).astimezone(ams).isoformat()", "'2024-01-01T11:00:00+01:00'")]   // naive = local +02:00
        [InlineData("'Europe/Amsterdam' in available_timezones() and len(available_timezones()) > 300", "True")]
        [InlineData("issubclass(ZoneInfoNotFoundError, KeyError)", "True")]
        public void Behaviour(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // tzname(): tzdata's abbreviations where it has them, the numeric form elsewhere.
        [Theory]
        [InlineData("(datetime(2024, 1, 1, tzinfo=ams).tzname(), datetime(2024, 7, 1, tzinfo=ams).tzname())", "('CET', 'CEST')")]
        [InlineData("[datetime(2024, 7, 1, tzinfo=ZoneInfo(k)).tzname() for k in ['America/New_York', 'Europe/London', 'Asia/Kolkata', 'Asia/Tokyo', 'Australia/Sydney', 'UTC']]", "['EDT', 'BST', 'IST', 'JST', 'AEST', 'UTC']")]
        [InlineData("[datetime(2024, 1, 1, tzinfo=ZoneInfo(k)).tzname() for k in ['America/New_York', 'Europe/London', 'Australia/Sydney', 'Europe/Moscow']]", "['EST', 'GMT', 'AEDT', 'MSK']")]
        [InlineData("[datetime(2024, 7, 1, tzinfo=ZoneInfo(k)).tzname() for k in ['Asia/Dubai', 'Asia/Kathmandu', 'America/Sao_Paulo', 'Asia/Bangkok']]", "['+04', '+0545', '-03', '+07']")]
        public void TzNames(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // fold (PEP 495): the second occurrence of the ambiguous hour, set by astimezone/fromtimestamp on
        // the way in, honoured by utcoffset/timestamp, ignored by == and hash under the same tzinfo.
        [Theory]
        [InlineData("repr(datetime(2024, 10, 27, 2, 30, tzinfo=ams, fold=1))", "\"datetime.datetime(2024, 10, 27, 2, 30, tzinfo=zoneinfo.ZoneInfo(key='Europe/Amsterdam'), fold=1)\"")]
        [InlineData("(datetime(2024, 1, 1).fold, datetime(2024, 1, 1, fold=1).fold, datetime(2024, 1, 1, fold=1).replace(fold=0).fold)", "(0, 1, 0)")]
        [InlineData("(datetime(2024, 10, 27, 2, 30, tzinfo=ams).utcoffset(), datetime(2024, 10, 27, 2, 30, tzinfo=ams, fold=1).utcoffset())", "(datetime.timedelta(seconds=7200), datetime.timedelta(seconds=3600))")]
        [InlineData("(datetime(2024, 3, 31, 2, 30, tzinfo=ams).utcoffset(), datetime(2024, 3, 31, 2, 30, tzinfo=ams, fold=1).utcoffset())", "(datetime.timedelta(seconds=3600), datetime.timedelta(seconds=7200))")]
        [InlineData("(datetime(2024, 10, 27, 0, 30, tzinfo=utc).astimezone(ams).fold, datetime(2024, 10, 27, 1, 30, tzinfo=utc).astimezone(ams).fold)", "(0, 1)")]
        [InlineData("datetime(2024, 10, 27, 1, 30, tzinfo=utc).astimezone(ams).isoformat()", "'2024-10-27T02:30:00+01:00'")]
        [InlineData("(datetime.fromtimestamp(1729989000, ams).fold, datetime.fromtimestamp(1729992600, ams).fold)", "(0, 1)")]   // 00:30Z / 01:30Z
        [InlineData("datetime(2024, 10, 27, 2, 30, tzinfo=ams) == datetime(2024, 10, 27, 2, 30, tzinfo=ams, fold=1)", "True")]
        [InlineData("len({datetime(2024, 10, 27, 2, 30, tzinfo=ams), datetime(2024, 10, 27, 2, 30, tzinfo=ams, fold=1)})", "1")]
        [InlineData("datetime(2024, 10, 27, 2, 30, tzinfo=ams, fold=1).timestamp() - datetime(2024, 10, 27, 2, 30, tzinfo=ams).timestamp()", "3600.0")]
        [InlineData("datetime(2024, 10, 27, 2, 30, tzinfo=ams, fold=1).astimezone(utc).isoformat()", "'2024-10-27T01:30:00+00:00'")]
        [InlineData("(datetime(2024, 10, 27, 2, 30, tzinfo=ams).tzname(), datetime(2024, 10, 27, 2, 30, tzinfo=ams, fold=1).tzname())", "('CEST', 'CET')")]
        [InlineData("datetime(2024, 1, 1, tzinfo=ams, fold=1) == datetime(2024, 1, 1, tzinfo=ams)", "True")]
        public void Fold(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("ZoneInfo('Mars/Olympus')", "ZoneInfoNotFoundError", "No time zone found with key Mars/Olympus")]
        [InlineData("ZoneInfo(1)", "TypeError", "ZoneInfo keys must be strings, not int")]
        [InlineData("ZoneInfo()", "TypeError", "ZoneInfo() takes exactly one argument (0 given)")]
        [InlineData("ams.utcoffset(1)", "TypeError", "utcoffset(dt) argument must be a datetime instance or None")]
        [InlineData("datetime(2024, 1, 1, fold=2)", "ValueError", "fold must be either 0 or 1")]
        [InlineData("datetime(2024, 1, 1).replace(fold='x')", "TypeError", "'str' object cannot be interpreted as an integer")]
        public void Errors(string expr, string exc, string message)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Prelude + "r = " + expr + "\n", clock: Clock));
            Assert.Equal(exc, ex.Value.ExcType.Name);
            Assert.Equal(message, ex.Value.GetMessageText(Rt.Ctx()));
        }
    }
}
