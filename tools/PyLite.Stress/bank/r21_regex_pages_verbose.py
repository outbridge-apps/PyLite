# allocmb: 64
# deadline: 60000
# Review 18: in VERBOSE an indent before a group ended the run the page charge read, and .NET passes it: a 215-page
# literal in the usual layout (a newline, an indent, a named group) kept 224 KB a regex charged 3-5 KB (300 kept grew
# the process to 300 MB under 64 MB). The limit is met now.
import re
head = ''.join(chr((k + 1) * 256 + 1) for k in range(215))
kept = []
for i in range(300):
    r = re.compile(chr(10) + '    (?P<w>' + head + ')  k%d' % i, re.X | re.I)
    r.search(head + 'k%d' % i); r.match(head); r.fullmatch(head)
    kept.append(r)
print(len(kept))
