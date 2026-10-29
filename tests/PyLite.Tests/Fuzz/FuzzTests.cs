using System;
using System.Collections.Generic;
using Outbridge.PyLite.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Outbridge.PyLite.Tests.Fuzz
{
    // T-TST-10: the fuzzer smoke run + component tests. The key guarantee: over thousands of generated and
    // mutated programs the engine NEVER crashes (R-13) — only legal Syntax/Runtime/Budget/Host outcomes.
    [Trait("Category", "Fuzz")]
    public class FuzzTests
    {
        private readonly ITestOutputHelper _out;
        public FuzzTests(ITestOutputHelper output) { _out = output; }

        private static ScriptEngine NewEngine()
        {
            return new ScriptEngine(new EngineOptions
            {
                Clock = new SystemClock(TimeSpan.Zero, "UTC"),
                SleepPolicy = new NoOpSleepPolicy(),
                StrHashSeed = 0x12345678u,
            });
        }

        [Fact]
        public void GeneratorIsDeterministicBySeed()
        {
            string a = new GrammarFuzzer(new FuzzConfig { Seed = 99 }).GenModule();
            string b = new GrammarFuzzer(new FuzzConfig { Seed = 99 }).GenModule();
            string c = new GrammarFuzzer(new FuzzConfig { Seed = 100 }).GenModule();
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
        }

        [Fact]
        public void MutatorIsDeterministicBySeed()
        {
            const string src = "x = 1\ny = x + 2\nprint(y)\n";
            Assert.Equal(new CorpusMutator(7).Mutate(src, 4), new CorpusMutator(7).Mutate(src, 4));
        }

        [Fact]
        public void SmokeRunNeverCrashes()
        {
            var engine = NewEngine();
            var orch = new FuzzOrchestrator(engine, new FuzzConfig { Seed = 2026 }, Corpus());
            FuzzReport report = orch.Run(TimeSpan.FromSeconds(25), 3000);
            _out.WriteLine(report.ToString());
            Assert.True(report.Iterations > 0);
            Assert.Equal(0, report.Crashes);
            Assert.Equal(0, report.EngineFaults);
            // A healthy generator produces a MIX of outcomes (not everything a SyntaxError).
            Assert.True(report.LegalSuccesses > 0, "no successful programs generated");
            Assert.True(report.LegalRuntimeErrors > 0, "no runtime errors generated");
        }

        [Fact]
        public void ForbiddenConstructsAreCleanSyntaxErrors()
        {
            var engine = NewEngine();
            var orch = new FuzzOrchestrator(engine, new FuzzConfig { Seed = 5, AllowForbidden = true }, null);
            FuzzReport report = orch.Run(TimeSpan.FromSeconds(10), 1000);
            _out.WriteLine(report.ToString());
            Assert.Equal(0, report.Crashes);
            Assert.Equal(0, report.EngineFaults);
        }

        [Fact]
        public void MutatedCorpusNeverCrashes()
        {
            var engine = NewEngine();
            var report = new FuzzReport();
            var mutator = new CorpusMutator(42);
            foreach (string seed in Corpus())
                for (int i = 0; i < 40; i++)
                    orchClassify(engine, mutator.Mutate(seed, 1 + i % 5), report);
            _out.WriteLine(report.ToString());
            Assert.Equal(0, report.Crashes);
            Assert.Equal(0, report.EngineFaults);
        }

        [Fact]
        public void MinimizerShrinksARepro()
        {
            // The predicate: "contains the marker line". ddmin must strip everything else.
            string bloated = "a = 1\nb = 2\nMARKER = 3\nc = 4\nd = 5\n";
            string min = DeltaMinimizer.Minimize(bloated, s => s.Contains("MARKER"));
            Assert.Equal("MARKER = 3", min.Trim());
        }

        private static void orchClassify(ScriptEngine engine, string source, FuzzReport report)
        {
            new FuzzOrchestrator(engine, new FuzzConfig(), null).Classify(source, report);
            report.Iterations++;
        }

        private static string[] Corpus()
        {
            return new[]
            {
                "import math\nx = math.sqrt(16)\nresult = x + math.pi\n",
                "d = {}\nfor i in range(10):\n    d[i] = i * i\nresult = sum(d.values())\n",
                "import json\nresult = json.dumps({'a': [1, 2, 3], 'b': True})\n",
                "def fib(n):\n    return n if n < 2 else fib(n - 1) + fib(n - 2)\nresult = fib(10)\n",
                "from collections import Counter\nc = Counter('abracadabra')\nresult = c.most_common(2)\n",
                "s = 'hello world'\nresult = s.upper().split()\n",
                "import re\nresult = re.findall(r'\\d+', 'a1b22c333')\n",
                "nums = [3, 1, 4, 1, 5, 9, 2, 6]\nresult = sorted(nums, reverse=True)[:3]\n",
            };
        }
    }
}
