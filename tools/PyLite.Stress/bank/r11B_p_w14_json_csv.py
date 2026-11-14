import json, csv, io, time
t = time.perf_counter()
rows = [{"id": i, "name": "Customer %d" % i, "amount": i * 1.25, "tags": ["a", "b", str(i % 7)], "active": i % 2 == 0,
         "addr": {"city": "City%d" % (i % 50), "zip": "%05d" % i}} for i in range(20000)]
s = json.dumps(rows)
t1 = time.perf_counter()
back = json.loads(s)
t2 = time.perf_counter()
s2 = json.dumps(back, indent=2, sort_keys=True)
t3 = time.perf_counter()
buf = io.StringIO()
w = csv.writer(buf)
w.writerow(["id", "name", "amount", "city"])
for r in back:
    w.writerow([r["id"], r["name"], r["amount"], r["addr"]["city"]])
text = buf.getvalue()
t4 = time.perf_counter()
rd = csv.DictReader(io.StringIO(text))
tot = 0.0
for r in rd:
    tot += float(r["amount"])
t5 = time.perf_counter()
print(len(s), len(s2), len(text), tot, [round((b - a) * 1000) for a, b in ((t, t1), (t1, t2), (t2, t3), (t3, t4), (t4, t5))])
