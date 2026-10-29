# Adapted from CPython's test suite (dict union operators, PEP 584 cases in Lib/test/test_dict.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

assert {'a': 1} | {'b': 2} == {'a': 1, 'b': 2}
assert {'a': 1} | {'a': 2} == {'a': 2}          # right side wins
merged = {'x': 1, 'y': 2} | {'y': 20, 'z': 3}
assert list(merged) == ['x', 'y', 'z']          # left order first, new keys appended
assert merged['y'] == 20

d = {'a': 1}
d |= {'b': 2}
assert d == {'a': 1, 'b': 2}
