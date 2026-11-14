# deadline: 60000
# Review 14: a 500-word alternation built every row for 3,000 rows; a pattern is paid while the CLR keeps it, not
# again by every compile (the build under review stopped at row 1,900 under the default limit, 1.1.0 ran it on 115 MB).
import re
words = ['w%d' % i for i in range(3000)]
total = 0
for row in range(3000):
    stop = words[row % 2000: row % 2000 + 500]
    pat = re.compile(r'\b(?:' + '|'.join(map(re.escape, stop)) + r')\b')
    total += len(pat.findall(' '.join(words[row % 2000 + 10: row % 2000 + 60])))
print(total)
