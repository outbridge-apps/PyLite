# Adapted from CPython's test suite (Lib/test/test_collections.py Counter cases).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

from collections import Counter

# counting from an iterable
c = Counter('abracadabra')
assert c['a'] == 5
assert c['b'] == 2
assert c['z'] == 0                     # missing keys count as 0, no KeyError
assert len(c) == 5                     # distinct elements

# most_common
top = Counter('abracadabra').most_common(2)
assert top[0] == ('a', 5)
assert top[1][1] == 2                  # 'b' or 'r', both count 2

# construction from mapping and kwargs
assert Counter({'x': 3})['x'] == 3
assert Counter(x=2, y=1)['x'] == 2

# update adds, subtract subtracts
c = Counter('aab')
c.update('ab')
assert c['a'] == 3 and c['b'] == 2
c.subtract('aaaa')
assert c['a'] == -1                    # counts may go negative

# arithmetic drops non-positive results
a = Counter(x=3, y=1)
b = Counter(x=1, y=2)
assert (a + b) == Counter(x=4, y=3)
assert (a - b) == Counter(x=2)         # y went to -1 and was dropped
assert (a & b) == Counter(x=1, y=1)    # min
assert (a | b) == Counter(x=3, y=2)    # max

# elements() expands positive counts
assert sorted(Counter(a=2, b=1).elements()) == ['a', 'a', 'b']

# total? (3.10) - use sum of values as the portable form
assert sum(Counter('aab').values()) == 3

# a Counter converts to a plain dict (isinstance(Counter(), dict) is a documented
# deviation — see cases/deviations/isinstance_host_containers.py)
assert dict(Counter('ab')) == {'a': 1, 'b': 1}
