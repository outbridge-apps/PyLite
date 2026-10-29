using System;
using System.Threading.Tasks;
using Outbridge.PyLite.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Outbridge.PyLite.Tests.Stress
{
    // Substring search under the sandbox contract. The BCL's ordinal IndexOf is a plain O(n*m) scan, so a
    // long needle over a long haystack used to run for minutes inside one native call and leak the runner
    // thread. PySearch now runs Knuth-Morris-Pratt once the quadratic worst case grows past a window, so
    // these answer — correctly, and well inside a deadline no real search would notice.
    [Collection("TimeSensitive")]   // asserts wall-clock bounds; must not race the rest of the suite
    [Trait("Category", "ResourceLimits")]
    [Trait("Category", "MachineSensitive")]
    public class SearchStressTests
    {
        private readonly ITestOutputHelper _out;
        public SearchStressTests(ITestOutputHelper output) { _out = output; }

        // 400k positions x 200k compares is ~8e10 character comparisons for the quadratic scan.
        private const string Miss = "s = 'a' * 400000\nn = 'a' * 200000 + 'b'\n";
        private const string Hit = "s = 'a' * 200000 + 'b' + 'a' * 200000\nn = 'a' * 100000 + 'b'\n";
        private const string BMiss = "s = b'a' * 400000\nn = b'a' * 200000 + b'b'\n";
        private const string BHit = "s = b'a' * 200000 + b'b' + b'a' * 200000\nn = b'a' * 100000 + b'b'\n";

        private string Run(string source)
        {
            var engine = new ScriptEngine(new EngineOptions
            {
                DefaultLimits = ResourceLimits.Default.With(deadlineMs: 4000),
                Clock = new SystemClock(TimeSpan.Zero, "UTC"),
                SleepPolicy = new NoOpSleepPolicy(),
            });
            RunResult result = null;
            var t = Task.Run(() => result = engine.Run(source, new RunRequest()));
            Assert.True(t.Wait(30000), "engine hung past the external cap");
            Assert.NotNull(result);
            if (!result.Succeeded)
                _out.WriteLine(result.Error.Kind + ": " + result.Error.Message);
            Assert.True(result.Succeeded, "the search did not answer: " + (result.Error != null ? result.Error.Message : ""));
            return result.PrintOutput.Replace("\r\n", "\n").TrimEnd('\n');
        }

        [Theory]
        [InlineData(Miss + "print(s.find(n))", "-1")]
        [InlineData(Miss + "print(s.rfind(n))", "-1")]
        [InlineData(Miss + "print(n in s)", "False")]
        [InlineData(Miss + "print(s.count(n))", "0")]
        [InlineData(Miss + "print(len(s.replace(n, 'x')))", "400000")]
        [InlineData(Miss + "print(len(s.split(n)))", "1")]
        [InlineData(Miss + "print(len(s.rsplit(n)))", "1")]
        [InlineData(Miss + "print(s.partition(n)[0] == s)", "True")]
        [InlineData(Miss + "print(s.rpartition(n)[2] == s)", "True")]
        [InlineData(Hit + "print(s.find(n))", "100000")]
        [InlineData(Hit + "print(s.rfind(n))", "100000")]
        [InlineData(Hit + "print(s.index(n))", "100000")]
        [InlineData(Hit + "print(n in s)", "True")]
        [InlineData(Hit + "print(s.count(n))", "1")]
        [InlineData(Hit + "print(len(s.split(n)))", "2")]
        [InlineData(Hit + "print(len(s.partition(n)[2]))", "200000")]
        public void Str_LongNeedleAnswers(string body, string expected)
        {
            Assert.Equal(expected, Run(body + "\n"));
        }

        [Theory]
        [InlineData(BMiss + "print(s.find(n))", "-1")]
        [InlineData(BMiss + "print(s.rfind(n))", "-1")]
        [InlineData(BMiss + "print(n in s)", "False")]
        [InlineData(BMiss + "print(s.count(n))", "0")]
        [InlineData(BMiss + "print(len(s.split(n)))", "1")]
        [InlineData(BHit + "print(s.find(n))", "100000")]
        [InlineData(BHit + "print(s.rfind(n))", "100000")]
        [InlineData(BHit + "print(s.count(n))", "1")]
        [InlineData(BHit + "print(len(s.partition(n)[2]))", "200000")]
        public void Bytes_LongNeedleAnswers(string body, string expected)
        {
            Assert.Equal(expected, Run(body + "\n"));
        }

        // The linear path takes over inside a window, so a match that straddles two windows, an
        // overlapping one, and the last of several all have to come back the same as a direct scan would.
        [Theory]
        [InlineData("s = 'ab' * 3000000\nn = 'b' + 'ab' * 2000\nprint((s.find(n), s.rfind(n), s.count('ab' * 3000)))",
            "(1, 5995999, 1000)")]
        [InlineData("s = b'ab' * 3000000\nn = b'b' + b'ab' * 2000\nprint((s.find(n), s.rfind(n), s.count(b'ab' * 3000)))",
            "(1, 5995999, 1000)")]
        [InlineData("s = 'x' * 3000000 + 'needle'\nn = 'x' * 10 + 'needle'\nprint((s.find(n), s.rfind(n), s.rpartition(n)[0] == 'x' * 2999990))",
            "(2999990, 2999990, True)")]
        [InlineData("s = 'ab' * 2000000\nn = 'bb' + 'ab' * 5000\nprint((s.find(n), s.rfind(n), s.count(n), n in s, s.split(n) == [s]))",
            "(-1, -1, 0, False, True)")]
        // one match, a million characters into a two-million-character subject, found from both ends
        [InlineData("s = 'a' * 1000000 + 'b' + 'a' * 1000000\nn = 'a' * 500000 + 'b' + 'a' * 500000\nprint((s.find(n), s.rfind(n), s.count(n)))",
            "(500000, 500000, 1)")]
        public void SlowPathAnswersLikeTheFastOne(string body, string expected)
        {
            Assert.Equal(expected, Run(body + "\n"));
        }

        // A long argument is a table, not a per-character scan: these finish well inside the deadline.
        [Theory]
        [InlineData("print(len(('a' * 1000000).strip('b' * 1000000 + 'a')))", "0")]
        [InlineData("print(len((b'a' * 1000000).strip(b'b' * 1000000 + b'a')))", "0")]
        [InlineData("import urllib.parse\nprint(len(urllib.parse.quote('%' * 300000, safe='!' * 300000)))", "900000")]
        [InlineData("import urllib.parse\nprint(urllib.parse.quote_plus('a b' * 100000, safe='/' * 200000) == 'a+b' * 100000)", "True")]
        public void LongArgumentsAreTables(string body, string expected)
        {
            Assert.Equal(expected, Run(body + "\n"));
        }
    }
}
