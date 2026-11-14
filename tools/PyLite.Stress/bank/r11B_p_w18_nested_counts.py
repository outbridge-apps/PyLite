import re, time
# a 1 MB mail-merge log; 12 extraction rules, several with nested bounded groups (address lists, dotted paths, CSV-ish runs)
para = "To: a.b@x.com.example.com, c.d@y.org.example.com, e.f@z.net.example.com; path=/srv/app/v1/x/y/z ip=10.1.2.3 vals=1,2,3,4,5,6,7,8 ok\n"
doc = para * 11000
rules = [
    r"(?:(?:\w+\.){1,4}\w+@(?:\w+\.){1,3}\w+(?:, )?){1,20}",
    r"(?:/(?:[\w-]+)){1,12}",
    r"(?:(?:\d{1,3}\.){3}\d{1,3})",
    r"vals=(?:(?:\d+),?){1,40}",
    r"(?:(?:[a-z]{1,8})(?:\.[a-z]{1,8}){0,3}@)",
    r"(?:(?:\w{1,10}\s){1,8}){1,10}",
    r"(?:ip|path)=\S+",
    r"(?:(?:\d,){1,9}){1,9}\d",
    r"(?:(?:[A-Za-z]+[.:;,]? ?){1,10}){1,10}",
    r"(?:(?:x|y|z)/?){1,9}",
    r"(?:(?:[a-f\d]{1,4}:){1,7}[a-f\d]{1,4})",
    r"(?:(?:[\w.]+@[\w.]+)(?:, |; ){0,1}){1,5}",
]
t = time.perf_counter()
out = []
for r in rules:
    p = re.compile(r)
    m = p.search(doc, len(doc) - 500)
    out.append(len(p.findall(doc[:20000])) + (1 if m else 0))
print(out, round((time.perf_counter() - t) * 1000))
