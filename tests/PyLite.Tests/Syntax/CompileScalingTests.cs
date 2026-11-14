using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    // A compile runs on the host's thread, before any deadline, so its time must grow with the source and not with
    // the source's square (the pattern checks took 53 s over 464 KB, review 4). Each shape is compiled at a size and
    // at eight times it: 8 is linear and 64 quadratic, so a ratio survives a busy machine as a time could not.
    [Trait("Category", "ResourceLimits")]
    [Trait("Category", "MachineSensitive")]
    public class CompileScalingTests
    {
        private const int Small = 3000;
        private const double MaxRatio = 24;

        private static string Join(int n, Func<int, string> item)
        {
            return string.Join(", ", Enumerable.Range(0, n).Select(item));
        }

        private static long BestMs(string src)
        {
            long best = long.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                var sw = Stopwatch.StartNew();
                Compiler.Compile(src, "<test>", SyntaxLimits.Default);
                best = Math.Min(best, sw.ElapsedTicks);
            }
            return best;
        }

        private static void GrowsLinearly(Func<int, string> shape)
        {
            BestMs(shape(100));   // JIT
            double ratio = 0;
            for (int attempt = 0; attempt < 3; attempt++)   // a parallel test can stall one pass, not three
            {
                long small = BestMs(shape(Small));
                long large = BestMs(shape(8 * Small));
                ratio = (double)large / Math.Max(small, 1);
                if (ratio < MaxRatio)
                    return;
            }
            Assert.True(false, "8x the source took " + ratio.ToString("F1") + "x the time");
        }

        [Fact]
        public void MappingKeys()
        {
            GrowsLinearly(n => "match x:\n    case {" + Join(n, i => i + ": _") + "}:\n        pass\n");
        }

        [Fact]
        public void Captures()
        {
            GrowsLinearly(n => "match x:\n    case [" + Join(n, i => "a" + i) + "]:\n        pass\n");
        }

        [Fact]
        public void ClassPatternKeywords()
        {
            GrowsLinearly(n => "match x:\n    case C(" + Join(n, i => "a" + i + "=_") + "):\n        pass\n");
        }

        [Fact]
        public void AlternativesBindingNames()
        {
            GrowsLinearly(n => "match x:\n    case " + string.Join(" | ", Enumerable.Range(0, n).Select(i => "[1, " + i + ", a]")) + ":\n        pass\n");
        }

        [Fact]
        public void TwoLongRadixKeys()
        {
            GrowsLinearly(n => "match x:\n    case {0x" + new string('f', 4 * n) + "1: 1, 0x" + new string('f', 4 * n) + "2: 2}:\n        pass\n");
        }

        [Fact]
        public void PrivateNamesUnderALongClassName()
        {
            GrowsLinearly(n => "class " + new string('C', 2000) + ":\n    def m(self, x):\n        return (" + Join(n, i => "x.__a") + ")\n");
        }

        [Fact]
        public void MethodDefaultsUnderALongName()
        {
            // The name grows with the defaults: at a fixed name the old cost was linear too, and this could not fail.
            GrowsLinearly(n => "class C:\n    def " + new string('m', n) + "(self, " + Join(n, i => "a" + i + "=0") + "):\n        pass\n");
        }

        // Keys whose BigInteger hashes collide: compiled on the host's thread, outside every limit.
        [Fact]
        public void MappingKeysThatCollide()
        {
            GrowsLinearly(n => "match x:\n    case {" + Join(n, i => (((long)(i + 1)) * 4294983808L).ToString(System.Globalization.CultureInfo.InvariantCulture) + ": _") + "}:\n        pass\n");
        }

        [Fact]
        public void ManyComprehensionsInOneFunction()
        {
            GrowsLinearly(n =>
            {
                var sb = new StringBuilder("def f(d):\n");
                for (int i = 0; i < n / 4; i++)
                    sb.Append("    [x for x in d]\n");
                return sb.ToString();
            });
        }
    }
}
