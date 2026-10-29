# Adapted from CPython's test suite (Lib/test/test_urlparse.py: UrlParseTestCase.test_roundtrips /
# test_RFC3986 / test_urldefrag / test_unquoting / test_quoting / test_urlencode / test_parse_qsl,
# the urljoin cases from RFC 3986 section 5.4).
# Python Software Foundation, PSF License Version 2 -- see README.
from urllib.parse import quote, quote_plus, unquote, unquote_plus, urlencode, parse_qs, parse_qsl
from urllib.parse import urlparse, urlsplit, urlunparse, urlunsplit, urljoin, urldefrag, ParseResult, SplitResult

# quoting: the always-safe set, the safe argument, utf-8 by default, '+' only from quote_plus
assert quote('abc def') == 'abc%20def' and quote('/~user/a-b_c.d') == '/~user/a-b_c.d'
assert quote('a b/c?d=é') == 'a%20b/c%3Fd%3D%C3%A9' and quote_plus('a b/c?d=é') == 'a+b%2Fc%3Fd%3D%C3%A9'
assert quote('a/b', safe='') == 'a%2Fb' and quote(':/?#', safe=':/?#') == ':/?#'
assert quote_plus('a/b') == 'a%2Fb' and quote_plus('a/b', safe='/') == 'a/b'   # quote_plus defaults safe to ''
assert unquote('abc%20def') == 'abc def' and unquote('%C3%A9') == 'é' and unquote_plus('a+b%2B') == 'a b+'
assert unquote('%zz%41') == '%zzA' and unquote('%') == '%'                         # malformed escapes stay
assert unquote(quote('Ärger & Co / 100%')) == 'Ärger & Co / 100%'

# query strings both ways
assert urlencode({'q': 'a b', 'n': 1, 'e': 'é'}) == 'q=a+b&n=1&e=%C3%A9'
assert urlencode([('k', 'v w'), ('k', 2)]) == 'k=v+w&k=2' and urlencode({'l': [1, 2]}, doseq=True) == 'l=1&l=2'
assert urlencode({'a b': 'c d'}, quote_via=quote) == 'a%20b=c%20d'
assert parse_qs('a=1&a=2&b=x+y&c=') == {'a': ['1', '2'], 'b': ['x y']}
assert parse_qs('a=1&c=', keep_blank_values=True) == {'a': ['1'], 'c': ['']}
assert parse_qsl('a=1&b=%20') == [('a', '1'), ('b', ' ')] and parse_qsl('') == []
assert parse_qsl(urlencode({'x': 'a&b=c'})) == [('x', 'a&b=c')]
try:
    parse_qsl('a=1&b', strict_parsing=True)
    assert False
except ValueError:
    pass

# urlparse/urlsplit: the six/five fields, the properties, the tuple nature, the round trip
u = urlparse('https://user:pw@Example.com:8080/p/a;par?q=1&r=2#frag')
assert u.scheme == 'https' and u.netloc == 'user:pw@Example.com:8080' and u.path == '/p/a' and u.params == 'par'
assert u.query == 'q=1&r=2' and u.fragment == 'frag'
assert u.hostname == 'example.com' and u.port == 8080 and u.username == 'user' and u.password == 'pw'
assert u.geturl() == 'https://user:pw@Example.com:8080/p/a;par?q=1&r=2#frag' and urlunparse(u) == u.geturl()
assert isinstance(u, tuple) and isinstance(u, ParseResult) and len(u) == 6 and u[0] == 'https'
assert u == ('https', 'user:pw@Example.com:8080', '/p/a', 'par', 'q=1&r=2', 'frag')
assert u._replace(scheme='http').geturl() == 'http://user:pw@Example.com:8080/p/a;par?q=1&r=2#frag'
scheme, netloc, path, params, query, fragment = u
assert (scheme, fragment) == ('https', 'frag')
s = urlsplit('http://[::1]:80/p;x?y')
assert isinstance(s, SplitResult) and s.path == '/p;x' and s.hostname == '::1' and s.port == 80 and s.query == 'y'
assert urlparse('/a/b?c').path == '/a/b' and urlparse('www.x.com/a').netloc == '' and urlparse('http://h').port is None
assert urlparse('mailto:a@b').path == 'a@b' and urlparse('HTTP://H/').scheme == 'http'
assert urlparse('http://h/p#f', allow_fragments=False).path == '/p#f' and urlparse('p', scheme='ftp').scheme == 'ftp'
assert urlunsplit(('http', 'h', 'p', '', '')) == 'http://h/p' and urlunsplit(('', '', 'p', '', '')) == 'p'
for url in ['http://h/p?q#f', 'https://u@h:1/a;b?c#d', 'file:///tmp/x', 'mailto:a@b', 'urn:isbn:0451450523']:
    assert urlunparse(urlparse(url)) == url and urlunsplit(urlsplit(url)) == url
try:
    urlparse('http://h:abc/').port
    assert False
except ValueError:
    pass

# urljoin: RFC 3986 section 5.4 normal and abnormal examples
base = 'http://a/b/c/d;p?q'
for rel, expected in [('g', 'http://a/b/c/g'), ('./g', 'http://a/b/c/g'), ('g/', 'http://a/b/c/g/'), ('/g', 'http://a/g'),
                      ('//g', 'http://g'), ('?y', 'http://a/b/c/d;p?y'), ('g?y', 'http://a/b/c/g?y'), ('#s', 'http://a/b/c/d;p?q#s'),
                      ('g#s', 'http://a/b/c/g#s'), ('g?y#s', 'http://a/b/c/g?y#s'), (';x', 'http://a/b/c/;x'), ('g;x', 'http://a/b/c/g;x'),
                      ('', 'http://a/b/c/d;p?q'), ('.', 'http://a/b/c/'), ('./', 'http://a/b/c/'), ('..', 'http://a/b/'),
                      ('../', 'http://a/b/'), ('../g', 'http://a/b/g'), ('../..', 'http://a/'), ('../../', 'http://a/'),
                      ('../../g', 'http://a/g'), ('../../../g', 'http://a/g'), ('../../../../g', 'http://a/g'),
                      ('/./g', 'http://a/g'), ('/../g', 'http://a/g'), ('g.', 'http://a/b/c/g.'), ('.g', 'http://a/b/c/.g'),
                      ('g..', 'http://a/b/c/g..'), ('..g', 'http://a/b/c/..g'), ('./../g', 'http://a/b/g'), ('./g/.', 'http://a/b/c/g/'),
                      ('g/./h', 'http://a/b/c/g/h'), ('g/../h', 'http://a/b/c/h'), ('g;x=1/./y', 'http://a/b/c/g;x=1/y'),
                      ('g;x=1/../y', 'http://a/b/c/y'), ('g?y/./x', 'http://a/b/c/g?y/./x'), ('g#s/../x', 'http://a/b/c/g#s/../x'),
                      ('http:g', 'http://a/b/c/g')]:
    assert urljoin(base, rel) == expected, rel
assert urljoin('http://a/b', 'https://x/y') == 'https://x/y' and urljoin('', 'x') == 'x' and urljoin('http://a/b', '') == 'http://a/b'

# urldefrag
d = urldefrag('http://h/p#frag')
assert d.url == 'http://h/p' and d.fragment == 'frag' and d.geturl() == 'http://h/p#frag' and d == ('http://h/p', 'frag')
assert urldefrag('http://h/p').fragment == ''

import urllib.parse as up

# ---- the scheme lists are live (closed 2026-09-11) ----
# CPython's extension point: appending to uses_relative and uses_netloc teaches urljoin a scheme of its
# own, and urlparse/urlunsplit read uses_params and uses_netloc the same way. They were a snapshot of a
# private set here, so an append silently did nothing.
assert up.urljoin('myapp://h/a/b', 'c') == 'c'                 # untaught: the url wins whole
up.uses_relative.append('myapp')
up.uses_netloc.append('myapp')
assert up.urljoin('myapp://h/a/b', 'c') == 'myapp://h/a/c'
assert up.urljoin('myapp://h/a/b', '../c') == 'myapp://h/c'
assert up.urljoin('myapp://h/a/b', '//other/x') == 'myapp://other/x'
assert up.urlunsplit(('myapp', 'h', 'p', '', '')) == 'myapp://h/p'
assert up.urlparse('myapp://h/p').geturl() == 'myapp://h/p'
assert up.urlparse('myapp://h/p;q').params == ''
up.uses_params.append('myapp')
assert up.urlparse('myapp://h/p;q').params == 'q'
# and in the other direction
assert up.urljoin('http://h/a/b', 'c') == 'http://h/a/c'
up.uses_relative.remove('http')
assert up.urljoin('http://h/a/b', 'c') == 'c'
up.uses_relative.append('http')

assert up.unwrap('<URL:http://h/p>') == 'http://h/p' and up.unwrap('<http://h/>') == 'http://h/'
assert up.unwrap('  http://h/  ') == 'http://h/' and up.unwrap('URL:http://h/') == 'http://h/'
assert up.clear_cache() is None            # dialect: nothing is cached here, the call keeps the surface
assert up.scheme_chars.startswith('abcdefgh') and '+' in up.scheme_chars
