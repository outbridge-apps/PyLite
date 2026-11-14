# allocmb: 64
# deadline: 60000
# Review 19: a group's quantifier was read in the group's own VERBOSE mode, .NET reads it in the mode around it.
# Without VERBOSE, (?x:<215 pages>) {2} is the group and a repeated space: .NET merges the run and keeps 225 KB a
# regex, which was charged 3.3 KB (300 kept grew the process to 229 MB under 64 MB). The limit is met now.
import re
head = ''.join(chr((k + 1) * 256) for k in range(215))
kept = []
for i in range(300):
    r = re.compile('(?x:' + head + ') {2}k%d' % i)
    r.search(head + '  k%d' % i); r.match(head); r.fullmatch(head)
    kept.append(r)
print(len(kept))
