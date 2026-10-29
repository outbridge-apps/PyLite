using System;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/09 T-MDT-08/09: datetime.datetime (constructors, factories, formatting, arithmetic, timestamp).
    [Trait("Category", "Compatibility")]
    public class DateTimeValueTests
    {
        private const string Prelude = "from datetime import datetime, date, time, timezone, timedelta\n";

        // Frozen: UtcNow = 2026-07-06T10:20:30.123456Z, LocalOffset = +02:00, 'CET'.
        private static readonly IScriptClock Clock = new FrozenClock(
            new DateTime(2026, 7, 6, 10, 20, 30, DateTimeKind.Utc).AddTicks(1234560),
            TimeSpan.FromHours(2), "CET");

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr, clock: Clock);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(Prelude + body + "\n", clock: Clock));
        }

        [Theory]
        // ---- clock factories (T-MDT-08) ----
        [InlineData("repr(datetime.utcnow())", "'datetime.datetime(2026, 7, 6, 10, 20, 30, 123456)'")]
        [InlineData("repr(datetime.now())", "'datetime.datetime(2026, 7, 6, 12, 20, 30, 123456)'")]
        [InlineData("repr(datetime.now(timezone.utc))", "'datetime.datetime(2026, 7, 6, 10, 20, 30, 123456, tzinfo=datetime.timezone.utc)'")]
        [InlineData("repr(datetime.fromtimestamp(0))", "'datetime.datetime(1970, 1, 1, 2, 0)'")]
        // ---- astimezone: the same instant under another zone; naive = local (+02:00 'CET' here) ----
        [InlineData("datetime(2024, 1, 1, 12, tzinfo=timezone.utc).astimezone(timezone(timedelta(hours=3))).isoformat()", "'2024-01-01T15:00:00+03:00'")]
        [InlineData("datetime(2024, 1, 1, 12).astimezone().isoformat()", "'2024-01-01T12:00:00+02:00'")]
        [InlineData("datetime(2024, 1, 1, 12).astimezone().tzname()", "'CET'")]
        [InlineData("datetime(2024, 1, 1, 12, tzinfo=timezone.utc).astimezone().isoformat()", "'2024-01-01T14:00:00+02:00'")]
        [InlineData("datetime(2024, 1, 1, 12).astimezone(timezone.utc).isoformat()", "'2024-01-01T10:00:00+00:00'")]
        [InlineData("datetime(2024, 1, 1, 12).astimezone(tz=timezone.utc).isoformat()", "'2024-01-01T10:00:00+00:00'")]
        [InlineData("datetime(2024, 1, 1, 12, tzinfo=timezone.utc).astimezone(None).isoformat()", "'2024-01-01T14:00:00+02:00'")]
        [InlineData("(lambda d: d.astimezone(d.tzinfo) is d)(datetime(2024, 1, 1, tzinfo=timezone.utc))", "True")]
        [InlineData("datetime(2024, 1, 1, 12, 30, 15, 250, tzinfo=timezone(timedelta(hours=-5, minutes=-30))).astimezone(timezone.utc).isoformat()", "'2024-01-01T18:00:15.000250+00:00'")]
        [InlineData("datetime(2024, 3, 31, 1, tzinfo=timezone.utc).astimezone(timezone(timedelta(hours=-2))).day", "30")]
        [InlineData("datetime(2024, 1, 1, tzinfo=timezone.utc).timestamp() == datetime(2024, 1, 1, tzinfo=timezone.utc).astimezone(timezone(timedelta(hours=9))).timestamp()", "True")]
        [InlineData("datetime(1970, 1, 1, 2).strftime('%s')", "'0'")]
        [InlineData("repr(datetime.utcfromtimestamp(0))", "'datetime.datetime(1970, 1, 1, 0, 0)'")]
        [InlineData("repr(datetime.utcfromtimestamp(-0.5))", "'datetime.datetime(1969, 12, 31, 23, 59, 59, 500000)'")]
        [InlineData("repr(datetime.fromtimestamp(0, timezone.utc))", "'datetime.datetime(1970, 1, 1, 0, 0, tzinfo=datetime.timezone.utc)'")]
        [InlineData("repr(datetime.combine(date(2011,1,1), time(2,3,4,5)))", "'datetime.datetime(2011, 1, 1, 2, 3, 4, 5)'")]
        [InlineData("datetime.combine(date(2011,1,1), time(2, tzinfo=timezone.utc)).tzname()", "'UTC'")]
        // ---- formatting ----
        [InlineData("datetime(2011,1,1).isoformat()", "'2011-01-01T00:00:00'")]
        [InlineData("str(datetime(2011,1,1,1,2,3,400000))", "'2011-01-01 01:02:03.400000'")]
        [InlineData("datetime(2011,1,1,1,2,3,tzinfo=timezone(timedelta(hours=-4,minutes=-30))).isoformat()", "'2011-01-01T01:02:03-04:30'")]
        [InlineData("repr(datetime(2011,1,1))", "'datetime.datetime(2011, 1, 1, 0, 0)'")]
        [InlineData("repr(datetime(2011,1,1,0,0,1))", "'datetime.datetime(2011, 1, 1, 0, 0, 1)'")]
        [InlineData("datetime(2002,12,4,20,30,40).ctime()", "'Wed Dec  4 20:30:40 2002'")]
        [InlineData("datetime(2011,1,1,12,0,tzinfo=timezone.utc).utctimetuple() == (2011,1,1,12,0,0,5,1,0)", "True")]
        [InlineData("datetime(2011,1,1).timetuple().tm_isdst", "-1")]
        [InlineData("datetime(2011,1,1,2,3,4,5).date()", "datetime.date(2011, 1, 1)")]
        [InlineData("repr(datetime(2011,1,1,2,3).time())", "'datetime.time(2, 3)'")]
        // ---- arithmetic (T-MDT-09) ----
        [InlineData("repr(datetime(2011,1,1) + timedelta(days=1, microseconds=1))", "'datetime.datetime(2011, 1, 2, 0, 0, 0, 1)'")]
        [InlineData("repr(datetime(2011,1,1) - timedelta(microseconds=1))", "'datetime.datetime(2010, 12, 31, 23, 59, 59, 999999)'")]
        [InlineData("repr(datetime(2011,1,2) - datetime(2011,1,1,23))", "'datetime.timedelta(seconds=3600)'")]
        [InlineData("repr(datetime(2011,1,1,14,tzinfo=timezone(timedelta(hours=2))) - datetime(2011,1,1,12,tzinfo=timezone.utc))", "'datetime.timedelta(0)'")]
        [InlineData("datetime(2011,1,1,14,tzinfo=timezone(timedelta(hours=2))) == datetime(2011,1,1,12,tzinfo=timezone.utc)", "True")]
        [InlineData("datetime(2011,1,1) == datetime(2011,1,1,tzinfo=timezone.utc)", "False")]
        [InlineData("(datetime(2011,1,1,tzinfo=timezone(timedelta(hours=2))) + timedelta(hours=1)).hour", "1")]
        [InlineData("datetime(1970,1,1,tzinfo=timezone.utc).timestamp()", "0.0")]
        [InlineData("datetime(1970,1,1,2,0).timestamp()", "0.0")]
        [InlineData("datetime(1969,12,31,23,59,59,500000,tzinfo=timezone.utc).timestamp()", "-0.5")]
        [InlineData("hash(datetime(2011,1,1,14,tzinfo=timezone(timedelta(hours=2)))) == hash(datetime(2011,1,1,12,tzinfo=timezone.utc))", "True")]
        // ---- cross-type comparisons (deferred from T-MDT-05) ----
        [InlineData("date(2011,1,1) == datetime(2011,1,1)", "False")]
        public void DateTimeBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("x = datetime(2011,1,1,25)", "ValueError")]
        [InlineData("x = datetime(2011,2,29,0,0)", "ValueError")]
        [InlineData("x = datetime.max + timedelta(microseconds=1)", "OverflowError")]
        [InlineData("x = datetime(2024, 1, 1).astimezone(1)", "TypeError")]
        [InlineData("x = datetime(2024, 1, 1).astimezone(timezone.utc, 1)", "TypeError")]
        [InlineData("x = datetime.min.astimezone(timezone.utc)", "OverflowError")]
        [InlineData("x = datetime(2011,1,1) - datetime(2011,1,1,tzinfo=timezone.utc)", "TypeError")]
        [InlineData("x = datetime(2011,1,1) < datetime(2011,1,1,tzinfo=timezone.utc)", "TypeError")]
        [InlineData("x = datetime(2011,1,1) - date(2011,1,1)", "TypeError")]
        [InlineData("x = date(2011,1,1) < datetime(2011,1,2)", "TypeError")]
        public void DateTimeErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void SubtractMixedMessage()
        {
            string src = Prelude + "msg = ''\ntry:\n    datetime(2011,1,1) - datetime(2011,1,1,tzinfo=timezone.utc)\nexcept TypeError as e:\n    msg = str(e)\n";
            Assert.Equal("\"can't subtract offset-naive and offset-aware datetimes\"", Ev.Run(src, clock: Clock).Repr("msg"));
        }

        [Fact]
        public void CompareDateTimeToDateMessage()
        {
            string src = Prelude + "msg = ''\ntry:\n    date(2011,1,1) < datetime(2011,1,2)\nexcept TypeError as e:\n    msg = str(e)\n";
            Assert.Equal("\"can't compare datetime.datetime to datetime.date\"", Ev.Run(src, clock: Clock).Repr("msg"));
        }

        [Fact]
        public void AwareTimestampIndependentOfLocalOffset()
        {
            // aware timestamp() must not depend on the clock's LocalOffset.
            var otherClock = new FrozenClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(-5), "EST");
            string src = Prelude + "r = datetime(1970,1,1,tzinfo=timezone.utc).timestamp()\n";
            Assert.Equal("0.0", Ev.Run(src, clock: otherClock).Repr("r"));
        }
    }
}
