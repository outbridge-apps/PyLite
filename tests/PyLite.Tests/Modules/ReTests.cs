using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/08 re module phase 1: RegexTranslator + Pattern/Match + cache + compile/search/match/fullmatch.
    [Trait("Category", "Compatibility")]
    public class ReTests
    {
        private static string R(string expr)
        {
            return Ev.ReprOf("import re\n", expr);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run("import re\n" + body + "\n"));
        }

        [Theory]
        // search / match / fullmatch
        [InlineData("re.search('b', 'abc').group()", "'b'")]
        [InlineData("re.search('x', 'abc')", "None")]
        [InlineData("re.match('ab', 'abc').group()", "'ab'")]
        [InlineData("re.match('bc', 'abc')", "None")]                    // match is anchored at pos 0
        [InlineData("re.fullmatch('abc', 'abc').group()", "'abc'")]
        [InlineData("re.fullmatch('ab', 'abc')", "None")]
        // groups
        [InlineData("re.search('(a)(b)', 'xab').group(1)", "'a'")]
        [InlineData("re.search('(a)(b)', 'xab').group(2)", "'b'")]
        [InlineData("re.search('(a)(b)', 'xab').group(0)", "'ab'")]
        [InlineData("re.search('(a)(b)', 'xab').group(1, 2)", "('a', 'b')")]
        [InlineData("re.search('(a)(b)', 'xab').groups()", "('a', 'b')")]
        [InlineData("re.search('(x)?y', 'y').group(1)", "None")]         // non-participating group -> None
        [InlineData("re.search('(x)?y', 'y').groups()", "(None,)")]
        [InlineData("re.search('(x)?y', 'y').groups('-')", "('-',)")]    // groups(default)
        // named groups
        [InlineData("re.search('(?P<first>a)(?P<second>b)', 'ab').group('first')", "'a'")]
        [InlineData("re.search('(?P<n>a)', 'a').groupdict()", "{'n': 'a'}")]
        [InlineData("re.search('(?P<a>x)(?P<b>y)', 'xy').groupdict()", "{'a': 'x', 'b': 'y'}")]
        [InlineData("re.search('(?P<w>a)(?P=w)', 'aa').group()", "'aa'")] // named backref
        // spans
        [InlineData("re.search('(a)(b)', 'xab').span()", "(1, 3)")]
        [InlineData("re.search('(a)(b)', 'xab').start()", "1")]
        [InlineData("re.search('(a)(b)', 'xab').end()", "3")]
        [InlineData("re.search('(a)(b)', 'xab').span(1)", "(1, 2)")]
        [InlineData("re.search('(x)?y', 'y').span(1)", "(-1, -1)")]      // non-participating -> (-1,-1)
        // Pattern properties
        [InlineData("re.compile('a').pattern", "'a'")]
        [InlineData("re.compile('a', re.I).flags", "34")]           // str patterns imply re.UNICODE (32)
        [InlineData("re.compile('(a)(b)').groups", "2")]
        [InlineData("re.compile('(?P<n>x)').groupindex", "{'n': 1}")]
        [InlineData("re.compile('ab')", "re.compile('ab')")]             // deterministic repr
        // flags + anchors + truthiness
        [InlineData("re.search('a', 'A', re.I).group()", "'A'")]         // IGNORECASE
        [InlineData("re.search('^b', 'a\\nb', re.M).group()", "'b'")]    // MULTILINE
        [InlineData("re.search('a.b', 'a\\nb', re.S).group()", "'a\\nb'")] // DOTALL
        [InlineData("re.search('a\\\\Z', 'ba').group()", "'a'")]         // \Z end anchor
        [InlineData("bool(re.search('a', 'abc'))", "True")]             // Match is truthy
        // Match back-links
        [InlineData("re.search('a', 'xa').string", "'xa'")]
        [InlineData("re.compile('a').search('xa').re.pattern", "'a'")]
        public void ReBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void CompileIsCached()
        {
            Assert.Equal("True", R("re.compile('abc') is re.compile('abc')"));
        }

        [Fact]
        public void CompileOfCompiledReturnsSame()
        {
            Assert.Equal("True", Ev.Run("import re\np = re.compile('a')\nr = (re.compile(p) is p)\n").Repr("r"));
        }

        [Fact]
        public void CompileCompiledWithFlagsIsValueError()
        {
            var ex = Raises("re.compile(re.compile('a'), re.I)");
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void BadPatternIsReError()
        {
            var ex = Raises("re.compile('[z-a]')");
            Assert.Equal("error", ex.Value.ExcType.Name);
        }

        [Fact]
        public void ReErrorIsSubclassOfException()
        {
            string src = "import re\ncaught = 0\ntry:\n    re.compile('(')\nexcept Exception:\n    caught = 1\n";
            Assert.Equal("1", Ev.Run(src).Repr("caught"));
        }

        [Fact]
        public void ReErrorCaughtByReError()
        {
            string src = "import re\ncaught = 0\ntry:\n    re.compile('[z-a]')\nexcept re.error:\n    caught = 1\n";
            Assert.Equal("1", Ev.Run(src).Repr("caught"));
        }

        [Fact]
        public void CompileNonStrIsTypeError()
        {
            Assert.Equal("TypeError", Raises("re.compile(123)").Value.ExcType.Name);
        }

        [Fact]
        public void NoSuchGroupIsIndexError()
        {
            Assert.Equal("IndexError", Raises("re.search('a', 'a').group('nope')").Value.ExcType.Name);
        }

        // ---- scoped inline flags and atomic groups (.NET takes both verbatim) ----

        [Theory]
        [InlineData("re.findall('(?i:ab)c', 'ABc abC')", "['ABc']")]
        [InlineData("re.match('a(?-i:b)', 'aB', re.I)", "None")]
        [InlineData("re.match('a(?-i:b)', 'ab', re.I).group()", "'ab'")]
        [InlineData("re.match('(?i:a)(b)', 'Ab').group(1)", "'b'")]          // scoped flags do not capture
        [InlineData("re.compile('(?i:a)(b)').groups", "1")]
        [InlineData("re.findall('(?s:.)', 'a\\nb')", "['a', '\\n', 'b']")]
        [InlineData("re.match('(?i-s:A.)', 'a\\n')", "None")]
        [InlineData("re.match('(?u:a)b', 'ab').group()", "'ab'")]
        [InlineData("re.match('(?>a+)b', 'aaab').group()", "'aaab'")]
        [InlineData("re.match('(?>a+)a', 'aaa')", "None")]                   // atomic: no backtracking
        public void ScopedFlagsAndAtomicGroups(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // ---- re.ASCII: the class escapes are rewritten to their ASCII forms ----

        [Theory]
        // The scripts spell non-ASCII input with \xNN / \uNNNN escapes so this file stays pure ASCII;
        // rows whose RESULT is non-ASCII compare inside the script instead of through repr.
        [InlineData("re.findall('\\\\w+', 'h\\xe9llo w\\xf6rld', re.A)", "['h', 'llo', 'w', 'rld']")]
        [InlineData("re.match('\\\\d+', '\\u0661\\u0662\\u0663', re.A)", "None")]
        [InlineData("re.match('\\\\d+', '\\u0661\\u0662\\u0663') is not None", "True")]
        [InlineData("re.sub('\\\\s', '_', 'a\\xa0b', flags=re.A) == 'a\\xa0b'", "True")]
        [InlineData("re.sub('\\\\s', '_', 'a\\xa0b')", "'a_b'")]
        [InlineData("re.findall('\\\\S+', 'a\\xa0b', re.A) == ['a\\xa0b']", "True")]
        [InlineData("re.findall('\\\\W+', 'a\\xe9b', re.A) == ['\\xe9']", "True")]
        [InlineData("re.findall('\\\\bfoo\\\\b', 'foo f\\xf6o', re.A)", "['foo']")]
        [InlineData("re.findall('\\\\Bx', 'ax x', re.A)", "['x']")]
        [InlineData("re.compile('(?a)\\\\w+').findall('ab \\xe9')", "['ab']")]           // inline (?a)
        [InlineData("re.findall('[\\\\w.-]+', 'a.b-c \\xe9', re.A)", "['a.b-c']")]
        [InlineData("re.findall('[^\\\\d]+', 'a1b', re.A)", "['a', 'b']")]
        [InlineData("re.findall('\\\\w+', 'A_b9 c', re.A | re.I)", "['A_b9', 'c']")]
        public void AsciiFlag(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // A negated escape inside [...] under re.A is rebuilt as a complement, and (?a:...) scopes the
        // ASCII rewrite to its group, both exactly.
        [Theory]
        [InlineData("re.findall('[\\\\D]+', 'ab1c\\u0663', re.A) == ['ab', 'c\\u0663']", "True")]   // \u0663 is not an ASCII digit
        [InlineData("re.findall('[\\\\D]+', 'ab1c\\u0663') == ['ab', 'c']", "True")]                // without re.A it is
        [InlineData("re.findall('[\\\\Dx]', '1x2', re.A)", "['x']")]
        [InlineData("re.findall('[^\\\\D]+', 'ab12c\\u0663', re.A)", "['12']")]
        [InlineData("re.findall('[^\\\\D1]+', 'ab12c', re.A)", "['2']")]
        [InlineData("re.findall('[\\\\W]', 'a\\xe9 b', re.A) == ['\\xe9', ' ']", "True")]
        [InlineData("re.findall('[\\\\S]+', 'a\\xa0b c', re.A) == ['a\\xa0b', 'c']", "True")]
        [InlineData("re.findall('[\\\\D\\\\S]', 'a1 ', re.A)", "['a', '1', ' ']")]                  // (not digit) or (not space)
        [InlineData("re.findall('(?a:\\\\w+)', 'ab \\xe9')", "['ab']")]
        [InlineData("re.findall('(?a:\\\\w+) \\\\w+', 'ab \\xe9') == ['ab \\xe9']", "True")]        // the scope ends with the group
        [InlineData("re.findall('(?ia:B+)\\\\w', 'bb\\xe9') == ['bb\\xe9']", "True")]              // the \w outside the scope stays Unicode
        [InlineData("re.compile('(?a:x)').flags", "32")]                                            // a scoped flag is not a pattern flag
        public void AsciiClassesAndScopes(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void AsciiCannotBeTurnedOff()
        {
            Assert.Equal("error", Raises("re.compile('(?i-a:x)')").Value.ExcType.Name);
        }

        // Pattern.flags follows CPython's fix_flags for a str pattern: UNICODE is implied unless ASCII
        // was asked for, and the two together are a contradiction. Inline flags are OR'd in afterwards.
        [Theory]
        [InlineData("re.compile('a').flags", "32")]
        [InlineData("re.compile('a', re.I).flags", "34")]
        [InlineData("re.compile('a', re.U).flags", "32")]
        [InlineData("re.compile('a', re.A).flags", "256")]
        [InlineData("re.compile('a', re.A | re.I).flags", "258")]
        [InlineData("re.compile('a', re.M | re.S | re.X).flags", "120")]
        [InlineData("re.compile('(?a)a').flags", "288")]     // implied UNICODE plus the inline ASCII
        [InlineData("re.compile('(?i)a').flags", "34")]
        public void PatternFlags(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("re.compile('x', re.A | re.U)", "ASCII and UNICODE flags are incompatible")]
        [InlineData("re.compile('x', re.L)", "cannot use LOCALE flag with a str pattern")]
        [InlineData("re.compile('(?L)x')", "cannot use LOCALE flag with a str pattern")]
        public void ContradictoryFlagsAreValueError(string body, string message)
        {
            // A bad flag COMBINATION is a plain ValueError in CPython, not re.error.
            ExceptionValue e = Raises("x = " + body).Value;
            Assert.Equal("ValueError", e.ExcType.Name);
            Assert.Equal(message, e.GetMessageText(Rt.Ctx()));
        }

        [Theory]
        [InlineData("re.compile('(?L:x)')")]
        [InlineData("re.compile('(?i-a:x)')")]
        [InlineData("re.compile('(?i-:x)')")]
        public void BadScopedFlagsRejected(string body)
        {
            Assert.Equal("error", Raises(body).Value.ExcType.Name);
        }

        // ---- findall / finditer / sub / subn / split / escape ----

        [Theory]
        // findall: 0 groups -> whole match; 1 group -> that group; N -> tuples; non-participating -> ''
        [InlineData("re.findall('a', 'aba')", "['a', 'a']")]
        [InlineData("re.findall('x', 'abc')", "[]")]
        [InlineData("re.findall('(a)', 'aa')", "['a', 'a']")]
        [InlineData("re.findall('(a)(b)', 'abab')", "[('a', 'b'), ('a', 'b')]")]
        [InlineData("re.findall('(a)|(b)', 'ab')", "[('a', ''), ('', 'b')]")]
        // finditer
        [InlineData("[m.group() for m in re.finditer('a', 'aba')]", "['a', 'a']")]
        [InlineData("len(list(re.finditer('', 'ab')))", "3")]
        // sub / subn (modern empty-match)
        [InlineData("re.sub('a', 'X', 'aba')", "'XbX'")]
        [InlineData("re.sub('x*', '-', 'abxd')", "'-a-b--d-'")]
        [InlineData("re.sub('a', 'X', 'aaa', 2)", "'XXa'")]
        [InlineData("re.sub('(a)', r'[\\1]', 'a')", "'[a]'")]
        [InlineData("re.sub('(?P<x>a)', r'[\\g<x>]', 'a')", "'[a]'")]
        [InlineData("re.sub('a', lambda m: m.group().upper(), 'aba')", "'AbA'")]
        [InlineData("re.subn('a', 'X', 'aa')", "('XX', 2)")]
        // split (modern: empty matches split; capturing groups included)
        [InlineData("re.split(',', 'a,b,c')", "['a', 'b', 'c']")]
        [InlineData("re.split('(,)', 'a,b')", "['a', ',', 'b']")]
        [InlineData("re.split('x*', 'abc')", "['', 'a', 'b', 'c', '']")]
        [InlineData("re.split(',', 'a,b,c', 1)", "['a', 'b,c']")]
        [InlineData("re.split(':', '')", "['']")]
        // Pattern methods
        [InlineData("re.compile('a').findall('aba')", "['a', 'a']")]
        [InlineData("re.compile('a').sub('X', 'aba')", "'XbX'")]
        [InlineData("re.compile(',').split('a,b')", "['a', 'b']")]
        public void ReScanBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        // escape — modern 3.7+ special set only (RFC-003 §4)
        [InlineData("re.escape('a.b') == 'a\\\\.b'")]
        [InlineData("re.escape('a+b') == 'a\\\\+b'")]
        [InlineData("re.escape('a b') == 'a\\\\ b'")]        // space is in the modern special set
        [InlineData("re.escape('a,b') == 'a,b'")]            // comma NOT escaped (modern; 3.4 escaped it)
        [InlineData("re.escape('abc_1') == 'abc_1'")]
        public void ReEscape(string boolExpr)
        {
            Assert.Equal("True", R(boolExpr));
        }

        [Fact]
        public void SubReplNonStrIsTypeError()
        {
            Assert.Equal("TypeError", Raises("re.sub('a', lambda m: 5, 'a')").Value.ExcType.Name);
        }

        [Fact]
        public void SubInvalidGroupRefIsReError()
        {
            Assert.Equal("error", Raises("re.sub('a', r'\\9', 'a')").Value.ExcType.Name);
        }

        // Catastrophic backtracking (a+)+$ on non-matching input -> the .NET MatchTimeout fires -> EngineAbort,
        // which is NOT a ScriptException (a bare except does NOT catch it) -> Ev.Run propagates it. ~250 ms.
        [Fact]
        public void CatastrophicBacktrackingHitsTimeout()
        {
            string src = "import re\ncaught = 0\ntry:\n    re.match('(a+)+$', 'a' * 40 + 'b')\nexcept:\n    caught = 1\n";
            Assert.Throws<EngineAbort>(() => Ev.Run(src));
        }

        // expand() runs sub's template machinery over one match; lastindex/lastgroup name the group that
        // closed last, which is the successful group ending furthest right and the outer one on a tie.
        [Theory]
        [InlineData("re.match(r'(a)(b)', 'ab').expand(r'\\2\\1')", "'ba'")]
        [InlineData("re.match(r'(?P<x>a)', 'a').expand(r'<\\g<x>>')", "'<a>'")]
        [InlineData("re.match(r'ab', 'ab').expand(r'[\\g<0>]')", "'[ab]'")]
        [InlineData("re.match(r'(a)', 'a').expand('no groups')", "'no groups'")]
        [InlineData("re.match(r'(a)(b)?', 'a').expand(r'[\\2]')", "'[]'")]
        [InlineData("re.match(r'(a)(b)', 'ab').lastindex", "2")]
        [InlineData("re.match(r'((a)(b))', 'ab').lastindex", "1")]
        [InlineData("re.match(r'(a)|(b)', 'b').lastindex", "2")]
        [InlineData("re.match(r'a', 'a').lastindex", "None")]
        [InlineData("re.match(r'(a)(b)', 'ab').lastgroup", "None")]
        [InlineData("re.match(r'(?P<n>a)', 'a').lastgroup", "'n'")]
        [InlineData("re.match(r'a', 'a').lastgroup", "None")]
        public void ExpandAndLastGroup(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void ExpandRejectsNonString()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("import re\nr = re.match('a', 'a').expand(1)\n"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }
    }
}
