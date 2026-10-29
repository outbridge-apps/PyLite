using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // urllib.parse: quoting, query strings, the split/parse/join family and the result namedtuples.
    [Trait("Category", "Compatibility")]
    public class UrllibParseTests
    {
        private const string Imp = "import urllib.parse\nfrom urllib.parse import quote, quote_plus, unquote, unquote_plus, urlencode, parse_qs, parse_qsl, "
            + "urlparse, urlsplit, urlunparse, urlunsplit, urljoin, urldefrag, ParseResult, SplitResult\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(Imp, expr);
        }

        [Theory]
        // quote / unquote
        [InlineData("quote('a b/c?d=\\u00e9')", "'a%20b/c%3Fd%3D%C3%A9'")]
        // quote defaults safe to '/', quote_plus to '': a path separator is not safe in a query value
        [InlineData("quote_plus('a b/c?d=\\u00e9')", "'a+b%2Fc%3Fd%3D%C3%A9'")]
        [InlineData("(quote('a/b'), quote_plus('a/b'), quote_plus('a/b', safe='/'))", "('a/b', 'a%2Fb', 'a/b')")]
        [InlineData("(quote('a b', safe=''), quote('a/b', safe=''), quote('/a:b', safe='/:'), quote('~-_.'))", "('a%20b', 'a%2Fb', '/a:b', '~-_.')")]
        [InlineData("(quote(b'\\xff x'), quote('\\u00e9', encoding='latin-1'), quote_plus('a b', safe='b'))", "('%FF%20x', '%E9', 'a+b')")]
        [InlineData("unquote('a%20b%2Fc%C3%A9%') == 'a b/c\\u00e9%'", "True")]
        [InlineData("(unquote_plus('a+b%2B'), unquote('%zz%41'), unquote('no escapes'), unquote('%E9', encoding='latin-1') == '\\u00e9')", "('a b+', '%zzA', 'no escapes', True)")]
        [InlineData("unquote('%E9') == '\\ufffd'", "True")]   // errors='replace' by default
        // urlencode / parse_qs
        [InlineData("urlencode({'q': 'a b', 'n': 1, 'x': None, 'e': '\\u00e9'})", "'q=a+b&n=1&x=None&e=%C3%A9'")]
        [InlineData("(urlencode([('k', 'v w'), ('k', 2)]), urlencode({'l': [1, 2]}, doseq=True), urlencode({'l': [1, 2]}), urlencode({}))", "('k=v+w&k=2', 'l=1&l=2', 'l=%5B1%2C+2%5D', '')")]
        [InlineData("(urlencode({'a b': '/'}, safe='/'), urlencode({'a b': 'c d'}, quote_via=quote))", "('a+b=/', 'a%20b=c%20d')")]
        [InlineData("parse_qs('a=1&a=2&b=x+y&c=')", "{'a': ['1', '2'], 'b': ['x y']}")]
        [InlineData("(parse_qs('a=1&c=', keep_blank_values=True), parse_qsl('a=1&b=%20', keep_blank_values=True), parse_qsl(''), parse_qsl('a=1;b=2', separator=';'))", "({'a': ['1'], 'c': ['']}, [('a', '1'), ('b', ' ')], [], [('a', '1'), ('b', '2')])")]
        [InlineData("parse_qsl('a=1&b')", "[('a', '1')]")]
        // urlparse / urlsplit and the result objects
        [InlineData("tuple(urlparse('https://user:pw@Example.com:8080/p/a;par?q=1&r=2#frag'))", "('https', 'user:pw@Example.com:8080', '/p/a', 'par', 'q=1&r=2', 'frag')")]
        [InlineData("(lambda u: (u.hostname, u.port, u.username, u.password, u.geturl()))(urlparse('https://user:pw@Example.com:8080/p/a;par?q=1&r=2#frag'))", "('example.com', 8080, 'user', 'pw', 'https://user:pw@Example.com:8080/p/a;par?q=1&r=2#frag')")]
        [InlineData("(lambda u: (u[0], len(u), u == ('http', 'h', '/p', '', '', ''), isinstance(u, tuple), isinstance(u, ParseResult), u._replace(scheme='https').geturl(), u._asdict()['netloc']))(urlparse('http://h/p'))", "('http', 6, True, True, True, 'https://h/p', 'h')")]
        [InlineData("repr(urlparse('http://h/p'))", "\"ParseResult(scheme='http', netloc='h', path='/p', params='', query='', fragment='')\"")]
        [InlineData("repr(urlsplit('http://h/p;x?y'))", "\"SplitResult(scheme='http', netloc='h', path='/p;x', query='y', fragment='')\"")]
        [InlineData("(lambda u: (u.hostname, u.port, u.username, u.password))(urlsplit('http://[::1]:80/'))", "('::1', 80, None, None)")]
        [InlineData("(urlparse('/a/b?c').path, urlparse('www.x.com/a').netloc, urlparse('http://h').port, urlparse('mailto:a@b').path, urlparse('HTTP://H/').scheme, urlparse('//h/p').netloc)", "('/a/b', '', None, 'a@b', 'http', 'h')")]
        [InlineData("(urlparse('http://h/p#f', allow_fragments=False).path, urlparse('p', scheme='ftp').scheme, urlparse(' http://h/p\\n').netloc)", "('/p#f', 'ftp', 'h')")]
        [InlineData("(urlparse('http://user@h/').username, urlparse('http://user@h/').password, urlparse('http://h/').hostname)", "('user', None, 'h')")]
        [InlineData("scheme, netloc, path, params, query, fragment = urlparse('s://n/p;x?q#f')\nr = (scheme, fragment)", "('s', 'f')")]
        // unparse / join / defrag
        [InlineData("(urlunparse(('https', 'h', '/p', 'x', 'q', 'f')), urlunsplit(('', '', 'p', '', '')), urlunsplit(('http', 'h', 'p', '', '')), urlunsplit(['http', 'h', '/p', 'a=1', '']))", "('https://h/p;x?q#f', 'p', 'http://h/p', 'http://h/p?a=1')")]
        [InlineData("[urljoin('http://a/b/c/d;p?q', u) for u in ['g', './g', 'g/', '/g', '//g', '?y', 'g?y', '#s', 'g#s', ';x', 'g;x', '', '.', './', '..', '../', '../g', '../..', '../../g']]",
            "['http://a/b/c/g', 'http://a/b/c/g', 'http://a/b/c/g/', 'http://a/g', 'http://g', 'http://a/b/c/d;p?y', 'http://a/b/c/g?y', 'http://a/b/c/d;p?q#s', 'http://a/b/c/g#s', 'http://a/b/c/;x', 'http://a/b/c/g;x', 'http://a/b/c/d;p?q', 'http://a/b/c/', 'http://a/b/c/', 'http://a/b/', 'http://a/b/', 'http://a/b/g', 'http://a/', 'http://a/g']")]
        [InlineData("(urljoin('http://a/b/c/d', '../../../g'), urljoin('http://a/b', 'https://x/y'), urljoin('', 'x'), urljoin('http://a/b', ''), urljoin('http://a/b/', 'c/./d/../e'))", "('http://a/g', 'https://x/y', 'x', 'http://a/b', 'http://a/b/c/e')")]
        [InlineData("(urldefrag('http://h/p#frag'), urldefrag('http://h/p').fragment, urldefrag('http://h/p#f').geturl(), urldefrag('http://h/p#f').url)", "(DefragResult(url='http://h/p', fragment='frag'), '', 'http://h/p#f', 'http://h/p')")]
        [InlineData("ParseResult('http', 'h', '/p', '', '', '').geturl()", "'http://h/p'")]
        [InlineData("SplitResult(scheme='http', netloc='h', path='/', query='', fragment='')._fields", "('scheme', 'netloc', 'path', 'query', 'fragment')")]
        public void Surface(string expr, string expected)
        {
            string src = expr.Contains("\n") ? Imp + expr + "\n" : Imp + "r = (" + expr + ")\n";
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("urlparse('http://h:abc/').port", "ValueError", "Port could not be cast to integer value as 'abc'")]
        [InlineData("urlparse('http://h:70000/').port", "ValueError", "Port out of range 0-65535")]
        [InlineData("urlparse('http://[::1/')", "ValueError", "Invalid IPv6 URL")]
        [InlineData("urlparse(1)", "TypeError", "urlparse() argument 'url' must be str, not int")]
        [InlineData("parse_qsl('a=1&b', strict_parsing=True)", "ValueError", "bad query field: 'b'")]
        [InlineData("parse_qs('a=1&b=2&c=3', max_num_fields=2)", "ValueError", "Max number of fields exceeded")]
        [InlineData("urlencode(1)", "TypeError", "not a valid non-string sequence or mapping object")]
        [InlineData("urlencode([('a', 1, 2)])", "TypeError", "not a valid non-string sequence or mapping object")]
        [InlineData("urlunsplit(('a', 'b'))", "ValueError", "urlunsplit() takes a 5-item sequence (2 given)")]
        [InlineData("quote('\\u00e9', encoding='ascii')", "UnicodeEncodeError", "'ascii' codec can't encode character '\\xe9' in position 0: ordinal not in range(128)")]
        [InlineData("SplitResult('http', 1, '', '', '').geturl()", "TypeError", "SplitResult field 'netloc' must be str, not int")]
        public void Errors(string expr, string exc, string message)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + "r = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
            Assert.Equal(message, ex.Value.GetMessageText(Rt.Ctx()));
        }

        // An engine class is named with its module for its repr; __name__ and __qualname__ are its own name (N2).
        [Fact]
        public void ResultClassNamesAsCPython()
        {
            Assert.Equal("('ParseResult', 'ParseResult', \"<class 'urllib.parse.ParseResult'>\")",
                R("type(urlparse('http://x')).__name__, type(urlparse('http://x')).__qualname__, repr(type(urlparse('http://x')))"));
        }
    }
}
