# Adapted from CPython's test suite (Lib/test/test_functools.py: TestReduce, TestPartial.test_basic/
# test_attributes/test_kw_overrides, TestLRU.test_lru/test_cache, TestUpdateWrapper, TestCmpToKey).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import functools
from functools import reduce, partial, lru_cache, cache, wraps, update_wrapper, cmp_to_key

# ---- reduce ----
assert reduce(lambda a, b: a + b, [1, 2, 3, 4]) == 10
assert reduce(lambda a, b: a + b, [1, 2, 3, 4], 100) == 110
assert reduce(lambda a, b: a * b, range(1, 6)) == 120
assert reduce(lambda a, b: a + b, [], 'empty') == 'empty'
assert reduce(lambda a, b: a + [b], 'abc', []) == ['a', 'b', 'c']
assert reduce(lambda a, b: b, [1, 2, 3]) == 3 and reduce(lambda a, b: a, [1, 2, 3]) == 1
assert reduce(max, [3, 9, 2]) == 9 and reduce(lambda a, b: a + b, (x * x for x in range(4))) == 14
assert reduce(lambda acc, kv: {**acc, kv[0]: kv[1]}, [('a', 1), ('b', 2)], {}) == {'a': 1, 'b': 2}
assert reduce(lambda a, b: a + b, [7]) == 7
try:
    reduce(lambda a, b: a + b, [])
    assert False
except TypeError:
    pass
try:
    reduce(lambda a, b: a + b)
    assert False
except TypeError:
    pass
# left-to-right evaluation order
order = []
def rec(a, b):
    order.append((a, b))
    return a + b
reduce(rec, [1, 2, 3])
assert order == [(1, 2), (3, 3)]

# ---- partial ----
def power(base, exp, mod=None):
    r = base ** exp
    return r % mod if mod is not None else r
square = partial(power, exp=2)
assert square(5) == 25 and square(5, mod=7) == 4 and square(base=3) == 9
two_pow = partial(power, 2)
assert two_pow(10) == 1024 and two_pow(10, 1000) == 24
assert square.func is power and square.args == () and square.keywords == {'exp': 2}
assert two_pow.args == (2,) and two_pow.keywords == {}
assert two_pow(exp=3) == 8
assert square(4, exp=3) == 64                   # a later keyword overrides the stored one
p2 = partial(partial(power, 2), 3)
assert p2() == 8
inc = partial(lambda a, b: a + b, 1)
assert inc(41) == 42 and list(map(inc, [1, 2])) == [2, 3]
pj = partial('-'.join)
assert pj(['a', 'b']) == 'a-b'
pmax = partial(max, 10)
assert pmax(3) == 10 and pmax(30) == 30
assert callable(square) and repr(square).startswith('functools.partial(')
try:
    partial(5)
    assert False
except TypeError:
    pass
try:
    two_pow()
    assert False
except TypeError:
    pass
try:
    partial()
    assert False
except TypeError:
    pass

# ---- lru_cache / cache ----
calls = []
@lru_cache(maxsize=None)
def fib(n):
    calls.append(n)
    return n if n < 2 else fib(n - 1) + fib(n - 2)
assert fib(30) == 832040
assert len(calls) == 31                          # each n computed once
calls.clear()
assert fib(30) == 832040 and calls == []

@lru_cache(maxsize=2)
def sq(n):
    calls.append(n)
    return n * n
sq(1); sq(2); sq(1)                              # 1 is now most recent
sq(3)                                            # evicts 2
sq(1)                                            # still cached
assert calls == [1, 2, 3]
sq(2)                                            # recomputed
assert calls == [1, 2, 3, 2]

@lru_cache
def bare(n):
    calls.append(('bare', n))
    return n
calls.clear()
bare(1); bare(1)
assert calls == [('bare', 1)]

@cache
def kw(a, b=0):
    calls.append((a, b))
    return a + b
calls.clear()
assert kw(1) == 1 and kw(1) == 1 and kw(1, 0) == 1 and kw(1, b=0) == 1 and kw(a=1) == 1
assert calls == [(1, 0), (1, 0), (1, 0), (1, 0)]  # different call shapes are distinct keys

# unhashable arguments cannot be cached
@cache
def ident(x):
    return x
try:
    ident([1])
    assert False
except TypeError:
    pass
assert ident((1,)) == (1,)

# ---- wraps / update_wrapper ----
def deco(fn):
    @wraps(fn)
    def inner(*a, **k):
        return fn(*a, **k) * 2
    return inner

@deco
def value():
    'value doc'
    return 21
assert value() == 42
assert value.__name__ == 'value'

def plain(fn):
    def inner(*a):
        return fn(*a)
    return inner
@plain
def other():
    return 1
assert other.__name__ == 'inner'

def w(fn):
    def inner(*a):
        return fn(*a) + 1
    return update_wrapper(inner, fn)
@w
def base_fn():
    return 1
assert base_fn() == 2 and base_fn.__name__ == 'base_fn'

# ---- cmp_to_key ----
def numeric_then_alpha(a, b):
    if a.isdigit() != b.isdigit():
        return -1 if a.isdigit() else 1
    return (a > b) - (a < b)
assert sorted(['b', '2', 'a', '10'], key=cmp_to_key(numeric_then_alpha)) == ['10', '2', 'a', 'b']
assert sorted([3, 1, 2], key=cmp_to_key(lambda a, b: b - a)) == [3, 2, 1]
assert max(['aa', 'b', 'ccc'], key=cmp_to_key(lambda a, b: len(a) - len(b))) == 'ccc'
assert min([3, 1, 2], key=cmp_to_key(lambda a, b: a - b)) == 1
k = cmp_to_key(lambda a, b: a - b)
assert k(1) < k(2) and k(2) > k(1) and k(1) == k(1) and k(1) <= k(1) and k(3) >= k(2) and not (k(1) != k(1))
try:
    sorted([1, 2], key=cmp_to_key(lambda a, b: 'x'))
    assert False
except TypeError:
    pass

# ---- the module object ----
assert functools.reduce is reduce and functools.partial is partial
assert callable(functools.lru_cache) and callable(functools.cache) and callable(functools.wraps)
