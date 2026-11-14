import time, string
t = time.perf_counter()
parts = []
for i in range(50000):
    parts.append(f"{i:06d};{'name' + str(i):<12}|{i * 1.5:>10.2f}|{hex(i)}")
s = "\n".join(parts)
t1 = time.perf_counter()
lines = s.splitlines()
cols = [ln.split(";")[1].split("|") for ln in lines]
up = [c[0].strip().upper() for c in cols]
rep = s.replace("name", "NM").replace("|", ",")
fnd = sum(1 for ln in lines if ln.startswith("00") and ln.endswith("f"))
cnt = s.count("name1")
idx = [s.find("name%d" % i) for i in range(0, 50000, 500)]
t2 = time.perf_counter()
acc = ""
for i in range(5000):
    acc += "%d," % i
tr = s.translate(str.maketrans("0123456789", "abcdefghij"))[:50]
tpl = string.Template("Dear $name, you owe $amt").substitute(name="Bob", amt="10.00")
pads = [str(i).zfill(8) + str(i).rjust(10) + str(i).center(12, "*") for i in range(20000)]
cap = " ".join(w.capitalize() for w in ("hello world foo bar " * 5000).split())
enc = s.encode("utf-8")
dec = enc.decode("utf-8")
t3 = time.perf_counter()
print(len(s), up[1], len(rep), fnd, cnt, idx[:3], len(acc), tr, tpl, pads[5], len(cap), len(enc), dec == s)
print([round((b - a) * 1000) for a, b in ((t, t1), (t1, t2), (t2, t3))])
