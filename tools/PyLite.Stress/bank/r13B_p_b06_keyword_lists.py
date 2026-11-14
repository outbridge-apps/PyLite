# deadline: 60000
# product-code and stop-word alternations compiled once each per customer profile and scanned over documents
import re
import time
words = ['w%sx%d' % (chr(97 + i % 26) * (i % 5 + 1), i) for i in range(300)]
codes = ['P-%04d-%s' % (i, chr(65 + i % 26)) for i in range(250)]
text = ' '.join(words[i % 300] + ' lorem ipsum ' + codes[i % 250] for i in range(40000))
t0 = time.perf_counter()
p1 = re.compile(r'\b(?:' + '|'.join(words) + r')\b')
p2 = re.compile('|'.join(re.escape(c) for c in codes))
p3 = re.compile(r'\b(?:' + '|'.join(words[:300]) + r')\b', re.I)
t1 = time.perf_counter()
a = len(p1.findall(text))
b = len(p2.findall(text))
c = len(p3.findall(text))
t2 = time.perf_counter()
kept = []
for prof in range(6):
    sub = words[prof * 40:(prof + 1) * 40] + codes[prof * 30:(prof + 1) * 30]
    kept.append(re.compile('|'.join(re.escape(s) for s in sub)))
d = sum(len(k.findall(text[:200000])) for k in kept)
t3 = time.perf_counter()
print(a, b, c, d, 'compile', round(t1 - t0, 3), 'scan', round(t2 - t1, 3), 'profiles', round(t3 - t2, 3))
