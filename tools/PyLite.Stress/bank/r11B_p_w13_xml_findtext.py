import time
import xml.etree.ElementTree as ET
# a host hands an XML export (built here); the script validates, maps and totals each record by findtext
fields = ["Id", "Name", "City", "Zip", "Qty", "Price", "Cur", "Date", "Ref", "Note"]
rec = "<R>" + "".join("<%s>%d</%s>" % (f, k + 1, f) for k, f in enumerate(fields)) + "</R>"
root = ET.fromstring("<Rows>" + rec * N + "</Rows>")
t = time.perf_counter()
bad = [r for r in root if any(r.findtext(f) in (None, "") for f in fields)]
out = [{f: r.findtext(f) for f in fields} for r in root]
total = sum(int(r.findtext("Qty")) * int(r.findtext("Price")) for r in root)
print(len(bad), len(out), total, round((time.perf_counter() - t) * 1000))
