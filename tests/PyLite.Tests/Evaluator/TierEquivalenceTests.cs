using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Evaluator
{
    // The fast tiers (compiled closures, fused longs, flat comprehension drain, range loop) add speed,
    // never semantics: every corpus script must print the same and end the same on the tree walk alone (D1).
    [Trait("Category", "Regression")]
    public class TierEquivalenceTests
    {
        public static IEnumerable<object[]> Scripts()
        {
            string root = RepoRoot();
            var files = new List<string>();
            files.AddRange(Directory.GetFiles(Path.Combine(root, "conformance", "cases"), "*.py", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(Path.Combine(root, "tools", "diff-corpus"), "*.py"));
            return files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => new object[] { f.Substring(root.Length + 1).Replace('\\', '/') });
        }

        [Theory]
        [MemberData(nameof(Scripts))]
        public void TreeWalkAloneGivesTheSameRun(string script)
        {
            string source = File.ReadAllText(Path.Combine(RepoRoot(), script));
            if (source.Split('\n').TakeWhile(l => l.TrimStart().StartsWith("#")).Any(l => l.TrimStart().StartsWith("# skip:")))
                return;

            RunResult fast = Engine(false).Run(source, new RunRequest());
            RunResult slow = Engine(true).Run(source, new RunRequest());

            Assert.Equal(Describe(fast), Describe(slow));
            Assert.Equal(fast.PrintOutput, slow.PrintOutput);
        }

        // The switch is real: the fused range drain never boxes the loop variable, the tree walk does.
        [Fact]
        public void InterpretOnlyTurnsTheTiersOff()
        {
            const string src = "r = [x * 2 for x in range(1000)]\nprint(sum(r))\n";
            RunResult fast = Engine(false).Run(src, new RunRequest());
            RunResult slow = Engine(true).Run(src, new RunRequest());
            Assert.Equal("999000\n", fast.PrintOutput);
            Assert.Equal(fast.PrintOutput, slow.PrintOutput);
            Assert.True(slow.Stats.AllocatedBytes > fast.Stats.AllocatedBytes);
        }

        private static ScriptEngine Engine(bool interpretOnly)
        {
            return new ScriptEngine(new EngineOptions
            {
                DefaultLimits = ResourceLimits.Default.With(deadlineMs: 60_000, maxAllocBytes: 256L << 20, maxCallDepth: 512),
                InterpretOnly = interpretOnly,
            });
        }

        private static string Describe(RunResult r)
        {
            return r.Succeeded ? "ok" : (r.Error.PythonType ?? r.Error.Kind.ToString()) + " at " + r.Error.Line + ": " + r.Error.Message;
        }

        private static string RepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "conformance", "cases")))
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            Assert.NotNull(dir);
            return dir.TrimEnd(Path.DirectorySeparatorChar);
        }
    }
}
