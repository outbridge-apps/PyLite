# repeat: 2
# deadline: 60000
import time, json, csv, io, re, datetime, decimal
from xml.etree import ElementTree as ET
T = time.perf_counter
out = []
def sec(name, f):
    t = T()
    f()
    out.append("%s=%d" % (name, (T() - t) * 1000))

def s_json():
    data = [{"id": i, "name": "n%d" % i, "tags": ["a", "b", i % 5], "v": i * 0.5, "ok": i % 2 == 0, "z": None} for i in range(5000)]
    s = json.dumps(data)
    back = json.loads(s)
    s2 = json.dumps(back, indent=2, sort_keys=True)
    return len(s) + len(back) + len(s2)

def s_csv():
    buf = io.StringIO()
    w = csv.writer(buf)
    for i in range(10000):
        w.writerow([i, "name %d" % i, "a,b" if i % 3 == 0 else "x", i * 1.25, 'q"t' if i % 7 == 0 else ""])
    text = buf.getvalue()
    rows = list(csv.reader(io.StringIO(text)))
    dr = list(csv.DictReader(io.StringIO("a,b,c\n" + "1,2,3\n" * 5000)))
    return len(rows) + len(dr)

def s_re():
    text = " ".join("word%d foo-%d bar_%d 2024-01-%02d x@y.com.example.com" % (i, i, i, i % 28 + 1) for i in range(3000))
    a = len(re.findall(r"\b\w+\b", text))
    b = len(re.findall(r"\d{4}-\d{2}-\d{2}", text))
    c = len(re.sub(r"(\w+)@(\w+)\.com", r"\2 at \1", text))
    d = len(re.split(r"\s+", text))
    p = re.compile(r"foo-(\d+)")
    e = sum(int(m.group(1)) for m in p.finditer(text))
    f = 0
    for i in range(5000):
        if re.match(r"word\d+", "word%d" % i):
            f += 1
    g = len(re.findall(r"(?i)WORD\d+", text))
    return a + b + c + d + e + f + g

def s_dt():
    base = datetime.datetime(2024, 1, 1, 8, 30)
    t = 0
    for i in range(10000):
        d = base + datetime.timedelta(days=i, minutes=i)
        t += d.weekday() + len(d.isoformat()) + len(d.strftime("%Y-%m-%d %H:%M"))
    for i in range(3000):
        t += datetime.datetime.strptime("2024-03-%02d 10:11:12" % (i % 28 + 1), "%Y-%m-%d %H:%M:%S").day
        t += datetime.date.fromisoformat("2024-02-%02d" % (i % 28 + 1)).toordinal() % 3
    return t

def s_dec():
    D = decimal.Decimal
    t = D(0)
    for i in range(20000):
        p = D("%d.%02d" % (i, i % 100))
        t += (p * D("1.21")).quantize(D("0.01"), rounding=decimal.ROUND_HALF_UP)
        t -= p / D(3)
        t += p // D(7) + p % D(7)
    return str(t)

def s_xml():
    root = ET.Element("root")
    for i in range(3000):
        e = ET.SubElement(root, "item", id=str(i), kind="k%d" % (i % 5))
        e.text = "value %d" % i
        ET.SubElement(e, "sub").text = str(i * 2)
    s = ET.tostring(root, encoding="unicode")
    r2 = ET.fromstring(s)
    a = len(r2.findall("item[@kind='k3']"))
    b = sum(int(x.text) for x in r2.iter("sub"))
    c = len(r2.findall(".//sub"))
    return len(s) + a + b + c

def s_fmt():
    t = 0
    for i in range(20000):
        t += len("{:>10}|{:<8.3f}|{:,}|{:x}".format(i, i / 7, i * 1000, i))
        t += len("%5d %-6s %.2e" % (i, "ab", i * 1.1))
        t += len(repr(i / 3)) + len(str(float(i)))
    return t

for name, f in (("json", s_json), ("csv", s_csv), ("re", s_re), ("dt", s_dt), ("dec", s_dec), ("xml", s_xml), ("fmt", s_fmt)):
    sec(name, f)
print(" ".join(out))
