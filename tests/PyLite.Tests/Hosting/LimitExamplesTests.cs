using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // examples/limits holds one script per ResourceLimits member; its header names the limits the host sets
    // ("# limits: MaxSteps=1000000") and the outcome ("# expect: BudgetExceeded MaxSteps"). Each must end
    // exactly so, and the same engine must then run an ordinary script as if nothing had happened.
    [Trait("Category", "ResourceLimits")]
    public class LimitExamplesTests
    {
        private static readonly ScriptEngine Engine = new ScriptEngine(new EngineOptions());

        public static IEnumerable<object[]> Examples()
        {
            return Directory.GetFiles(ExamplesDir(), "*.py").OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new object[] { Path.GetFileName(p) });
        }

        [Fact]
        public void EveryLimitHasAnExample()
        {
            string all = string.Join("\n", Directory.GetFiles(ExamplesDir(), "*.py").Select(File.ReadAllText));
            foreach (string limit in new[] { "MaxSteps", "DeadlineMs", "MaxAllocBytes", "MaxCallDepth", "MaxDataDepth",
                "MaxStrChars", "MaxCollectionItems", "MaxIntBits", "MaxOutputBytes", "RegexPerOpTimeoutMs", "SleepTotalMs" })
                Assert.Contains("# limits: " + limit + "=", all);
        }

        [Theory]
        [MemberData(nameof(Examples))]
        public void TheLimitFiresAsDescribed(string file)
        {
            string src = File.ReadAllText(Path.Combine(ExamplesDir(), file));
            string[] expect = Header(src, "expect").Split(' ');
            RunResult r = Engine.Run(src, new RunRequest { Limits = LimitsOf(Header(src, "limits")) });

            Assert.False(r.Succeeded, file + " succeeded");
            Assert.Equal(expect[0], r.Error.Kind.ToString());
            Assert.Equal(expect[1], r.Error.Kind == ScriptErrorKind.BudgetExceeded ? r.Error.LimitName : r.Error.PythonType);

            RunResult next = Engine.Run("total = sum(range(10))\n",
                new RunRequest { ScalarOutputNames = new[] { "total" } });
            Assert.True(next.Succeeded, "the run after " + file + " failed");
            Assert.Equal("45", next.ScalarsOut["total"]);
        }

        private static string Header(string src, string key)
        {
            string prefix = "# " + key + ": ";
            string line = src.Split('\n').Select(l => l.TrimEnd('\r')).FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
            Assert.NotNull(line);
            return line.Substring(prefix.Length).Trim();
        }

        private static ResourceLimits LimitsOf(string spec)
        {
            ResourceLimits lim = ResourceLimits.Default;
            foreach (string pair in spec.Split(','))
            {
                string[] kv = pair.Trim().Split('=');
                long v = long.Parse(kv[1]);
                switch (kv[0])
                {
                    case "MaxSteps": lim = lim.With(maxSteps: v); break;
                    case "DeadlineMs": lim = lim.With(deadlineMs: (int)v); break;
                    case "MaxAllocBytes": lim = lim.With(maxAllocBytes: v); break;
                    case "MaxCallDepth": lim = lim.With(maxCallDepth: (int)v); break;
                    case "MaxDataDepth": lim = lim.With(maxDataDepth: (int)v); break;
                    case "MaxStrChars": lim = lim.With(maxStrChars: (int)v); break;
                    case "MaxCollectionItems": lim = lim.With(maxCollectionItems: (int)v); break;
                    case "MaxIntBits": lim = lim.With(maxIntBits: (int)v); break;
                    case "MaxOutputBytes": lim = lim.With(maxOutputBytes: (int)v); break;
                    case "RegexPerOpTimeoutMs": lim = lim.With(regexPerOpTimeoutMs: (int)v); break;
                    case "SleepTotalMs": lim = lim.With(sleepTotalMs: (int)v); break;
                    default: throw new ArgumentException("unknown limit " + kv[0]);
                }
            }
            return lim;
        }

        private static string ExamplesDir()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "examples", "limits")))
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            Assert.NotNull(dir);
            return Path.Combine(dir, "examples", "limits");
        }
    }
}
