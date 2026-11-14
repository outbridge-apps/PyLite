import time
import xml.etree.ElementTree as ET
# an XML export of 12,000 records: validate, map and total each record by findtext; then the same after a fix-up pass
fields = ["Id", "Name", "City", "Zip", "Qty", "Price", "Cur", "Date", "Ref", "Note"]
rec = "<R>" + "".join("<%s>%d</%s>" % (f, k + 1, f) for k, f in enumerate(fields)) + "</R>"
root = ET.fromstring("<Rows>" + rec * 12000 + "</Rows>")
t = time.perf_counter()
for rnd in range(2):
    bad = [r for r in root if any(r.findtext(f) in (None, "") for f in fields)]
    out = [{f: r.findtext(f) for f in fields} for r in root]
    total = sum(int(r.findtext("Qty")) * int(r.findtext("Price")) for r in root)
    for r in root:
        r.find("Note").text = "checked"
print(len(bad), len(out), total, round((time.perf_counter() - t) * 1000))
