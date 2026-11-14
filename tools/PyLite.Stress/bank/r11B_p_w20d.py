import re, time
# validation rules from a config table: each field has its own pattern, checked with match / fullmatch
rules0 = [(r"[A-Z]{2}\d{%d}" % (i % 9 + 2), "AB" + "1" * (i % 9 + 2)) for i in range(3000)]
rules = [(r"(?i)[a-z_][\w.-]{0,%d}@[a-z\d-]+\.[a-z]{2,6}" % (i % 40 + 5), "john.smith@example.com") for i in range(1500)]
re.compile(r"(?i)[a-z_][w.-]{0,1}@[a-zd-]+.[a-z]{2,6}").match("a@b.cc.example.com"); re.compile(r"(?i)kb").search("k")
t0 = time.perf_counter(); re.compile(r"(?i)[a-z_][w.-]{0,77}@[a-zd-]+.[a-z]{2,6}").match("a@b.cc.example.com"); print("one", round((time.perf_counter() - t0) * 1000, 2))
t = time.perf_counter()
ok1 = sum(1 for p, s in rules if re.match(p, s))
t1 = time.perf_counter()
ok2 = sum(1 for p, s in rules if re.fullmatch(p, s))
t2 = time.perf_counter()
ok3 = sum(1 for p, s in rules if re.search(p, s))
t3 = time.perf_counter()
print(ok1, ok2, ok3, round((t1 - t) * 1000), round((t2 - t1) * 1000), round((t3 - t2) * 1000))
