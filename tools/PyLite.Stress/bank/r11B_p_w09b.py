import re, time
para = "The customer reported that the shipping status of order %d was not updated. Please assist with the invoice. "
doc = "".join(para % i for i in range(9000))
pats = [r"status", r"\bassist\b", r"(?:invoice|shipping|kiosk|risk)", r"order \d+", r"zzz", r"customer", r"updated"]
re.findall(r"x\bq", "x", re.I); re.findall("k", "k", re.I)
out = []
for rnd in range(2):
    row = []
    for p in pats:
        t = time.perf_counter()
        n = len(re.findall(p, doc, re.I))
        row.append((n, round((time.perf_counter() - t) * 1000)))
    out.append(row)
print(out[0]); print(out[1])
