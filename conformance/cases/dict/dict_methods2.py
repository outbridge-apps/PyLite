# Adapted from CPython's test suite (Lib/test/test_dict.py method cases).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# fromkeys
assert dict.fromkeys(['a', 'b']) == {'a': None, 'b': None}
assert dict.fromkeys('ab', 0) == {'a': 0, 'b': 0}
shared = dict.fromkeys(['x', 'y'], [])
shared['x'].append(1)
assert shared['y'] == [1]            # the default value is SHARED (the classic)

# copy() is shallow
d = {'k': [1]}
e = d.copy()
e['k'].append(2)
assert d['k'] == [1, 2]
e['new'] = 1
assert 'new' not in d

# clear()
f = {'a': 1}
f.clear()
assert f == {}

# get never raises, [] does
g = {'a': 1}
assert g.get('b') is None
raised = False
try:
    _ = g['b']
except KeyError:
    raised = True
assert raised

# update accepts pairs and keywords
h = {'a': 1}
h.update([('b', 2)])
h.update(c=3)
assert h == {'a': 1, 'b': 2, 'c': 3}

# setdefault with a mutable default builds grouped structures
groups = {}
for word in ['apple', 'avocado', 'banana']:
    groups.setdefault(word[0], []).append(word)
assert groups == {'a': ['apple', 'avocado'], 'b': ['banana']}

# in checks keys, not values
i = {'k': 'v'}
assert 'k' in i and 'v' not in i

# len after add/delete
j = {}
j['x'] = 1
j['y'] = 2
del j['x']
assert len(j) == 1

# values may repeat, keys are unique
k = {'a': 1, 'b': 1}
assert len(k) == 2
assert sorted(k.values()) == [1, 1]

# bool keys collide with int keys (True == 1)
m = {1: 'one'}
m[True] = 'yes'
assert m == {1: 'yes'}
assert len(m) == 1

# iteration order after deletes: remaining keys keep relative order
n = {}
for ch in 'abcd':
    n[ch] = 0
del n['b']
assert list(n) == ['a', 'c', 'd']
n['b'] = 0                            # re-insertion goes to the END
assert list(n) == ['a', 'c', 'd', 'b']
