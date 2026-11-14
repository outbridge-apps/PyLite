import time, functools
def logged(fn):
    def wrapper(*args, **kwargs):
        return fn(*args, **kwargs)
    return wrapper

@logged
def depth_sum(node, *path):
    s = len(path)
    for ch in node:
        s += depth_sum(ch, *path, len(ch))
    return s

def tree(d):
    return [] if d == 0 else [tree(d - 1), tree(d - 1)]

t = time.perf_counter()
tr = tree(14)
r1 = depth_sum(tr)
t1 = time.perf_counter()
rows = [[i, i * 2, i * 3, "x%d" % i] for i in range(100000)]
cols = list(zip(*rows))
m = max(*cols[0])
t2 = time.perf_counter()
def merge(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out
acc = 0
for i in range(100000):
    acc += len(merge(*rows[i:i + 3]))
t3 = time.perf_counter()
print(r1, len(cols), m, acc, round((t1 - t) * 1000), round((t2 - t1) * 1000), round((t3 - t2) * 1000))
