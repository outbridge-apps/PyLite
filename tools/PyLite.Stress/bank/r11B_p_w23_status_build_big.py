import json, time
# HostPerf's status_build.py on a 60,000-row callback
callback_json = json.dumps({"rows": [{"doc": "D%06d" % (i % 50000), "ok": i % 7 != 0} for i in range(60000)]})
t = time.perf_counter()
payload = json.loads(callback_json)
seen = set()
statuses = []
for row in payload.get("rows", []):
    doc = str(row.get("doc") or "")
    if doc == "" or doc in seen:
        continue
    seen.add(doc)
    statuses.append({
        "doc": doc,
        "status": 2 if row.get("ok") else 3,
        "message": "ok" if row.get("ok") else "failed",
    })
status_json = json.dumps(statuses)
print(len(status_json), len(statuses), round((time.perf_counter() - t) * 1000))
