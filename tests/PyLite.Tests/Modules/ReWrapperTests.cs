using System;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Modules;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // P3 (1.1.0): group reads by .NET group number, match() on an anchored twin, sub templates written
    // straight into the result. The limits and the timeout must stay exactly as strong.
    [Trait("Category", "Compatibility")]
    public class ReWrapperTests
    {
        private static string R(string src)
        {
            return Ev.Run("import re\n" + src).Repr("r");
        }

        private static RunResult Run(string src, ResourceLimits limits)
        {
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = limits });
            return engine.Run("import re\n" + src, new RunRequest());
        }

        [Theory]
        [InlineData("r = re.compile(r'(?<=a)b').match('ab', 1) is not None", "True")]   // pos is not a string edge
        [InlineData("r = re.compile('^b').match('ab', 1)", "None")]
        [InlineData("r = re.compile(r'\\bb').match('ab', 1)", "None")]
        [InlineData("r = re.compile('b').match('ab')", "None")]
        [InlineData("r = re.compile('a|ab').match('ab').group()", "'a'")]
        [InlineData("r = re.compile('b').match('abc', 1, 2).span()", "(1, 2)")]
        [InlineData("r = re.compile('bc').match('abc', 1, 2)", "None")]
        public void MatchIsAnchoredAtPosLikeCPython(string src, string expected)
        {
            Assert.Equal(expected, R(src));
        }

        // Module functions and Pattern methods share one argument binder (D12): an extra positional is refused.
        [Theory]
        [InlineData("re.match('a', 'a', 0, 1)", "match() takes at most 3 arguments (4 given)")]
        [InlineData("re.compile('a').search('a', 0, 1, 2)", "search() takes at most 3 arguments (4 given)")]
        [InlineData("re.sub('a', 'b', 'a', 0, 0, 1)", "sub() takes at most 5 arguments (6 given)")]
        [InlineData("re.compile('a').split('a', 0, 1)", "split() takes at most 2 arguments (3 given)")]
        public void ExtraPositionalIsRefused(string call, string message)
        {
            Assert.Equal(message, Ev.Err("import re\n" + call + "\n"));
        }

        [Fact]
        public void GroupsResolveByNumberAndNameOnEveryEntry()
        {
            Assert.Equal("(('x', 'y', 'z'), ('x', 'z'), ('a', None, 'a'), ('b', 'b'), ['1-x', '2-y'], ['x', 'y', 'z'], 'z|y|x', (1, 2))",
                R("p = re.compile(r'(?P<a>x)(y)(?P<b>z)')\nm = p.match('xyz')\nf = re.fullmatch(r'(a)(?P<n>b)?', 'a')\n"
                + "g = re.search(r'(?P<w>b)', 'ab')\n"
                + "r = (m.group(1, 2, 3), (m['a'], m.group('b')), (f.group(1), f.group(2), f.groups()[0]), (g.group('w'), g.groupdict()['w']),\n"
                + "     re.findall(r'(\\d)(\\w)', '1x 2y'.replace(' ', '')) and [a + '-' + b for a, b in re.findall(r'(\\d)(\\w)', '1x2y')],\n"
                + "     re.split(r'(\\w)', 'xyz')[1::2], p.sub(r'\\g<b>|\\2|\\g<a>', 'xyz'), m.span(2))\n"));
        }

        [Fact]
        public void SubOnAMegabyteStopsOnMaxStrChars()
        {
            RunResult r = Run("s = re.sub('a', 'aaa', 'a' * 1000000)\n", ResourceLimits.Default.With(maxStrChars: 2_000_000, deadlineMs: 60_000));
            Assert.Equal("MaxStrChars", r.Error.LimitName);
            RunResult g = Run("s = re.sub('(a)', r'\\1\\1\\1', 'a' * 1000000)\n", ResourceLimits.Default.With(maxStrChars: 2_000_000, deadlineMs: 60_000));
            Assert.Equal("MaxStrChars", g.Error.LimitName);
        }

        [Fact]
        public void FindallOnAMegabyteStopsOnMaxCollectionItems()
        {
            RunResult r = Run("x = re.findall('a', 'a' * 1000000)\n", ResourceLimits.Default.With(maxCollectionItems: 500_000, deadlineMs: 60_000));
            Assert.Equal("MaxCollectionItems", r.Error.LimitName);
        }

        [Theory]
        [InlineData("re.match('(a+)+$', 'a' * 40 + 'b')\n")]
        [InlineData("re.compile('(a+)+$').match('a' * 40 + 'b')\n")]
        [InlineData("re.sub('(a+)+$', 'x', 'a' * 40 + 'b')\n")]
        [InlineData("re.findall('(a+)+$', 'a' * 40 + 'b')\n")]
        [InlineData("re.fullmatch('(a+)+', 'a' * 40 + 'b')\n")]
        public void ACatastrophicPatternStillEndsOnTheRegexTimeout(string src)
        {
            RunResult r = Run("try:\n    " + src.TrimEnd('\n') + "\nexcept Exception:\n    pass\n", ResourceLimits.Default);
            Assert.False(r.Succeeded);
            Assert.Equal("RegexPerOpTimeout", r.Error.LimitName);
        }

        // The per-operation timeout is the run's RegexPerOpTimeoutMs, not a fixed 250 ms.
        [Theory]
        [InlineData(40)]
        [InlineData(5000)]
        public void ThePatternTimeoutIsTheRunsLimit(int ms)
        {
            var ctx = Rt.Ctx(ResourceLimits.Default.With(regexPerOpTimeoutMs: ms));
            PatternValue p = new RegexCache().GetOrCompile(ctx, "(a+)+$", 0);
            Assert.Equal(TimeSpan.FromMilliseconds(ms), p.Net.MatchTimeout);
        }
    }
}
