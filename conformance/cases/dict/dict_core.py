# Adapted from CPython's test suite (Lib/test/test_dict.py, ordering cases from test_ordered_dict-era guarantees).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# insertion order is preserved (3.7+ language guarantee)
d = {}
d['b'] = 1
d['a'] = 2
d['c'] = 3
assert list(d) == ['b', 'a', 'c']
assert list(d.keys()) == ['b', 'a', 'c']
assert list(d.values()) == [1, 2, 3]
assert list(d.items()) == [('b', 1), ('a', 2), ('c', 3)]

# overwriting keeps the original position
d['a'] = 99
assert list(d) == ['b', 'a', 'c']
assert d['a'] == 99

# equality ignores order
assert {'a': 1, 'b': 2} == {'b': 2, 'a': 1}
assert {'a': 1} != {'a': 2}

# get / setdefault / pop
d = {'x': 1}
assert d.get('x') == 1
assert d.get('missing') is None
assert d.get('missing', 42) == 42
assert d.setdefault('x', 9) == 1
assert d.setdefault('y', 9) == 9
assert d == {'x': 1, 'y': 9}
assert d.pop('y') == 9
assert d.pop('nope', 'dflt') == 'dflt'
assert d == {'x': 1}

# pop on a missing key raises KeyError
raised = False
try:
    d.pop('nope')
except KeyError:
    raised = True
assert raised

# popitem is LIFO
d = {}
d['a'] = 1
d['b'] = 2
assert d.popitem() == ('b', 2)
assert d.popitem() == ('a', 1)

# update, in, len, del
d = {'a': 1}
d.update({'b': 2, 'a': 10})
assert d == {'a': 10, 'b': 2}
assert 'a' in d and 'z' not in d
assert len(d) == 2
del d['a']
assert d == {'b': 2}

# dict comprehension
sq = {n: n * n for n in range(4)}
assert sq == {0: 0, 1: 1, 2: 4, 3: 9}
inv = {v: k for k, v in {'a': 1, 'b': 2}.items()}
assert inv == {1: 'a', 2: 'b'}

# non-string keys, mixed keys, tuple keys
mixed = {1: 'one', (2, 3): 'pair', 'k': 'str'}
assert mixed[(2, 3)] == 'pair'
assert mixed[1] == 'one'

# iteration over items with unpacking
total = 0
for k, v in {'a': 1, 'b': 2}.items():
    total += v
assert total == 3

# dict() constructor forms
assert dict([('a', 1), ('b', 2)]) == {'a': 1, 'b': 2}
assert dict(a=1, b=2) == {'a': 1, 'b': 2}

# ---- mutating a dict while iterating it: CPython 3.8+ words the two cases differently ----
try:
    d = {'a': 1}
    for k in d:
        d['c'] = 3            # the size moved
    assert False
except RuntimeError as e:
    assert str(e) == 'dictionary changed size during iteration'
try:
    d = {'a': 1}
    for k in d:
        del d['a']            # ...and back to the same size, but the keys are not the same
        d['b'] = 2
    assert False
except RuntimeError as e:
    assert str(e) == 'dictionary keys changed during iteration'
