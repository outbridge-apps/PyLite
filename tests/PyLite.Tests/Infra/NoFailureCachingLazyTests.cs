using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Outbridge.PyLite.Tests.Infra
{
    // A Lazy<T> in its default mode keeps the exception of a failed build: a host thread aborted (an ASP.NET timeout)
    // while it builds a process-wide table made every later run an engine fault until restart. The gate is the
    // source: every Lazy in the engine publishes only a finished value (PublicationOnly), or is not a Lazy at all.
    [Trait("Category", "Regression")]
    public class NoFailureCachingLazyTests
    {
        private static readonly Regex NewLazy = new Regex(@"new\s+Lazy<[^;]*;", RegexOptions.CultureInvariant | RegexOptions.Singleline);

        [Fact]
        public void EveryLazyInTheEnginePublishesOnlyAFinishedValue()
        {
            string src = Path.Combine(RepoPaths.Root(), "src", "PyLite");
            var hits = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
                .Where(f => f.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) < 0)
                .SelectMany(f => Offenders(File.ReadAllText(f)).Select(s => Path.GetFileName(f) + ": " + s))
                .ToList();
            Assert.True(hits.Count == 0, string.Join("\n", hits));
        }

        [Fact]
        public void TheBanCatchesWhatItIsFor()
        {
            Assert.Single(Offenders("static readonly Lazy<int> X = new Lazy<int>(() => 1);"));
            Assert.Empty(Offenders("var l = new Lazy<int>(() => 1,\n    LazyThreadSafetyMode.PublicationOnly);"));
            Assert.Empty(Offenders("// new Lazy<int>(() => 1);"));
        }

        private static string[] Offenders(string text)
        {
            return NewLazy.Matches(text).Cast<Match>()
                .Where(m => m.Value.IndexOf("PublicationOnly", StringComparison.Ordinal) < 0 && !InComment(text, m.Index))
                .Select(m => Regex.Replace(m.Value, @"\s+", " "))
                .ToArray();
        }

        private static bool InComment(string text, int at)
        {
            int lineStart = text.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
            return text.IndexOf("//", lineStart, at - lineStart, StringComparison.Ordinal) >= 0;
        }
    }
}
