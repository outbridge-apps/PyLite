# allocmb: 16
# deadline: 60000
# Review 22: after the loop .NET drops groups that hold nothing it keeps ((?x: ) without VERBOSE, (?:b{0})), and still
# merges the loops: each regex keeps a 4,000-character prefix (24 KB). The walk took those groups for content and
# charged none of it (1,200 kept grew the process to 132 MB under 64 MB). The limit is met now.
import re
kept = []
for i in range(1200):
    r = re.compile(('(?:a{10,}(?x: )){400}x%d' if i % 2 else '(?:a{10,}(?:b{0})){400}x%d') % i)
    r.match('ab')
    kept.append(r)
print(len(kept))
