import re, time
# alloc per distinct compiled pattern, by kind; each kind compiles N distinct patterns and drops them
N = 2000
kinds = {
    "plain": lambda i: "item%d-[0-9]+" % i,
    "w": lambda i: r"\w+=%d" % i,
    "b": lambda i: r"\bkey%d\b" % i,
    "wb3": lambda i: r"\b(\w+)\s+(\w+)\s+%d\b" % i,
    "I_cls": lambda i: r"[a-z_]+%d[a-z]*" % i,
    "email": lambda i: r"[\w.+-]+@[\w-]+\.[\w.]+%d" % i,
}
which = KIND
f = kinds[which]
flags = FLAGS
t = time.perf_counter()
hits = 0
for i in range(N):
    p = re.compile(f(i), flags)
    if p.search("x key%d foo bar %d item%d-12 a@b.c%d" % (i, i, i, i)):
        hits += 1
print(which, flags, hits, round((time.perf_counter() - t) * 1000))
