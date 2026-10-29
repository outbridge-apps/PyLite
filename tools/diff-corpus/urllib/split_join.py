from urllib.parse import urlparse, urlsplit, urlunparse, urlunsplit, urljoin, urldefrag

out = []
u = urlparse('https://user:pw@shop.example.com:8443/a/b;p?q=1&r=2#frag')
out.append((u.scheme, u.netloc, u.path, u.params, u.query, u.fragment))
out.append((u.hostname, u.port, u.username, u.password))
out.append(urlunparse(u))
s = urlsplit('HTTP://Example.COM/Path?x=1')
out.append((s.scheme, s.netloc, s.hostname, s.path))
out.append(urlunsplit(('https', 'h.org', '/p', 'a=1', '')))
out.append(tuple(urlparse('//host/path')))
out.append(tuple(urlparse('mailto:someone@example.com')))
out.append(tuple(urlparse('/just/a/path?x')))
base = 'http://a/b/c/d;p?q'
for ref in ['g', './g', 'g/', '/g', '//g', '?y', 'g?y', '#s', '..', '../g', '../..', '../../g', 'g;x', '']:
    out.append(urljoin(base, ref))
out.append(tuple(urldefrag('http://h/p#sec')))
out.append(u._replace(fragment='').geturl())
result = out
