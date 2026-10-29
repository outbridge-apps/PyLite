# Adapted from CPython's test suite (Lib/test/test_html.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import html

# escape: quote=True by default
assert html.escape('<a & "b">') == '&lt;a &amp; &quot;b&quot;&gt;'
assert html.escape("it's") == 'it&#x27;s'
assert html.escape('<>', quote=False) == '&lt;&gt;'
assert html.escape('"q"', quote=False) == '"q"'
assert html.escape('plain') == 'plain'

# unescape: named, decimal and hex references
assert html.unescape('&lt;&amp;&gt;') == '<&>'
assert html.unescape('&quot;x&quot;') == '"x"'
assert html.unescape('&#65;') == 'A'
assert html.unescape('&#x41;') == 'A'
assert html.unescape('no refs') == 'no refs'

# round trip
src = '<tag attr="v">&data</tag>'
assert html.unescape(html.escape(src)) == src

# legacy semicolon-less names, numeric refs without ';', cp1252 remap, invalid code points
assert html.unescape('&amp &ampxyz &AMP; &#65 &#x41;') == '& &xyz & A A'
assert html.unescape('&#128;&#0;&#x110000;') == '€��'
assert html.unescape('&copy2000 &foo &notit; &notin;') == '\xa92000 &foo \xacit; ∉'
# the full HTML5 name table (2231 references), not just the HTML 4 set
assert html.unescape('&hellip;&NewLine;&nbsp;') == '…\n\xa0' and html.unescape('&bsol;') == '\\' and html.unescape('&apos;') == "'"
assert html.unescape('&Afr;') == html.unescape('&#x1D504;') and len(html.unescape('&Afr;')) == 2
assert html.unescape('&hellip') == '&hellip' and html.unescape('&zzz;') == '&zzz;' and html.unescape('&notanentity;') == '\xacanentity;'
