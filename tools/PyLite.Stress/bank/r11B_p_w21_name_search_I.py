import re, time
# for each customer, find its name (case-insensitive, whole word) in a short note; one pattern per customer
names = ["Kirsten Sikkink %d" % i for i in range(3000)]
notes = ["Called kirsten sikkink %d about the invoice; KIRSTEN SIKKINK %d to pay" % (i, i) for i in range(3000)]
re.search(r"\bwarm\b", "warm up", re.I)   # first-use tables out of the timing
t0 = time.perf_counter()
hits = 0
for n, s in zip(names, notes):
    if re.search(r"\b" + re.escape(n) + r"\b", s, re.I):
        hits += 1
t1 = time.perf_counter()
hits2 = 0
for n, s in zip(names, notes):
    if re.search(r"\b" + re.escape(n) + r"\b", s):
        hits2 += 1
t2 = time.perf_counter()
print(hits, hits2, round((t1 - t0) * 1000), round((t2 - t1) * 1000))
