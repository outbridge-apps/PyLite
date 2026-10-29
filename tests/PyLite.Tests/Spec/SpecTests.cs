using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Outbridge.PyLite.Tests.Spec
{
    // T-TST-02: golden discovery — every Spec/**/*.py is one case (named by its relative path).
    // A golden .py is self-contained: script + '# out./# print:/# error:' expectations inside.
    [Trait("Category", "Compatibility")]
    public class SpecTests
    {
        public static IEnumerable<object[]> EnumerateGoldenFiles()
        {
            string root = SpecRoot();
            if (!Directory.Exists(root))
                yield break;
            foreach (string path in Directory.GetFiles(root, "*.py", SearchOption.AllDirectories))
                yield return new object[] { path.Substring(root.Length + 1).Replace('\\', '/') };
        }

        private static string SpecRoot()
        {
            // Spec files are copied next to the test dll (CopyToOutputDirectory).
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Spec");
        }

        [Theory]
        [MemberData(nameof(EnumerateGoldenFiles))]
        public void Golden(string relPath)
        {
            GoldenFile gf = GoldenFile.Parse(Path.Combine(SpecRoot(), relPath));
            if (gf.Skip != null)
                return;   // xUnit has no runtime Ignore; a skip directive just short-circuits green
            string verdict = SpecRunner.RunCase(gf);
            Assert.True(verdict == null, relPath + ":\n" + verdict);
        }

        // ---- runner self-tests on synthetic golden strings (impl/12 mandatory table) ----

        private static string WriteTemp(string content)
        {
            string path = Path.Combine(Path.GetTempPath(), "pylite_golden_" + Guid.NewGuid().ToString("N") + ".py");
            File.WriteAllText(path, content);
            return path;
        }

        private static string RunInline(string content)
        {
            string path = WriteTemp(content);
            try
            {
                return SpecRunner.RunCase(GoldenFile.Parse(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void PassOnMatchingScalar()
        {
            Assert.Null(RunInline("# out.x: 2\nx = 1 + 1\n"));
        }

        [Fact]
        public void FailShowsExpectedActual()
        {
            string verdict = RunInline("# out.x: 3\nx = 1 + 1\n");
            Assert.NotNull(verdict);
            Assert.Contains("expected: 3", verdict);
            Assert.Contains("actual:   2", verdict);
        }

        [Fact]
        public void PrintLinesInOrder()
        {
            Assert.Null(RunInline("# print: a\n# print: b\nprint('a')\nprint('b')\n"));
            Assert.NotNull(RunInline("# print: b\n# print: a\nprint('a')\nprint('b')\n"));
        }

        [Fact]
        public void ErrorTypeMatch()
        {
            Assert.Null(RunInline("# error: ValueError\nx = int('zz')\n"));
            Assert.NotNull(RunInline("# error: TypeError\nx = int('zz')\n"));
        }

        [Fact]
        public void ErrorMessageMatch()
        {
            Assert.Null(RunInline("# error: ValueError: invalid literal\nx = int('zz')\n"));
        }

        [Fact]
        public void UnexpectedSuccessFails()
        {
            string verdict = RunInline("# error: ValueError\nx = 1\n");
            Assert.Contains("expected an error", verdict);
        }

        [Fact]
        public void ScalarInputFeedsRun()
        {
            Assert.Null(RunInline("# in.n: 21\n# out.r: 42\nr = n * 2\n"));
        }

        [Fact]
        public void LimitOverrideBudget()
        {
            Assert.Null(RunInline("# limits.steps: 5000\n# error_kind: Budget\nwhile True:\n    pass\n"));
        }

        [Fact]
        public void SeedInjection()
        {
            Assert.Null(RunInline("# seed: 42\nimport random\na = random.random()\nrandom.seed(42)\nassert a == random.random()\n"));
        }

        [Fact]
        public void MissingSpaceAfterColonIsParseError()
        {
            string path = WriteTemp("# out.x:5\nx = 5\n");
            try
            {
                Assert.Throws<FormatException>(() => GoldenFile.Parse(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void DuplicateOutIsParseError()
        {
            string path = WriteTemp("# out.x: 1\n# out.x: 2\nx = 1\n");
            try
            {
                Assert.Throws<FormatException>(() => GoldenFile.Parse(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void EscapesInExpectations()
        {
            Assert.Null(RunInline("# out.s: a\\tb\ns = 'a\\tb'\n"));
        }
    }
}
