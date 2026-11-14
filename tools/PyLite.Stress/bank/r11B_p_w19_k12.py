import re, time
# 100 passes of a nested bounded group over a 3.6 MB document: what does it keep, what is charged?
doc = "Alpha beta, gamma delta; epsilon zeta: eta theta iota kappa lambda mu nu " * 50000
keep = []
t = time.perf_counter()
n = 0
for i in range(12):
    p = re.compile(r"(?:(?:[A-Za-z]+[.:;,]? ){1,10}){1,10}(?:%d)?" % i)
    n += len(p.findall(doc, 0, 300)) + (1 if p.search(doc, 3000000) else 0)
    keep.append(p)
print(n, len(doc), round((time.perf_counter() - t) * 1000))
