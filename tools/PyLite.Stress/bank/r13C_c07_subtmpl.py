import re, time
s = "k1=v1;" * 200000
t0 = time.perf_counter()
r = re.sub(r"(\w+)=(\w+)", r":", s)
t1 = time.perf_counter()
r2 = re.subn(r"(\w)(\w)=", r"\g<2>\g<1>\g<0>==", s)[1]
t2 = time.perf_counter()
print(len(r), r2, round(t1 - t0, 3), round(t2 - t1, 3))
