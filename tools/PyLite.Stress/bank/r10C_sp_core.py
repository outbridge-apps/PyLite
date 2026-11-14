# repeat: 2
# deadline: 60000
import time
T = time.perf_counter
out = []
def sec(name, f):
    t = T()
    f()
    out.append("%s=%d" % (name, (T() - t) * 1000))

def s_str():
    acc = 0
    for i in range(20000):
        s = "item-%d-%s" % (i, "x" * (i % 7))
        acc += len(s.upper().replace("X", "yz").split("-"))
        acc += s.find("7") + len(s.strip("i")) + s.count("-")
        acc += len(f"{i:08d}|{s!r}|{i * 1.5:.2f}")
    parts = [str(i) for i in range(50000)]
    acc += len(",".join(parts))
    return acc

def s_list():
    xs = list(range(100000))
    ys = [x * 2 for x in xs if x % 3]
    ys.sort(key=lambda v: -v)
    zs = sorted(ys)[:1000]
    a = sum(xs) + len(ys) + zs[0]
    for i in range(20000):
        xs.append(i)
        xs.pop()
    return a + xs[-1] + len(xs[10:5000:3])

def s_dict():
    d = {}
    for i in range(50000):
        d["k%d" % i] = i
    t = 0
    for k, v in d.items():
        t += v
    for i in range(50000):
        t += d.get("k%d" % (i * 2), 0)
    s = set(range(0, 100000, 3)) & set(range(0, 100000, 5))
    dd = {i: [i] for i in range(20000)}
    return t + len(s) + len(dd)

class Pt:
    def __init__(self, x, y):
        self.x = x
        self.y = y
    def norm(self):
        return self.x * self.x + self.y * self.y
    def __eq__(self, o):
        return self.x == o.x and self.y == o.y
    def __hash__(self):
        return hash((self.x, self.y))

class Pt3(Pt):
    def __init__(self, x, y, z):
        super().__init__(x, y)
        self.z = z
    def norm(self):
        return super().norm() + self.z * self.z

def s_class():
    t = 0
    for i in range(30000):
        p = Pt3(i, i + 1, i + 2)
        t += p.norm()
    st = {Pt(i % 100, i % 7) for i in range(20000)}
    return t + len(st)

def s_exc():
    t = 0
    for i in range(20000):
        try:
            if i % 2:
                raise ValueError("bad %d" % i)
            {}["x"]
        except ValueError as e:
            t += len(str(e))
        except KeyError:
            t += 1
        finally:
            t += 1
    return t

def gen(n):
    return (i * i for i in range(n))

def s_comp():
    a = sum(gen(100000))
    b = {i % 1000: i for i in range(100000)}
    c = [[j for j in range(i % 10)] for i in range(20000)]
    d = list(map(lambda v: v + 1, range(100000)))
    e = list(filter(None, range(100000)))
    f = list(zip(range(50000), range(50000)))
    g = list(enumerate("abcdefghij" * 5000))
    return a + len(b) + len(c) + len(d) + len(e) + len(f) + len(g)

def s_int():
    t = 0
    for i in range(100000):
        t += (i * 31) % 17 + (i << 3) - (i >> 2) + i // 7
    big = 1
    for i in range(1, 600):
        big *= i
    return t + len(str(big)) + int(1.5 * 10) + round(2.675, 2) * 0

for name, f in (("str", s_str), ("list", s_list), ("dict", s_dict), ("class", s_class), ("exc", s_exc), ("comp", s_comp), ("int", s_int)):
    sec(name, f)
print(" ".join(out))
