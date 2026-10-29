# MaxDataDepth: how deeply ==, ordering, hash and the walkers that re-enter through a value (a deque of
# deques, a record inside a record) descend into nested data; json and copy stop at it too. Unlike the
# budgets this is a Python RecursionError, so a script can catch it.
# limits: MaxDataDepth=32
# expect: RuntimeError RecursionError
a = []
b = []
for _ in range(100):
    a = [a]
    b = [b]
same = a == b
