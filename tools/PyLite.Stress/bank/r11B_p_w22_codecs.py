import time
t = time.perf_counter()
n = 0
for i in range(100000):
    b = ("row %d" % i).encode("utf-8")
    n += len(b.decode("UTF8")) + len(b.decode("latin-1")) + len(("x%d" % i).encode("ascii"))
t1 = time.perf_counter()
for i in range(50000):
    n += len(str(b, "cp1252")) + len(bytes("a", encoding="iso-8859-1"))
t2 = time.perf_counter()
print(n, round((t1 - t) * 1000), round((t2 - t1) * 1000))
