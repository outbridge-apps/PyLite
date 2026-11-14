class CM:
    def __init__(self, n): self.n = n
    def __enter__(self): return self.n
    def __exit__(self, a, b, c): return False
out = []
with (CM(1) as a, CM(2) as b):
    out.append(a + b)
with (CM(1) as a, CM(2) as b,):
    out.append(a * b)
with (CM(3)):
    out.append(3)
with (CM(4)) as c:
    out.append(c)
with (CM(5), CM(6)):
    out.append(56)
with (CM(7) as d):
    out.append(d)
lst = [0]
with (CM(9) as lst[0]):
    out.append(lst[0])
class O: pass
o = O()
with (CM(10) as lst[0], CM(11) as (r)):
    out.append(lst[0])
with (
    CM(12) as g,
    CM(13) as h
):
    out.append(g + h)
with (CM(14) as (i)):
    out.append(i)
with (CM)(15) as j:
    out.append(j)
with (lambda: CM(16))() as k:
    out.append(k)
with (x := CM(17)) as m:
    out.append(m)
print(out)
