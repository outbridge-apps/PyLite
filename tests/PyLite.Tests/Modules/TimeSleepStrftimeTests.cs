using System;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/09 T-MDT-16/17/18: time.sleep, time.strftime/strptime, module assembly + integration.
    [Trait("Category", "Compatibility")]
    public class TimeSleepStrftimeTests
    {
        // LocalOffset +02:00, 'CET', UtcNow = epoch 1000.
        private static readonly IScriptClock Cet = new FrozenClock(
            new DateTime(1970, 1, 1, 0, 16, 40, DateTimeKind.Utc), TimeSpan.FromHours(2), "CET");

        private static string R(string expr)
        {
            return Ev.ReprOf("import time\n", expr, clock: Cet);
        }

        // ---- sleep ----

        [Fact]
        public void SleepNegativeIsValueError()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("import time\ntime.sleep(-1)\n"));
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void SleepNaNIsValueError()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("import time\ntime.sleep(float('nan'))\n"));
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void SleepZeroReturnsNone()
        {
            Assert.Equal("None", Ev.Run("import time\nr = time.sleep(0)\n").Repr("r"));
        }

        [Fact]
        public void NoOpPolicySleepReturnsInstantly()
        {
            var res = Ev.Run("import time\nr = time.sleep(10)\n", sleepPolicy: new NoOpSleepPolicy());
            Assert.Equal("None", res.Repr("r"));
        }

        // SleepTotalMs is the run's sleep budget: a sleep past it fails at once, without waiting it out.
        [Fact]
        public void SleepPastTotalFailsBeforeWaiting()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run("import time\ntime.sleep(0.05)\ntime.sleep(3)\n",
                ResourceLimits.Default.With(sleepTotalMs: 1000)));
            Assert.Equal("SleepTotalMs", ex.LimitName);
            Assert.InRange(sw.ElapsedMilliseconds, 50, 1500);
        }

        [Fact]
        public void DefaultPolicyLeavesTheTotalToTheLimit()
        {
            var p = new DefaultSleepPolicy();
            Assert.Equal(TimeSpan.FromSeconds(10), p.Clamp(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)));
        }

        [Fact]
        public void HugeSleepDoesNotOverflow()
        {
            // 2**63 seconds must not throw OverflowException from TimeSpan.FromSeconds.
            var res = Ev.Run("import time\nr = time.sleep(2**63)\n", sleepPolicy: new NoOpSleepPolicy());
            Assert.Equal("None", res.Repr("r"));
        }

        // ---- strftime / strptime ----

        private const string T0 = "time.gmtime(0)";

        [Theory]
        [InlineData("time.strftime('%Y-%m-%d', " + T0 + ")", "'1970-01-01'")]
        [InlineData("time.strftime('%H:%M:%S', " + T0 + ")", "'00:00:00'")]
        [InlineData("time.strftime('%a %A', " + T0 + ")", "'Thu Thursday'")]
        [InlineData("time.strftime('%j', " + T0 + ")", "'001'")]
        [InlineData("time.strftime('%w', " + T0 + ")", "'4'")]
        [InlineData("time.strftime('%f', " + T0 + ")", "'%f'")]           // time has no microseconds
        [InlineData("time.strftime('%Z', " + T0 + ")", "''")]             // v1: empty
        [InlineData("time.strftime('%c', " + T0 + ")", "'Thu Jan  1 00:00:00 1970'")]
        [InlineData("time.strftime('%Y', (5,1,1,0,0,0,0,1,0))", "'0005'")] // no year gate
        [InlineData("time.strptime('Thu Jan 1 00:00:00 1970') == (1970,1,1,0,0,0,3,1,-1)", "True")]
        [InlineData("time.strptime('2004-11-30', '%Y-%m-%d').tm_year", "2004")]
        public void StrftimeStrptimeBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void StrftimeBadMonthIsValueError()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("import time\ntime.strftime('%Y', (1970,13,1,0,0,0,0,1,0))\n", clock: Cet));
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
        }

        // ---- assembly / integration (T-MDT-18) ----

        [Fact]
        public void RemovedNameIsAttributeError()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("import time\nx = time.clock\n"));
            Assert.Equal("AttributeError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void FromTimeImportSleep()
        {
            Assert.Equal("True", Ev.Run("from time import sleep\nr = callable(sleep)\n").Repr("r"));
        }

        [Fact]
        public void CrossModuleClockConsistency()
        {
            // datetime.utcfromtimestamp(time.time()) with UtcNow=epoch 1000 -> 1970-01-01 00:16:40.
            string src = "import time, datetime\nr = repr(datetime.datetime.utcfromtimestamp(time.time()))\n";
            Assert.Equal("'datetime.datetime(1970, 1, 1, 0, 16, 40)'", Ev.Run(src, clock: Cet).Repr("r"));
        }

        [Fact]
        public void DateStrftimeIntegration()
        {
            Assert.Equal("'Wednesday'", Ev.Run("import datetime\nr = datetime.date(2020,1,1).strftime('%A')\n").Repr("r"));
        }

        [Fact]
        public void PerRunTimezoneIsolation()
        {
            var east = new FrozenClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(2), "A");
            var west = new FrozenClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(-2), "B");
            Assert.Equal("-7200", Ev.Run("import time\nr = time.timezone\n", clock: east).Repr("r"));
            Assert.Equal("7200", Ev.Run("import time\nr = time.timezone\n", clock: west).Repr("r"));
        }
    }
}
