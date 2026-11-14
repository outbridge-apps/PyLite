import re, time
# case-insensitive searches over a 1 MB document
para = "The customer reported that the shipping status of order %d was not updated. Please assist with the invoice. "
doc = "".join(para % i for i in range(9000))
t = time.perf_counter()
a = len(re.findall(r"status", doc, re.I))
t1 = time.perf_counter()
b = len(re.findall(r"\bassist\b", doc, re.I))
t2 = time.perf_counter()
c = len(re.findall(r"(?:invoice|shipping|kiosk|risk)", doc, re.I))
t3 = time.perf_counter()
d = len(re.findall(r"order \d+", doc, re.I))
t4 = time.perf_counter()
e = len(re.findall(r"zzz", doc, re.I))
t5 = time.perf_counter()
print(len(doc), a, b, c, d, e, [round((y - x) * 1000) for x, y in ((t, t1), (t1, t2), (t2, t3), (t3, t4), (t4, t5))])
