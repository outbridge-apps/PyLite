using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // datetime/date/time.fromisoformat: the 3.11+ grammar (basic and extended forms, ISO weeks, any
    // separator, comma fractions, Z and offsets), the hour-24 rule, and CPython's error wording.
    [Trait("Category", "Compatibility")]
    public class FromIsoformatTests
    {
        private const string Imp = "from datetime import datetime as dt, date, time, timedelta, timezone\n";

        [Theory]
        [InlineData("dt.fromisoformat('2011-11-04')", "datetime.datetime(2011, 11, 4, 0, 0)")]
        [InlineData("dt.fromisoformat('20111104')", "datetime.datetime(2011, 11, 4, 0, 0)")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23')", "datetime.datetime(2011, 11, 4, 0, 5, 23)")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23Z')", "datetime.datetime(2011, 11, 4, 0, 5, 23, tzinfo=datetime.timezone.utc)")]
        [InlineData("dt.fromisoformat('20111104T000523')", "datetime.datetime(2011, 11, 4, 0, 5, 23)")]
        [InlineData("dt.fromisoformat('2011-W01-2T00:05:23.283')", "datetime.datetime(2011, 1, 4, 0, 5, 23, 283000)")]
        [InlineData("dt.fromisoformat('2011-11-04 00:05:23.283')", "datetime.datetime(2011, 11, 4, 0, 5, 23, 283000)")]
        [InlineData("dt.fromisoformat('2011-11-04 00:05:23.283+00:00')", "datetime.datetime(2011, 11, 4, 0, 5, 23, 283000, tzinfo=datetime.timezone.utc)")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23,283')", "datetime.datetime(2011, 11, 4, 0, 5, 23, 283000)")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23.2')", "datetime.datetime(2011, 11, 4, 0, 5, 23, 200000)")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23.1234567')", "datetime.datetime(2011, 11, 4, 0, 5, 23, 123456)")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05')", "datetime.datetime(2011, 11, 4, 0, 5)")]
        [InlineData("dt.fromisoformat('2011-11-04T00')", "datetime.datetime(2011, 11, 4, 0, 0)")]
        [InlineData("dt.fromisoformat('2011-11-04T24:00:00')", "datetime.datetime(2011, 11, 5, 0, 0)")]
        [InlineData("dt.fromisoformat('2011-12-31T24:00')", "datetime.datetime(2012, 1, 1, 0, 0)")]
        [InlineData("dt.fromisoformat('2011-W01')", "datetime.datetime(2011, 1, 3, 0, 0)")]
        [InlineData("dt.fromisoformat('2011W01')", "datetime.datetime(2011, 1, 3, 0, 0)")]
        [InlineData("dt.fromisoformat('2011W012')", "datetime.datetime(2011, 1, 4, 0, 0)")]
        [InlineData("dt.fromisoformat('2020-W53-7')", "datetime.datetime(2021, 1, 3, 0, 0)")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23+04:00').utcoffset()", "datetime.timedelta(seconds=14400)")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23-05:30').utcoffset()", "datetime.timedelta(days=-1, seconds=66600)")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23+04').utcoffset()", "datetime.timedelta(seconds=14400)")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23+0430').utcoffset()", "datetime.timedelta(seconds=16200)")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23+01:02:03.000004').utcoffset()", "datetime.timedelta(seconds=3723, microseconds=4)")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23-00:00').tzinfo", "datetime.timezone.utc")]
        [InlineData("dt.fromisoformat(dt(2024, 2, 29, 13, 45, 30, 123456, tzinfo=timezone(timedelta(hours=3))).isoformat()) == dt(2024, 2, 29, 13, 45, 30, 123456, tzinfo=timezone(timedelta(hours=3)))", "True")]
        [InlineData("date.fromisoformat('2019-12-04')", "datetime.date(2019, 12, 4)")]
        [InlineData("date.fromisoformat('20191204')", "datetime.date(2019, 12, 4)")]
        [InlineData("date.fromisoformat('2021-W01-1')", "datetime.date(2021, 1, 4)")]
        [InlineData("date.fromisoformat('2021W011')", "datetime.date(2021, 1, 4)")]
        [InlineData("date.fromisoformat(date(2024, 2, 29).isoformat())", "datetime.date(2024, 2, 29)")]
        [InlineData("time.fromisoformat('04:23:01')", "datetime.time(4, 23, 1)")]
        [InlineData("time.fromisoformat('T04:23:01')", "datetime.time(4, 23, 1)")]
        [InlineData("time.fromisoformat('04:23:01.000384')", "datetime.time(4, 23, 1, 384)")]
        [InlineData("time.fromisoformat('04:23:01,000384')", "datetime.time(4, 23, 1, 384)")]
        [InlineData("time.fromisoformat('04:23:01Z')", "datetime.time(4, 23, 1, tzinfo=datetime.timezone.utc)")]
        [InlineData("time.fromisoformat('04:23:01+00:00')", "datetime.time(4, 23, 1, tzinfo=datetime.timezone.utc)")]
        [InlineData("time.fromisoformat('04:23:01+04:00').utcoffset()", "datetime.timedelta(seconds=14400)")]
        [InlineData("time.fromisoformat('0423')", "datetime.time(4, 23)")]
        [InlineData("time.fromisoformat('24:00')", "datetime.time(0, 0)")]
        [InlineData("time.fromisoformat(time(13, 45, 30, 5).isoformat())", "datetime.time(13, 45, 30, 5)")]
        public void Parses(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run(Imp + "r = (" + expr + ")\n").Repr("r"));
        }

        [Theory]
        [InlineData("dt.fromisoformat('abc')", "ValueError", "Invalid isoformat string: 'abc'")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23+4')", "ValueError", "Invalid isoformat string: '2011-11-04T00:05:23+4'")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23.')", "ValueError", "Invalid isoformat string: '2011-11-04T00:05:23.'")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23.12ab')", "ValueError", "Invalid isoformat string: '2011-11-04T00:05:23.12ab'")]
        [InlineData("dt.fromisoformat('2011-11-04T00:0523')", "ValueError", "Invalid isoformat string: '2011-11-04T00:0523'")]
        [InlineData("dt.fromisoformat('2011-1104')", "ValueError", "Invalid isoformat string: '2011-1104'")]
        [InlineData("dt.fromisoformat('2011-W54-1')", "ValueError", "Invalid isoformat string: '2011-W54-1'")]
        [InlineData("dt.fromisoformat('2011-W53-1')", "ValueError", "Invalid isoformat string: '2011-W53-1'")]
        [InlineData("dt.fromisoformat('2011-W01-8')", "ValueError", "Invalid isoformat string: '2011-W01-8'")]
        [InlineData("dt.fromisoformat('2011-13-04')", "ValueError", "month must be in 1..12")]
        [InlineData("dt.fromisoformat('2011-11-04T25:00')", "ValueError", "hour must be in 0..23")]
        [InlineData("dt.fromisoformat('2011-11-04T24:01')", "ValueError", "minute, second, and microsecond must be 0 when hour is 24")]
        [InlineData("dt.fromisoformat('2011-11-04T00:05:23+24:00')", "ValueError", "offset must be a timedelta strictly between -timedelta(hours=24) and timedelta(hours=24).")]
        [InlineData("dt.fromisoformat(1)", "TypeError", "fromisoformat: argument must be str")]
        [InlineData("dt.fromisoformat()", "TypeError", "fromisoformat() takes exactly one argument (0 given)")]
        [InlineData("date.fromisoformat('2019-12-4')", "ValueError", "Invalid isoformat string: '2019-12-4'")]
        [InlineData("date.fromisoformat('2019-12-04T00:00')", "ValueError", "Invalid isoformat string: '2019-12-04T00:00'")]
        [InlineData("date.fromisoformat('2019-02-30')", "ValueError", "day is out of range for month")]
        [InlineData("time.fromisoformat('')", "ValueError", "Invalid isoformat string: ''")]
        [InlineData("time.fromisoformat('4:23')", "ValueError", "Invalid isoformat string: '4:23'")]
        [InlineData("time.fromisoformat('24:01')", "ValueError", "minute, second, and microsecond must be 0 when hour is 24")]
        [InlineData("time.fromisoformat('25:00')", "ValueError", "hour must be in 0..23")]
        public void Errors(string expr, string exc, string message)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + "r = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
            Assert.Equal(message, ex.Value.GetMessageText(Rt.Ctx()));
        }
    }
}
