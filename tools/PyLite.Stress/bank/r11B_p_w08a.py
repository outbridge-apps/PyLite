import re, time
# per-tenant synonym list (about 2,400 chars), compiled once per tenant, case-insensitive, then dropped
base = ["invoice", "shipping", "kitchen", "desk", "assist", "basis", "status", "mission", "chassis", "disk",
        "sink", "skis", "silk", "kiosk", "risk", "list", "insist", "visit", "service", "position"]
t = time.perf_counter()
hits = 0
for tenant in range(100):
    words = [w + str(tenant) + suf for w in base for suf in ("", "s", "es", "ing", "ed", "er", "ers", "ion", "ist", "ive")]
    src = r"\b(?:" + "|".join(words) + r")\b"
    pat = re.compile(src, re.I)
    if pat.search("the kiosk%dist is here" % tenant):
        hits += 1
print(len(src), hits, round((time.perf_counter() - t) * 1000))
