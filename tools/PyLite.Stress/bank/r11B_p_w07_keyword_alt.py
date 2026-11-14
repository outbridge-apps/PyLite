import re, time
# each customer has its own stop-list of product keywords; the script flags documents mentioning any (case-insensitive)
base = ["invoice", "shipping", "kitchen", "desk", "assist", "basis", "status", "mission", "chassis", "disk",
        "sink", "skis", "silk", "kiosk", "risk", "list", "insist", "visit", "service", "position"]
docs = ["Customer %d asks about the status of the kitchen sink and a desk, invoice %d" % (i, i) for i in range(50)]
t = time.perf_counter()
hits = 0
for cust in range(CUSTS):
    words = [w + str(cust) for w in base] + [w + "s" + str(cust) for w in base] + [w for w in base]
    pat = re.compile(r"\b(?:" + "|".join(words) + r")\b", re.I)
    for d in docs:
        if pat.search(d):
            hits += 1
print(len(r"\b(?:" + "|".join(words) + r")\b"), hits, round((time.perf_counter() - t) * 1000))
