using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Modules;
using Outbridge.PyLite.Modules.Support;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // Review 15: a pattern that starts with c{m} keeps m c's and a Boyer-Moore table over them (6 B a repeat, built in
    // time quadratic in m), which was charged as the seven characters it is written in; and the exact translation's
    // text was held twice once built. The same scripts at their full size are the bank's `r21_*` probes.
    [Trait("Category", "Regression")]
    public class Review15FixTests
    {
        private static RunResult Run(string source, long? maxAllocBytes = null)
        {
            ResourceLimits limits = TestEngines.Unhurried.With(maxAllocBytes: maxAllocBytes);
            return new ScriptEngine(new EngineOptions { DefaultLimits = limits }).Run(source, new RunRequest());
        }

        // What RegexFCD.Prefix makes the prefix: it enters capturing, non-capturing, named and atomic groups and
        // passes anchors, lookarounds, options and comments, and a class of one is its character; a branch, a
        // repeated group or a literal before ends it. Each row's bytes were measured on .NET Framework 4.8.
        [Theory]
        [InlineData("a{1500}x", false, 1500)]
        [InlineData("(a{1500})x", false, 1500)]
        [InlineData("(?=q)a{1500}x", false, 1500)]
        [InlineData("(?<!q)a{9}x", false, 9)]
        [InlineData("^a{1500,}x", false, 1500)]
        [InlineData(@"\Ga{1500}?x", false, 1500)]
        [InlineData(@"\G(?:a{7}x)\z", false, 7)]
        [InlineData("(?>a{12})", false, 12)]
        [InlineData("(?<n>a{7})", false, 7)]
        [InlineData("(?'n'a{7})", false, 7)]
        [InlineData("(?i)a{7}", false, 7)]
        [InlineData("(?i:a{7})", false, 7)]
        [InlineData("(?#c)a{7}", false, 7)]
        [InlineData(@"\.{7}", false, 7)]
        [InlineData("a{7,9}", false, 7)]
        [InlineData("(?>)a{5000}", false, 0)]
        [InlineData("ba{1500}", false, 0)]
        [InlineData("(?:a{1500})+x", false, 0)]
        [InlineData("(?:a{7}){2}", false, 0)]
        [InlineData("a|b{1500}", false, 0)]
        [InlineData("(a{7}|b)", false, 0)]
        [InlineData("a{0,9}", false, 0)]
        [InlineData("a{x}", false, 0)]
        [InlineData("a{7", false, 0)]
        [InlineData(@"\d{7}", false, 0)]
        [InlineData("[a]{7}", false, 7)]
        [InlineData("[]]{7}", false, 7)]
        [InlineData("[ab]{7}", false, 0)]
        [InlineData("[^a]{7}", false, 0)]
        [InlineData("(?:a){7}", false, 0)]
        [InlineData("(?(1)a{7})", false, 0)]
        [InlineData("  a {7}x", true, 7)]
        [InlineData("  a {7}x", false, 0)]
        [InlineData("(?:a{7}){1}x", false, 7)]
        [InlineData("(?:a{7}){1,1}x", false, 7)]
        [InlineData("(?:a{7}){1}?x", false, 7)]
        [InlineData(@"\Za{7}x", false, 7)]
        [InlineData("$a{7}x", false, 7)]
        [InlineData(@"\za{7}x", false, 7)]
        [InlineData("(?:)a{7}x", false, 7)]
        [InlineData("(?:^)a{7}x", false, 7)]
        [InlineData(@"(?:\b)a{7}x", false, 7)]
        [InlineData("(?:(?=x))a{7}x", false, 7)]
        [InlineData("(?:(?:^)$)a{7}x", false, 7)]
        [InlineData("(^)a{7}x", false, 0)]
        [InlineData("(?x: a {7})", false, 7)]
        [InlineData("(?x:a {7})", false, 7)]
        [InlineData("(?x)  a {7}x", false, 7)]
        [InlineData("(?-x:a {7})", true, 0)]
        [InlineData(@"\0{7}x", false, 7)]
        [InlineData(@"[\b]{7}x", false, 7)]
        [InlineData("}{1500}x", false, 1500)]
        [InlineData("{{1500}x", false, 1500)]
        [InlineData(@"\Ba{1500}x", false, 0)]
        [InlineData("(?:a{1500}) *x", true, 0)]
        [InlineData("(?:a{10,}){150}x", false, 1500)]
        [InlineData("(?:a+){1500}x", false, 1500)]
        [InlineData("(?:a*){1500}x", false, 0)]
        [InlineData("(?:a{10}){150}x", false, 0)]
        [InlineData("(?:a{10,20}){150}x", false, 1500)]
        [InlineData("(?:a{10,19}){150}x", false, 0)]
        [InlineData("(?:a{10,})+x", false, 10)]
        [InlineData("(?:a{10,}){1}x", false, 10)]
        [InlineData("(?:a{10,}?){150}?x", false, 1500)]
        [InlineData("(?:a{10,}?){150}x", false, 0)]
        [InlineData("(?:a{10,}){150}?x", false, 0)]
        [InlineData("(a{10,}){150}x", false, 0)]
        [InlineData("(?>a{10,}){150}x", false, 0)]
        [InlineData("(?:a{10,}b){150}x", false, 0)]
        [InlineData("(?i:a{10,}){150}x", false, 1500)]
        [InlineData("(?:(?:a{3,}){10,}){50}x", false, 1500)]
        [InlineData("(?:(?:a{10,})){150}x", false, 1500)]
        [InlineData("(?:a{10,}(?:)){150}x", false, 1500)]
        [InlineData("(?:a{10,}(?i)){150}x", false, 1500)]
        [InlineData("(?:(?:)a{10,}){150}x", false, 1500)]
        [InlineData("(?:(?i)a{10,}){150}x", false, 1500)]
        [InlineData("(?:^a{10,}){150}x", false, 0)]
        [InlineData("(?:(a{10,})){150}x", false, 0)]
        [InlineData("(?:(?:^)a{10,}){150}x", false, 0)]
        [InlineData("(?:a{10,}(?=x)){150}x", false, 0)]
        [InlineData("((?:a{10,}){150})x", false, 1500)]
        [InlineData("(?:(?:a{10,}){1}){150}x", false, 1500)]
        [InlineData("[aa]{1500}x", false, 1500)]
        [InlineData("[a-a]{1500}x", false, 1500)]
        [InlineData("[a-b]{1500}x", false, 0)]
        [InlineData("x{0}a{1500}", false, 1500)]
        [InlineData("x{0,0}a{7}", false, 7)]
        [InlineData("x{0}?a{7}", false, 7)]
        [InlineData("(xy){0}a{7}", false, 7)]
        [InlineData("(?:xy){0}a{7}", false, 7)]
        [InlineData("[xy]{0}a{7}", false, 7)]
        [InlineData(@"\d{0}a{7}", false, 7)]
        [InlineData(".{0}a{7}", false, 7)]
        [InlineData("(?:x{0}a{7})", false, 7)]
        [InlineData("(?:x{0}a{10,}){150}x", false, 1500)]
        [InlineData("(?:a{10,}x{0}){150}x", false, 1500)]
        [InlineData("(?i)x{0}A{7}", false, 7)]
        [InlineData("(?:a{10,}(?i:)){400}x", false, 4000)]
        [InlineData("(?:a{10,}(?:(?:))){150}x", false, 1500)]
        [InlineData("(?:a{10,}(?:){1}){150}x", false, 1500)]
        [InlineData("[a]{4,14}?x", false, 0)]
        [InlineData(@"[\t]{12}?x", false, 0)]
        [InlineData("(?:){1}a{1500}x", false, 1500)]
        [InlineData("(?=a){1}a{1500}x", false, 1500)]
        [InlineData("(?=a){1}?a{7}x", false, 7)]
        [InlineData("(?:(?:){1}){1}a{7}x", false, 7)]
        [InlineData("(?:(?=a){1}a{7})x", false, 7)]
        [InlineData("(?=a){2}a{7}x", false, 0)]
        [InlineData("(?x:a{2000}) {2}x", false, 2000)]
        [InlineData("(?-x:a{20}) {2}x", true, 0)]
        [InlineData("(?x:(?:a{20}) {2})x", false, 0)]
        [InlineData(@"[\p{L}-[\p{N}]]{0}a{1500}x", false, 1500)]
        [InlineData(@"(?=(a))\k<1>{0}a{1500}x", false, 1500)]
        [InlineData("(?:a{10,}\v){400}x", true, 0)]
        [InlineData("(?:a{10,}\t){400}x", true, 4000)]
        [InlineData("(?:a{10,}(?x: )){400}x", false, 4000)]
        [InlineData("(?:a{10,}(?:b{0})){400}x", false, 4000)]
        [InlineData("(?:a{10,}(?:(?:b){0}c{0})){400}x", false, 4000)]
        [InlineData("(?:a{10,}(?:(b){0})){400}x", false, 4000)]
        [InlineData("(?:a{10,}(?x: b{0} )){400}x", false, 4000)]
        [InlineData("(?:a{10,}(?-x:b{0})){400}x", true, 4000)]
        [InlineData("(?:a{10,}(?-x: )){400}x", true, 0)]
        [InlineData("(?:a{10,}(?-x:(?x: ) )){400}x", true, 0)]
        [InlineData("(?:a{10,}(?:b{0}c)){400}x", false, 0)]
        public void ALeadingRepeatIsReadAsDotNetPrefixesIt(string netPattern, bool verbose, int expected)
        {
            Assert.Equal(expected, RegexFootprint.LeadingRepeat(netPattern, verbose));
        }

        [Fact]
        public void ALeadingRepeatIsHeldAtItsLength()
        {
            Assert.Equal(RegexFootprint.Held("a{1500}x", 0) + 6 * 1500, RegexFootprint.Held("a{1500}x"));
            string cyrillic = (char)0x430 + "{1500}x";
            Assert.Equal(RegexFootprint.Held(cyrillic, 0) + 6 * 1500, RegexFootprint.Held(cyrillic));
        }

        // Read in one pass: a nest as deep as a pattern may be is walked once, not once a level.
        [Fact]
        public void ADeepNestIsReadThrough()
        {
            string deep = new string('(', 1) + string.Concat(System.Linq.Enumerable.Repeat("(?:", 3000)) + "a{7}"
                + new string(')', 3001);
            Assert.Equal(7, RegexFootprint.LeadingRepeat(deep, false));
            Assert.Equal(0, RegexFootprint.LeadingRepeat(string.Concat(System.Linq.Enumerable.Repeat("(?:^)", 3000)) + "(^)a{7}", false));
        }

        // .NET merges nested loops of one character into one: (?:(?:a{60,}){60,}){20} built a 72,000-character table in
        // one uninterruptible call (6 s). Past the longest literal a pattern can spell, the prefix is stopped.
        [Fact]
        public void AMergedPrefixPastTheLongestIsStopped()
        {
            var ctx = Rt.Ctx(TestEngines.Unhurried);
            // the stop is read off an upper bound over every atom: nothing .NET drops before it ({0}, (?:)) hides a merge
            foreach (string pattern in new[] { "(?:(?:a{60,}){60,}){20}", "(?:(?:(?:a{60,})){60,}){20}",
                "(?:(?:a{60,}(?:)){60,}){20}", "(?:(?:[aa]{60,}){60,}){20}", "(?:(?:a{60,}(?i:)){60,}){20}", "b{0}(?:(?:a{60,}){60,}){20}", "(xy){0}(?:(?:a{60,}){60,}){20}",
                "b{0}(?:(?:a{2,}){4000}){4000}" })
            {
                PatternValue p = new RegexCache().GetOrCompile(ctx, pattern, 0);
                Assert.Contains("(?>)", p.Plain(ctx, "x").ToString());
                Assert.Equal(0, RegexFootprint.LeadingRepeat(p.Plain(ctx, "x").ToString(), false));
            }
            PatternValue merged = new RegexCache().GetOrCompile(ctx, "(?:a{10,}){150}", 0);
            Assert.Equal(1500, RegexFootprint.LeadingRepeat(merged.Plain(ctx, "x").ToString(), false));
        }

        // Where the stop stands: after the anchors .NET starts from, before a quantified lookaround (not between it
        // and its quantifier), before the outermost group under a quantifier (inside one it kept .NET from merging
        // the loops), and nowhere in front of a literal nothing quantifies.
        [Theory]
        [InlineData("^(?:a{80}\n){60}END", "^(?>)(?:a{80}\n){60}END")]
        [InlineData("^(?:.{80}\n){60}END", "^(?:.{80}\n){60}END")]
        [InlineData("(?=a)?b{5000}", "(?>)(?=a)?b{5000}")]
        [InlineData("(?:a{10,}){500}b", "(?>)(?:a{10,}){500}b")]
        [InlineData("^((?:a{10,}){500})b", "^(?<p1>(?>)(?:a{10,}){500})b")]
        [InlineData("HDR(?:.{80}\n){60}", "HDR(?:.{80}\n){60}")]
        [InlineData("(?:){1}a{5000}", "(?:){1}(?>)a{5000}")]
        [InlineData("(?=a){1}^a{5000}", "(?=a){1}^(?>)a{5000}")]
        [InlineData("(?:key(?:x{100}){50}) +", "(?:key(?:x{100}){50}) +")]
        [InlineData("(?x:key(?:x{100}){50}) +", "(?x:key(?:x{100}){50}) +")]
        [InlineData(" total(?:x{100}){50}", " total(?:x{100}){50}")]
        [InlineData("(?x) k {5000}", " (?>)k {5000}")]
        public void TheStopStandsWhereItChangesNothing(string pattern, string expected)
        {
            var ctx = Rt.Ctx(TestEngines.Unhurried);
            Assert.Equal(expected, RegexTranslator.Translate(ctx, pattern, 0).NetPattern);
        }

        // Review 17: none before a set, which .NET makes no prefix of (in front of the exact \w the stop cost .NET its
        // first-character scan, 3-4x slower searches), nor before an anchor behind a {1} item ((?:){1}^ lost its anchor).
        [Theory]
        [InlineData(@"\w+@(?:(?:x{100}){50})?")]
        [InlineData(".(?:a{5000})?")]
        [InlineData(@"\d(?:a{5000})?")]
        [InlineData("(?:){1}^(?:.{80}\n){60}END")]
        public void NoStopStandsBeforeASet(string pattern)
        {
            var ctx = Rt.Ctx(TestEngines.Unhurried);
            RegexTranslator.Result r = RegexTranslator.Translate(ctx, pattern, 0);
            Assert.DoesNotContain("(?>)", r.NetPattern);
            Assert.DoesNotContain("(?>)", r.NativePattern ?? "");
        }

        // Review 19: a group's quantifier is read in the mode around it, and a subtraction class to its end. In
        // VERBOSE the stop stood between (?-x:(?=a)) and its ' ?', making the lookahead required; behind the exact
        // \w{0} no stop stood, and the merged loops built their table in one call that never returned.
        [Fact]
        public void TheStopReadsGroupsAsDotNetDoes()
        {
            var ctx = Rt.Ctx(TestEngines.Unhurried);
            Assert.Equal("(?>)(?-x:(?=a)) ?b{5000}", RegexTranslator.Translate(ctx, "(?-x:(?=a)) ?b{5000}", 64).NetPattern);
            Assert.Contains("(?>)", RegexTranslator.Translate(ctx, @"\w{0}(?:(?:a{60,}){60,}){20}", 0).NetPattern);
            RunResult r = Run("import re\nprint(re.match('(?-x:(?=a)) ?b{5000}', 'b' * 5000, re.X).span())\n"
                + "print(re.compile('\\\\w{0}(?:(?:a{60,}){60,}){20}').match(chr(0x1885) + 'a' * 10))\n");
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName + " " + r.Error.Message);
            Assert.Equal("(0, 5000)\nNone\n", r.PrintOutput.Replace("\r\n", "\n"));
        }

        // An earlier fix put the stop between a lookahead and its quantifier, and inside the group the loops merge through.
        [Fact]
        public void TheStopLeavesAnswersAndSpeedAlone()
        {
            RunResult r = Run("import re\nprint(re.match('(?=a)?b{5000}', 'b' * 5000).span())\n"
                + "print(re.search('(?:a{10,}){500}b', 'a' * 6000))\nprint(re.search('^(?:.{80}\\n){60}END', ('x' * 80 + '\\n') * 20000))\n"
                + "print(re.search('(?:){1}^(?:.{80}\\n){60}END', ('x' * 80 + '\\n') * 20000))\n");
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName + " " + r.Error.Message);
            Assert.Equal("(0, 5000)\nNone\nNone\nNone\n", r.PrintOutput.Replace("\r\n", "\n"));
        }

        // A leading \Z passed the prefix search unseen: 215 pages behind it held 229 KB a regex on 2 KB charged.
        [Fact]
        public void ThePagesBehindAZeroWidthStartAreHeld()
        {
            string run = "";
            for (int page = 1; page < 216; page++)
            {
                run += (char)(page << 8);
            }
            Assert.True(RegexFootprint.Held(@"\Z" + run) > 215 * 1024);
            Assert.True(RegexFootprint.Held("(?:" + run + "){1}x") > 215 * 1024);
            Assert.True(RegexFootprint.Held("(?:" + run + "){2}x") < 10 * 1024);
        }

        // Review 17: .NET merges a class of one, an item quantified {1} and a (?: or option group into the run, so
        // 215 pages spelled so kept 231 KB a regex charged 8-20 KB; a branch or a loop keeps a group out of it, and a
        // case other than the first ends it. Each row measured on .NET 4.8 (the pages of its Boyer-Moore prefix).
        [Theory]
        [InlineData("[X]", false, 215)]
        [InlineData("(?:X)", false, 215)]
        [InlineData("X{1}", false, 215)]
        [InlineData("(?:X){1}", false, 215)]
        [InlineData("X(?#c)", false, 215)]
        [InlineData("(?i:X)", false, 215)]
        [InlineData("(?i:X)", true, 215)]
        [InlineData("a(?i:X)", false, 0)]
        [InlineData("a(?i:X)", true, 215)]
        [InlineData("(?:X|y)", false, 0)]
        [InlineData("(?:X)+", false, 0)]
        [InlineData("(?:X(?:X|y))", false, 1)]
        [InlineData("X+", false, 1)]
        public void PagesSpelledAnyWayAreHeld(string unit, bool ignoreCase, int pages)
        {
            var wide = new System.Text.StringBuilder();
            var ascii = new System.Text.StringBuilder();
            for (int page = 1; page < 216; page++)
            {
                wide.Append(unit.Replace("X", ((char)(page << 8)).ToString()));
                ascii.Append(unit.Replace("X", "b"));
            }
            long held = RegexFootprint.Held(wide + "k", 0, ignoreCase) - RegexFootprint.Held(ascii + "k", 0, ignoreCase);
            Assert.Equal(pages == 0 ? 0 : 2048 + 1024L * pages, held);
        }

        // Review 18: in VERBOSE an indent before an anchor or a group ended the run the page charge read, and .NET
        // passes it: 215 pages kept 224 KB a regex charged 3 KB. (?x: and (?-x: on the way are followed.
        [Theory]
        [InlineData(" ^X", true, 215)]
        [InlineData("\n    (?:X)", true, 215)]
        [InlineData("\n    (?<w>X)  k", true, 215)]
        [InlineData("(?x: (?:X))", false, 215)]
        [InlineData("(?-x: (?:X))", true, 215)]
        [InlineData("(?x) (?:X)", false, 215)]
        [InlineData("(?x:X) {2}", false, 215)]
        public void PagesBehindVerboseSpaceAreHeld(string shape, bool verbose, int pages)
        {
            var wide = new System.Text.StringBuilder();
            var ascii = new System.Text.StringBuilder();
            for (int page = 1; page < 216; page++)
            {
                wide.Append((char)(page << 8));
                ascii.Append('b');
            }
            long held = RegexFootprint.Held(shape.Replace("X", wide.ToString()), 0, false, verbose)
                - RegexFootprint.Held(shape.Replace("X", ascii.ToString()), 0, false, verbose);
            Assert.Equal(pages == 0 ? 0 : 2048 + 1024L * pages, held);
        }

        // 2,000 kept a{800} held 12.6 MB that were charged 3.2 MB: an 8 MB limit is met now.
        [Fact]
        public void KeptLeadingRepeatsMeetTheLimit()
        {
            RunResult r = Run("import re\nkeep = []\nfor i in range(2000):\n    keep.append(re.compile('a{800}x%d' % i))\n"
                + "print(len(keep))\n", 8L << 20);
            Assert.False(r.Succeeded);
            Assert.Equal("MaxAllocBytes", r.Error.LimitName);
        }

        // a{4000} spent 22 ms building its table on 70 steps.
        [Fact]
        public void ALeadingRepeatPaysItsTableInSteps()
        {
            RunResult repeat = Run("import re\nre.compile('a{4000}x')\n");
            RunResult none = Run("import re\nre.compile('ba{4000}x')\n");
            Assert.True(repeat.Succeeded && none.Succeeded);
            Assert.True(repeat.Stats.Steps - none.Stats.Steps >= 4000L * 4000 / 512,
                "repeat " + repeat.Stats.Steps + ", none " + none.Stats.Steps);
        }

        // The exact translation's text is held with the pattern; the regex built on it shares it.
        [Fact]
        public void TheExactTranslationsTextIsHeldOnce()
        {
            var ctx = Rt.Ctx(TestEngines.Unhurried);
            PatternValue p = new RegexCache().GetOrCompile(ctx, @"\w+", 0);
            string native = p.Plain(ctx, "abc").ToString();
            string exact = p.Plain(ctx, "a" + (char)0x301).ToString();
            Assert.NotEqual(native, exact);
            Assert.Equal(RegexFootprint.Held(native) + RegexFootprint.Held(exact), p.HeldBytes);
        }
    }
}
