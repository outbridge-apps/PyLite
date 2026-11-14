using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // Pattern syntax .NET does not share with Python, rewritten by RegexTranslator (closed 2026-09-11):
    // possessive quantifiers, the open lower bound {,n}, and \N{NAME}.
    [Trait("Category", "Compatibility")]
    public class RePatternSyntaxTests
    {
        private static string R(string expr)
        {
            return Ev.ReprOf("import re\n", expr);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run("import re\n" + body + "\n"));
        }

        // A possessive quantifier (3.11) is an atomic group: it never gives back what it matched.
        [Theory]
        [InlineData(@"re.search(r'a*+b', 'aaab').group()", "'aaab'")]
        [InlineData(@"re.search(r'a*+a', 'aaa')", "None")]                  // nothing left to give back
        [InlineData(@"re.search(r'a++b', 'aaab').group()", "'aaab'")]
        [InlineData(@"re.search(r'a++', 'aaa').group()", "'aaa'")]
        [InlineData(@"re.search(r'a?+b', 'ab').group()", "'ab'")]
        [InlineData(@"re.match(r'a?+a', 'a')", "None")]                    // the one 'a' is not given back
        [InlineData(@"re.search(r'a{1,3}+b', 'aab').group()", "'aab'")]
        [InlineData(@"re.search(r'a{2}+b', 'aab').group()", "'aab'")]
        [InlineData(@"re.search(r'[ab]*+c', 'abac').group()", "'abac'")]
        [InlineData(@"re.search(r'[ab]*+a', 'aba')", "None")]
        [InlineData(@"re.search(r'(ab)*+c', 'ababc').group()", "'ababc'")]
        [InlineData(@"re.search(r'(ab)*+c', 'ababc').group(1)", "'ab'")]
        [InlineData(@"re.search(r'\d*+x', '123x').group()", "'123x'")]
        [InlineData(@"re.match(r'(?:ab|a)*+b', 'aba')", "None")]           // 'aba' is eaten whole, no 'b' left
        [InlineData(@"re.match(r'a*+a', 'aaa')", "None")]
        [InlineData(@"re.match(r'a*a', 'aaa').group()", "'aaa'")]          // greedy DOES give one back
        [InlineData(@"re.fullmatch(r'\w*+', 'abc').group()", "'abc'")]
        // the lazy form is spelled the same in .NET and must stay lazy
        [InlineData(@"re.search(r'a*?b', 'aaab').group()", "'aaab'")]
        [InlineData(@"re.findall(r'a+?', 'aaa')", "['a', 'a', 'a']")]
        [InlineData(@"re.match(r'<.*?>', '<a><b>').group()", "'<a>'")]
        public void PossessiveQuantifiers(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // {,n} means {0,n}; .NET would read the brace as a literal.
        [Theory]
        [InlineData(@"re.findall(r'a{,3}', 'aaaa')", "['aaa', 'a', '']")]
        [InlineData(@"re.match(r'a{,2}', 'aaa').group()", "'aa'")]
        [InlineData(@"re.match(r'a{,0}', 'aaa').group()", "''")]
        [InlineData(@"re.match(r'a{,}', 'aaa').group()", "'aaa'")]
        [InlineData(@"re.search(r'a{,3}+b', 'aab').group()", "'aab'")]
        // the forms .NET already understands are untouched
        [InlineData(@"re.match(r'a{2}', 'aaa').group()", "'aa'")]
        [InlineData(@"re.match(r'a{2,}', 'aaa').group()", "'aaa'")]
        [InlineData(@"re.match(r'a{2,3}', 'aaaa').group()", "'aaa'")]
        // a brace that is not a repeat stays a literal, in both engines
        [InlineData(@"re.findall(r'x{}', 'x{}')", "['x{}']")]
        [InlineData(@"re.findall(r'x{a}', 'x{a}')", "['x{a}']")]
        [InlineData(@"re.findall(r'x{1', 'x{1')", "['x{1']")]
        public void OpenLowerBound(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // A lower bound past 4096 puts an empty atomic group in front of the .NET pattern, so .NET builds no
        // Boyer-Moore table for an m-char prefix (quadratic: re.compile('a{100000}') ran ~30 s in one call).
        // The matches are those of the plain pattern.
        [Theory]
        [InlineData(@"re.search(r'a{100000}', 'x' + 'a' * 100000 + 'y').span()", "(1, 100001)")]
        [InlineData(@"re.search(r'(a{100000})y', 'x' + 'a' * 100001 + 'y').span(1)", "(2, 100002)")]
        [InlineData(@"re.fullmatch(r'^a{100000,}', 'a' * 100003).span()", "(0, 100003)")]
        [InlineData(@"re.match(r'\x61{5000}', 'a' * 4999)", "None")]
        [InlineData(@"re.fullmatch(r'(?x) a {100000} b', 'a' * 100000 + 'b') is not None", "True")]
        [InlineData(@"re.match(r'a{5000}?', 'a' * 6000).span()", "(0, 5000)")]
        [InlineData(@"re.match(r'a{5000}+a', 'a' * 5000)", "None")]
        [InlineData(@"re.search(r'\ba{5000}b|c', 'c ' + 'a' * 5000 + 'b').span()", "(0, 1)")]
        [InlineData(@"re.compile(r'a{100000}').pattern", "'a{100000}'")]
        public void LongLowerBoundBuildsNoPrefixTable(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void OnlyALongLowerBoundStopsThePrefix()
        {
            var ctx = Rt.Ctx();
            Assert.StartsWith("(?>)", Outbridge.PyLite.Modules.Support.RegexTranslator.Translate(ctx, "a{4097}", 0).NetPattern);
            // a literal nothing quantifies is the prefix .NET makes, never a long loop: it keeps its scan
            Assert.Equal("x|(?<p1>b{99999999999})", Outbridge.PyLite.Modules.Support.RegexTranslator.Translate(ctx, "x|(b{99999999999})", 0).NetPattern);
            // the stop stands where the prefix search meets the loop, after the anchor .NET starts from (review 16)
            Assert.Equal("^(?>)a{4097}", Outbridge.PyLite.Modules.Support.RegexTranslator.Translate(ctx, "^a{4097}", 0).NetPattern);
            Assert.Equal("a{4096}", Outbridge.PyLite.Modules.Support.RegexTranslator.Translate(ctx, "a{4096}", 0).NetPattern);
            Assert.Equal("a{0,100000}", Outbridge.PyLite.Modules.Support.RegexTranslator.Translate(ctx, "a{,100000}", 0).NetPattern);
        }

        // \N{NAME} in a pattern (3.8), off the same table the lexer uses for string literals.
        [Theory]
        [InlineData(@"ord(re.search(r'\N{DEGREE SIGN}', '5°').group())", "176")]
        [InlineData(@"[ord(c) for c in re.findall(r'[\N{DEGREE SIGN}x]', 'x°y')]", "[120, 176]")]
        [InlineData(@"re.findall(r'\N{FULL STOP}', 'a.b')", "['.']")]            // no metacharacter leaks
        [InlineData(@"re.findall(r'.', 'a.b')", "['a', '.', 'b']")]
        [InlineData(@"bool(re.search(r'\N{NUL}', '\x00'))", "True")]             // an alias
        [InlineData(@"ord(re.search(r'(?x) \N{DEGREE SIGN}  # deg', '5°').group())", "176")]
        [InlineData(@"re.search(r'\N{GRINNING FACE}', '\U0001F600').group() == '\U0001F600'", "True")]
        [InlineData(@"re.findall(r'[\N{GRINNING FACE}]', '\U0001F600') != []", "True")]
        [InlineData(@"re.search(r'\N{DEGREE SIGN}+', '5°°').group() == '°°'", "True")]
        public void NamedCharacterEscape(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData(@"re.compile(r'\N{NO SUCH NAME}')", "undefined character name 'NO SUCH NAME' at position 0")]
        [InlineData(@"re.compile(r'\N{DEGREE')", "missing }, unterminated name at position 3")]
        [InlineData(@"re.compile(r'\Nx')", "missing { at position 2")]                    // bare \N is still an error
        [InlineData(@"re.compile(r'[\Nx]')", "missing { at position 3")]
        public void NamedCharacterEscapeErrors(string expr, string message)
        {
            var ex = Raises("r = " + expr);
            Assert.Equal("PatternError", ex.Value.ExcType.Name);
            Assert.Equal(message, ex.Value.GetMessageText(Rt.Ctx()));
        }
    }
}
