using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Builtins
{
    // impl/06 T-BLT-14/15/16/17: str methods (search/split/case/predicates).
    [Trait("Category", "Compatibility")]
    public class BuiltinsStrMethodsTests
    {
        private static string R(string expr)
        {
            return Ev.ReprOf(expr);
        }

        // ---- search / replace (T-BLT-14) ----

        [Theory]
        [InlineData("'hello'.find('l')", "2")]
        [InlineData("'hello'.rfind('l')", "3")]
        [InlineData("'hello'.find('x')", "-1")]
        [InlineData("'abcabc'.find('b', 2)", "4")]
        [InlineData("'abcabc'.find('b', 2, 4)", "-1")]
        [InlineData("'abc'.find('', 2)", "2")]
        [InlineData("'abc'.find('', 4)", "-1")]
        [InlineData("'aaaa'.count('aa')", "2")]
        [InlineData("'abc'.count('', 1, 2)", "2")]
        [InlineData("'abc'.startswith(('x', 'ab'))", "True")]
        [InlineData("'abc'.endswith('bc', 0, 2)", "False")]
        [InlineData("'abc'.endswith('ab', 0, 2)", "True")]
        [InlineData("'ab'.replace('', '-')", "'-a-b-'")]
        [InlineData("'aaa'.replace('a', 'b', 2)", "'bba'")]
        [InlineData("'abc'.replace('x', 'y')", "'abc'")]
        public void Search(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("'hello'.index('x')", "substring not found")]
        [InlineData("'abc'.startswith(('x', 1))", "tuple for startswith must only contain str, not int")]
        [InlineData("'hello'.find(1)", "must be str, not int")]
        public void SearchErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        // ---- split / join / strip (T-BLT-15) ----

        [Theory]
        [InlineData("'a,,b'.split(',')", "['a', '', 'b']")]
        [InlineData("' a b  c '.split()", "['a', 'b', 'c']")]
        [InlineData("' a b  c '.split(None, 1)", "['a', 'b  c ']")]
        [InlineData("' a b  c '.rsplit(None, 1)", "[' a b', 'c']")]
        [InlineData("'a,b,c'.rsplit(',', 1)", "['a,b', 'c']")]
        [InlineData("'a\\x1cb\\x85c d'.splitlines()", "['a', 'b', 'c d']")]
        [InlineData("''.splitlines()", "[]")]
        [InlineData("'-'.join(['a', 'b', 'c'])", "'a-b-c'")]
        [InlineData("'  xy  '.strip()", "'xy'")]
        [InlineData("'xxyxx'.strip('x')", "'y'")]
        [InlineData("'abc'.lstrip('ba')", "'c'")]
        [InlineData("'www.ex.com'.rpartition('.')", "('www.ex', '.', 'com')")]
        [InlineData("'x'.partition('y')", "('x', '', '')")]
        public void Split(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void SplitlinesKeepends()
        {
            Assert.Equal("['a\\r\\n', 'b\\n']", R("'a\\r\\nb\\n'.splitlines(True)"));
        }

        [Theory]
        [InlineData("'ab'.split('')", "empty separator")]
        [InlineData("','.join(['a', 1])", "sequence item 1: expected str instance, int found")]
        [InlineData("','.join(5)", "can only join an iterable")]
        public void SplitErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        // ---- case / alignment (T-BLT-16) ----

        [Theory]
        [InlineData("'AbC'.upper()", "'ABC'")]
        [InlineData("'AbC'.lower()", "'abc'")]
        [InlineData("'hello world'.title()", "'Hello World'")]
        [InlineData("\"they're bill's\".title()", "\"They'Re Bill'S\"")]
        [InlineData("'Hello World'.istitle()", "True")]
        [InlineData("'Hello world'.istitle()", "False")]
        [InlineData("'A1a'.istitle()", "False")]
        [InlineData("'A1A'.istitle()", "True")]
        [InlineData("'AbC'.swapcase()", "'aBc'")]
        [InlineData("'hELLO'.capitalize()", "'Hello'")]
        [InlineData("'-42'.zfill(5)", "'-0042'")]
        [InlineData("'42'.zfill(5)", "'00042'")]
        [InlineData("'+1'.zfill(4)", "'+001'")]
        [InlineData("'ab'.ljust(5, '*')", "'ab***'")]
        [InlineData("'ab'.rjust(5, '*')", "'***ab'")]
        [InlineData("'ab'.center(5)", "'  ab '")]
        [InlineData("'abc'.center(6)", "' abc  '")]
        [InlineData("'a'.center(4)", "' a  '")]
        [InlineData("'1\\t23\\t4'.expandtabs(4)", "'1   23  4'")]
        [InlineData("'a\\tb'.expandtabs(0)", "'ab'")]
        [InlineData("'ab\\n\\tc'.expandtabs(4)", "'ab\\n    c'")]
        [InlineData("b'ab\\r\\tc'.expandtabs(2)", "b'ab\\r  c'")]
        public void Case(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // The result is charged once: charging the running length per tab cost tabs^2 and a 160,000-char
        // result from 20,000 tabs used to exceed the allocation limit (B5).
        [Fact]
        public void ExpandTabsChargesTheResultOnce()
        {
            var r = Ev.Run("r = (len(('\\t' * 20000).expandtabs()), len((b'\\t' * 20000).expandtabs()))\n");
            Assert.Equal("(160000, 160000)", r.Repr("r"));
        }

        // Full Unicode case mapping (PyUnicodeCase, generated tables). Non-ASCII is written with escapes
        // so this file stays pure ASCII.
        [Theory]
        // length-changing mappings, which the BCL has none of
        [InlineData("'\\xdf'.upper()", "'SS'")]                             // sharp s
        [InlineData("'\\ufb01le'.upper()", "'FILE'")]                       // ligature fi
        [InlineData("'\\u0149'.upper() == '\\u02bcN'", "True")]             // n preceded by apostrophe
        [InlineData("'\\u1fb3'.upper() == '\\u0391\\u0399'", "True")]       // alpha with ypogegrammeni
        [InlineData("len('\\xdf'.upper())", "2")]
        // casefold is its own mapping, not lower()
        [InlineData("'stra\\xdfe'.casefold()", "'strasse'")]
        [InlineData("'Stra\\xdfe'.casefold() == 'STRASSE'.casefold()", "True")]
        [InlineData("'\\xb5'.casefold() == '\\u03bc'.casefold()", "True")]  // micro sign onto mu
        [InlineData("'\\u017f'.casefold() == 's'.casefold()", "True")]      // long s
        [InlineData("'\\u212a'.casefold() == 'k'.casefold()", "True")]      // kelvin sign
        // final sigma: the one context rule CPython applies by default
        [InlineData("'\\u039f\\u03a3\\u039f\\u03a3'.lower() == '\\u03bf\\u03c3\\u03bf\\u03c2'", "True")]
        [InlineData("'\\u03a3\\u03a9'.lower() == '\\u03c3\\u03c9'", "True")]   // not final: a cased letter follows
        [InlineData("'\\u03a3'.lower() == '\\u03c3'", "True")]                 // not final: none precedes
        // mappings .NET's tables do not carry at all
        [InlineData("'\\u1e9e'.lower() == '\\xdf'", "True")]                // capital sharp s
        [InlineData("'\\u0131'.upper()", "'I'")]                            // dotless i
        [InlineData("'\\u017f'.upper()", "'S'")]                            // long s
        [InlineData("'\\u03f4'.lower() == '\\u03b8'", "True")]              // theta symbol
        [InlineData("'\\u0524'.lower() == '\\u0525'", "True")]              // Cyrillic supplement
        [InlineData("'\\u10d0'.upper() == '\\u1c90'", "True")]              // Georgian Mkhedruli -> Mtavruli
        [InlineData("'\\u13a0'.lower() == '\\uab70'", "True")]              // Cherokee
        // above the BMP: a surrogate pair maps as one code point
        [InlineData("'\\U00010428'.upper() == '\\U00010400'", "True")]
        // title / capitalize / swapcase take the TITLE mapping
        [InlineData("'\\u01f3'.title() == '\\u01f2'", "True")]
        [InlineData("'\\u01f3'.upper() == '\\u01f1'", "True")]
        [InlineData("'\\xdfa'.capitalize()", "'Ssa'")]
        [InlineData("'a\\xdf'.swapcase()", "'ASS'")]
        [InlineData("'\\u01f2'.swapcase() == '\\u01f2'", "True")]           // titlecase is neither
        [InlineData("'\\u01f2'.istitle()", "True")]
        [InlineData("'\\u01f2'.isupper()", "False")]
        // the ordinary alphabets stay exactly as they were
        [InlineData("'\\u043f\\u0440\\u0438\\u0432\\u0435\\u0442'.upper() == '\\u041f\\u0420\\u0418\\u0412\\u0415\\u0422'", "True")]
        [InlineData("'Ti\\u1ebfng Vi\\u1ec7t'.upper() == 'TI\\u1ebeNG VI\\u1ec6T'", "True")]
        [InlineData("'Invoice 4711'.upper()", "'INVOICE 4711'")]
        public void UnicodeCaseMapping(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // PEP 3131 exactly (XID_Start / XID_Continue), not a general-category approximation.
        [Theory]
        [InlineData("'abc'.isidentifier()", "True")]
        [InlineData("'_x'.isidentifier()", "True")]
        [InlineData("'_'.isidentifier()", "True")]
        [InlineData("'a1'.isidentifier()", "True")]
        [InlineData("'1a'.isidentifier()", "False")]
        [InlineData("''.isidentifier()", "False")]
        [InlineData("'a b'.isidentifier()", "False")]
        [InlineData("'a-b'.isidentifier()", "False")]
        [InlineData("'a$'.isidentifier()", "False")]
        // Other_ID_Continue / Other_ID_Start: in the property, outside any letter category
        [InlineData("'a\\xb7'.isidentifier()", "True")]                 // middle dot
        [InlineData("'a\\u0387'.isidentifier()", "True")]               // greek ano teleia
        [InlineData("'\\u2118'.isidentifier()", "True")]                // script capital P
        [InlineData("'\\u212e'.isidentifier()", "True")]                // estimated symbol
        // in a letter-ish category but NOT in the property
        [InlineData("'a\\xb2'.isidentifier()", "False")]                // superscript two
        [InlineData("'\\u24b6'.isidentifier()", "False")]               // circled latin A
        [InlineData("'\\U0001f600'.isidentifier()", "False")]           // emoji
        [InlineData("'\\u2160'.isidentifier()", "True")]                // roman numeral one (Nl)
        [InlineData("'\\u0301a'.isidentifier()", "False")]              // a combining mark cannot start
        // walks code POINTS, so an astral letter counts once
        [InlineData("'\\U0001d54f'.isidentifier()", "True")]
        [InlineData("'a\\U0001d54f'.isidentifier()", "True")]
        public void IsIdentifierIsExactXid(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("'x'.ljust(5, 'ab')", "The fill character must be exactly one character long")]
        [InlineData("'x'.ljust(5, 1)", "The fill character must be a unicode character, not int")]
        public void CaseErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        // ---- is* predicates (T-BLT-17) ----

        [Theory]
        [InlineData("'123'.isdecimal()", "True")]
        [InlineData("'123'.isdigit()", "True")]
        [InlineData("'123'.isnumeric()", "True")]
        [InlineData("''.isdigit()", "False")]
        [InlineData("'abc'.isalpha()", "True")]
        [InlineData("'ab1'.isalpha()", "False")]
        [InlineData("'ab1'.isalnum()", "True")]
        [InlineData("'ab '.isalnum()", "False")]
        [InlineData("'ABC1'.isupper()", "True")]
        [InlineData("'123'.isupper()", "False")]
        [InlineData("'Abc'.islower()", "False")]
        [InlineData("'abc1'.islower()", "True")]
        [InlineData("'\\xb2'.isdigit()", "True")]
        [InlineData("'\\xb2'.isdecimal()", "False")]
        public void Predicates(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void MethodsVisibleViaDir()
        {
            var r = Ev.Run("d = dir('x')\nhas = 'split' in d\nenc = 'encode' in d\nfm = 'format_map' in d\n");
            Assert.Equal("True", r.Repr("has"));
            Assert.Equal("True", r.Repr("enc"));   // RFC-001 §2 str.encode
            Assert.Equal("True", r.Repr("fm"));    // P2 gap-closure str.format_map
        }
    }
}
