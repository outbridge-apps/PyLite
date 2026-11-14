# allocmb: 64
# deadline: 60000
# Review 17: a literal prefix over 215 pages past ASCII spelled as [X], (?:X) or X{1}, which .NET merges into one
# run: its Boyer-Moore table keeps 1 KB a page (231 KB a regex), and the page charge read a plain run only (8-20 KB
# charged; 300 kept grew the process to 233 MB under 64 MB). The limit is met now.
import re
units = ['[%s]', '(?:%s)', '%s{1}']
subj = ''.join(chr((k + 1) * 256) for k in range(215))
kept = []
for i in range(300):
    head = ''.join(units[i % 3] % chr((k + 1) * 256) for k in range(215))
    r = re.compile(head + 'k%d' % i)
    r.search(subj + 'k%d' % i); r.match(subj); r.fullmatch(subj)
    kept.append(r)
print(len(kept))
