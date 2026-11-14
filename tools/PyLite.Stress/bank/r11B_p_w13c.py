import time
import xml.etree.ElementTree as ET
# 5,000 records; 16 validation rules, each walks the records and looks up the fields it needs (800,000 find calls)
fields = ["Id", "Name", "City", "Zip", "Qty", "Price", "Cur", "Date", "Ref", "Note"]
rec = "<R>" + "".join("<%s>%d</%s>" % (f, k + 1, f) for k, f in enumerate(fields)) + "</R>"
root = ET.fromstring("<Rows>" + rec * 5000 + "</Rows>")
t = time.perf_counter()
bad = 0
for rule in range(16):
    for r in root:
        for f in fields:
            if r.find(f) is None:
                bad += 1
print(bad, round((time.perf_counter() - t) * 1000))
