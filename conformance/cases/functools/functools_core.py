# Adapted from CPython's test suite (Lib/test/test_functools.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import functools
from functools import reduce, partial

# reduce with and without an initializer
assert reduce(lambda a, b: a + b, [1, 2, 3, 4]) == 10
assert reduce(lambda a, b: a + b, [1, 2, 3], 100) == 106
assert reduce(lambda a, b: a * b, [], 7) == 7            # empty + initializer -> initializer
raised = False
try:
    reduce(lambda a, b: a + b, [])                        # empty, no initializer
except TypeError:
    raised = True
assert raised
assert reduce(lambda a, b: a + b, [5]) == 5               # single element, no call at all

# partial freezes positional and keyword args
def power(base, exp, scale=1):
    return base ** exp * scale
square = partial(power, exp=2)
assert square(5) == 25
assert square(5, scale=2) == 50
add10 = partial(lambda a, b: a + b, 10)
assert add10(5) == 15

# lru_cache memoizes (observable via a side-effect counter)
calls = []
@functools.lru_cache(maxsize=None)
def slow_double(x):
    calls.append(x)
    return x * 2
assert slow_double(2) == 4
assert slow_double(2) == 4
assert slow_double(3) == 6
assert calls == [2, 3]                                     # the repeat was served from cache

# cache (3.9 alias)
calls2 = []
@functools.cache
def fib(n):
    calls2.append(n)
    return n if n < 2 else fib(n - 1) + fib(n - 2)
assert fib(10) == 55
assert len(calls2) == 11                                   # linear, not exponential

# cmp_to_key adapts an old-style comparator
def by_length_then_alpha(a, b):
    if len(a) != len(b):
        return len(a) - len(b)
    if a < b:
        return -1
    if a > b:
        return 1
    return 0
words = ['bb', 'a', 'ab', 'c']
assert sorted(words, key=functools.cmp_to_key(by_length_then_alpha)) == ['a', 'c', 'ab', 'bb']
