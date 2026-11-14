using System;
using System.Linq;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // Fixes whose answers never changed: only what they charge or refuse shows, so each test compares that.
    [Trait("Category", "ResourceLimits")]
    public class ChargeHoldTests
    {
        internal static RunStatistics Stats(string source)
        {
            ResourceLimits limits = TestEngines.Unhurried.With(deadlineMs: 180000);   // busy with the whole suite, these took past 20 s
            RunResult r = new ScriptEngine(new EngineOptions { DefaultLimits = limits }).Run(source, new RunRequest());
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Message);
            return r.Stats;
        }

        internal static string Classes(int n)
        {
            return string.Concat(Enumerable.Range(0, n).Select(i => "class K" + i + ":\n    pass\n"))
                + "ks = [" + string.Join(", ", Enumerable.Range(0, n).Select(i => "K" + i)) + "]\n";
        }

        [Fact]
        public void SortingJsonKeysIsCharged()
        {
            string dict = "import json\nd = {('k' * 2000) + str(i): i for i in range(3000)}\n";
            long sorted = Stats(dict + "json.dumps(d, sort_keys=True)\n").Steps;
            long plain = Stats(dict + "json.dumps(d)\n").Steps;
            Assert.True(sorted - plain >= 15000, (sorted - plain).ToString());
        }

        [Fact]
        public void AGrowingNormalizeBufferIsCharged()
        {
            long bytes = Stats("import unicodedata\nunicodedata.normalize('NFKD', '\\uFDFA' * 100000)\n").AllocatedBytes;
            Assert.True(bytes >= 10L << 20, bytes.ToString());
        }

        [Fact]
        public void ACancelledTokenRefusesBeforeAThreadStarts()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                RunResult r = new ScriptEngine(new EngineOptions()).Run("print('ran')", new RunRequest { CancellationToken = cts.Token });
                Assert.Equal("run cancelled before it started", r.Error.Message);
                Assert.Equal(0, r.Stats.Steps);
            }
        }

        [Fact]
        public void AClassInsideAMethodManglesWithItsOwnName()
        {
            string source = "class A:\n    def make(self):\n        class B:\n            def __init__(self):\n                self.__x = 1\n"
                + "        return B()\nb = A().make()\nprint(hasattr(b, '_B__x'), hasattr(b, '_A__x'))\n";
            RunResult r = new ScriptEngine(new EngineOptions()).Run(source, new RunRequest());
            Assert.Equal("True False\n", r.PrintOutput);
        }
    }

    // Seconds of CPU: 18M set inserts per run, so not with the everyday tests.
    [Trait("Category", "MachineSensitive")]
    [Trait("Category", "ResourceLimits")]
    public class WideUnionChargeTests
    {
        [Fact]
        public void BuildingAWideUnionAndTestingAgainstItAreCharged()
        {
            string wide = ChargeHoldTests.Classes(4200) + "U = ks[0]\nfor k in ks[1:]:\n    U = U | k\nfor i in range(2000):\n    isinstance(i, U)\n";
            string narrow = ChargeHoldTests.Classes(4200) + "U = ks[0]\nfor k in ks[1:]:\n    U = U | ks[0]\nfor i in range(2000):\n    isinstance(i, U)\n";
            long extra = ChargeHoldTests.Stats(wide).Steps - ChargeHoldTests.Stats(narrow).Steps;   // a step per 4,096 members: both halves are needed to reach it
            Assert.True(extra >= 3000, extra.ToString());
        }
    }

    // Wide Decimal format: the charge is the same with or without extra copies, only the heap shows them.
    [Trait("Category", "MachineSensitive")]
    [Collection("HeapSensitive")]
    [Trait("Category", "ResourceLimits")]
    public class DecimalFormatBufferTests
    {
        [Fact]
        public void AWideFormatKeepsOneBuffer()
        {
            AppDomain.MonitoringIsEnabled = true;
            var engine = new ScriptEngine(new EngineOptions());
            long best = long.MaxValue;
            for (int i = 0; i < 3 && best >= 140L << 20; i++)   // other tests allocate too: the least of three
            {
                long before = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
                RunResult r = engine.Run("from decimal import Decimal\nformat(Decimal('1.5'), '.15900000f')\n",
                    new RunRequest { Limits = TestEngines.Unhurried });
                Assert.True(r.Succeeded);
                best = Math.Min(best, AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - before);
            }
            Assert.True(best < 140L << 20, best.ToString());
        }
    }
}
