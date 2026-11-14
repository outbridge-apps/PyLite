import re, time
# a document with one combining mark (NFD text), searched for many distinct whole words
doc = ('the quick brown fox jumps over the lazy dog cafe' + chr(0x301) + ' ') * 50
words = ['w%05d' % i for i in range(20000)]
t = time.perf_counter()
hits = 0
for w in words:
    if re.search(r'\b' + w + r'\b', doc):
        hits += 1
print(hits, round(time.perf_counter() - t, 2))
