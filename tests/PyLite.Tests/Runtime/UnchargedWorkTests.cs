using System;
using System.Numerics;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // Review 3, P1: one operation that did seconds of work or held megabytes on a few steps and no charge,
    // so a loop of it outlived its deadline (a leaked runner) or the memory limit never saw it.
    [Trait("Category", "Sandbox")]
    public class UnchargedWorkTests
    {
        private static readonly ScriptEngine Engine = TestEngines.WithCeiling(TestEngines.Unhurried);

        private static RunResult Run(string src, ResourceLimits limits)
        {
            return Engine.Run(src, new RunRequest { Limits = limits, ScalarOutputNames = new[] { "r" } });
        }

        private static ResourceLimits Steps(long steps)
        {
            return TestEngines.Unhurried.With(maxSteps: steps);   // the steps decide, never the clock
        }

        [Fact]
        public void AFailingEnumCallInALoopMeetsTheDeadline()
        {
            // 1 GB, so only the deadline can end it: at 256 MB a fast box hit the memory limit first
            ResourceLimits limits = TestEngines.Unhurried.With(deadlineMs: 1000, maxAllocBytes: 1L << 30);
            RunResult r = TestEngines.WithCeiling(limits).Run(
                "import enum\nwhile True:\n    try:\n        enum.Enum('E', 'a ' * 2000000)\n    except TypeError:\n        pass\n",
                new RunRequest());
            Assert.Equal("DeadlineMs", r.Error.LimitName);   // was a leaked runner: 1.8 s per call on 28 steps
        }

        [Fact]
        public void EnumFunctionalNamesStillSplitAsBefore()
        {
            RunResult r = Run("import enum\nE = enum.Enum('E', ' a,b\\tc\\n d ')\nr = [m.name for m in E]\n", ResourceLimits.Default);
            Assert.Equal("['a', 'b', 'c', 'd']", r.ScalarsOut["r"]);
        }

        [Theory]
        [InlineData("for k in d:\n        pass")]
        [InlineData("1 in d.values()")]
        [InlineData("d == {0: 0, 50001: 0}")]
        [InlineData("str(d)")]
        public void WalksPastTombstonesAreCharged(string walk)
        {
            // 50K deleted entries between the two live ones: each walk passes them all (13 steps' worth)
            string src = "d = dict.fromkeys(range(50002), 0)\nfor i in range(1, 50001):\n    del d[i]\n"
                + "for _ in range(20000):\n    " + walk + "\n";
            Assert.True(Run(src.Replace("range(20000)", "range(10)"), Steps(200_000)).Succeeded);
            RunResult r = Run(src, Steps(200_000));
            Assert.Equal("MaxSteps", r.Error.LimitName);
        }

        [Fact]
        public void ADictEmptiedFromTheFrontIsWalkedFromItsFirstLiveEntry()
        {
            RunResult r = Run("d = dict.fromkeys(range(200000), 0)\nfor i in range(199999):\n    del d[i]\n"
                + "n = 0\nfor _ in range(200000):\n    for k in d:\n        n += k\nr = (n, list(d), list(reversed(d)))\n", TestEngines.Unhurried);
            Assert.Equal("(39999800000, [199999], [199999])", r.ScalarsOut["r"]);
        }

        [Fact]
        public void ASetEmptiedFromTheFrontIsWalkedFromItsFirstLiveEntry()
        {
            RunResult r = Run("s = set(range(100000))\nfor i in range(99999):\n    s.discard(i)\nr = sorted(s)\n", ResourceLimits.Default);
            Assert.Equal("[99999]", r.ScalarsOut["r"]);
        }

        [Fact]
        public void HashingAHugeIntIsCharged()
        {
            const string loop = "x = 3 ** 600000\nd = {}\nfor _ in range(1000):\n    ";
            RunResult hashed = Run(loop + "x in d\n", TestEngines.Unhurried);
            RunResult plain = Run(loop + "x is d\n", TestEngines.Unhurried);
            Assert.True(hashed.Succeeded && plain.Succeeded, (hashed.Error ?? plain.Error)?.Message);
            Assert.True(hashed.Stats.Steps - plain.Stats.Steps >= 1000 * 7, (hashed.Stats.Steps - plain.Stats.Steps).ToString());
        }

        [Fact]
        public void BoundMethodsMadePerAccessAreCharged()
        {
            RunResult r = Run("class A:\n    def m(self):\n        return 1\na = A()\nr = [a.m for _ in range(2_000_000)]\n",
                TestEngines.Unhurried.With(maxAllocBytes: 64L << 20, regexPerOpTimeoutMs: 60000));   // a busy box must not end it in a regex first
            Assert.Equal("MaxAllocBytes", r.Error.LimitName);
        }

        [Fact]
        public void TheSnifferChargesTheLinesItKeeps()
        {
            RunResult r = Run("import csv\ncsv.Sniffer().sniff('a\\n' * 3000000)\n", TestEngines.Unhurried.With(maxAllocBytes: 64L << 20, regexPerOpTimeoutMs: 60000));   // a busy box must not end it in a regex first
            Assert.Equal("MaxAllocBytes", r.Error.LimitName);
        }

        [Fact]
        public void StrptimeChargesAWhitespaceRun()
        {
            RunResult r = Run("import time\ns = ' ' * 1000000 + 'x'\nfor _ in range(1000):\n    time.strptime(s, ' x')\n",
                Steps(100_000));
            Assert.Equal("MaxSteps", r.Error.LimitName);
        }

        [Theory]
        [InlineData("123456789012345678901234567890")]
        [InlineData("1_000_000_000_000_000_000_000")]
        [InlineData("100000000000000000000000000000000000")]
        [InlineData("999999999999999999")]
        [InlineData("1000000000000000000")]
        public void IntegerLiteralsParseToTheirValue(string literal)
        {
            RunResult r = Run("r = " + literal + "\n", ResourceLimits.Default);
            Assert.Equal(BigInteger.Parse(literal.Replace("_", "")).ToString(), r.ScalarsOut["r"]);
        }

        [Fact]
        public void ALongIntegerLiteralRoundTrips()
        {
            string digits = "7" + new string('3', 4298) + "1";
            RunResult r = Run("r = " + digits + " % 1000003\n", ResourceLimits.Default);
            Assert.Equal((BigInteger.Parse(digits) % 1000003).ToString(), r.ScalarsOut["r"]);
        }

        // The magnitude is read in place; the copy it replaces stays the reference.
        [Fact]
        public void BitLengthMatchesTheByteCopy()
        {
            var rnd = new Random(7);
            for (int i = 0; i < 2000; i++)
            {
                var bytes = new byte[1 + rnd.Next(64)];
                rnd.NextBytes(bytes);
                var v = new BigInteger(bytes);
                Assert.Equal(Reference(v), NumericOps.BitLength(v));
            }
            for (int k = 0; k < 300; k++)
            {
                BigInteger p = BigInteger.One << k;
                foreach (BigInteger v in new[] { p, p - 1, p + 1, -p, -p + 1, -p - 1 })
                    Assert.Equal(Reference(v), NumericOps.BitLength(v));
            }
        }

        private static long Reference(BigInteger v)
        {
            if (v.IsZero)
                return 0;
            byte[] b = BigInteger.Abs(v).ToByteArray();
            int top = b.Length - 1;
            if (b[top] == 0)
                top--;
            long bits = 8L * top;
            for (int x = b[top]; x != 0; x >>= 1)
                bits++;
            return bits;
        }
    }
}
