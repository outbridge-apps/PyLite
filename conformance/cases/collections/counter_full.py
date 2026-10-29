# Adapted from CPython's test suite (Lib/test/test_collections.py: TestCounter.test_basics/test_update/
# test_subtract/test_multiset_operations/test_unary/test_repr/test_copying/test_total).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

from collections import Counter

# ---- construction ----
c = Counter('abcaba')
assert c == {'a': 3, 'b': 2, 'c': 1}
assert c['a'] == 3 and c['z'] == 0                 # missing keys count as 0 without being inserted
assert 'z' not in c and len(c) == 3
assert Counter() == {} and Counter({'x': 2}) == {'x': 2} and Counter(x=2, y=1) == {'x': 2, 'y': 1}
assert Counter([1, 1, 2]) == {1: 2, 2: 1} and Counter({'a': 0}) == {'a': 0}
assert Counter(range(3)) == {0: 1, 1: 1, 2: 1}
assert Counter('aab') == Counter({'a': 2, 'b': 1}) == Counter(a=2, b=1)
assert isinstance(c, dict)

# ---- most_common / elements / total ----
assert c.most_common() == [('a', 3), ('b', 2), ('c', 1)]
assert c.most_common(2) == [('a', 3), ('b', 2)] and c.most_common(0) == [] and c.most_common(100) == c.most_common()
tie = Counter('abab')
assert tie.most_common() == [('a', 2), ('b', 2)]   # ties keep insertion order
assert sorted(c.elements()) == ['a', 'a', 'a', 'b', 'b', 'c']
assert list(Counter({'x': 2, 'y': 0, 'z': -1}).elements()) == ['x', 'x']   # non-positive counts are skipped
assert sum(c.values()) == 6 and c.total() == 6 and Counter().total() == 0 and Counter({'x': 2, 'y': -3}).total() == -1

# ---- update / subtract ----
u = Counter('ab')
u.update('abc')
assert u == {'a': 2, 'b': 2, 'c': 1}
u.update({'a': 10})
assert u['a'] == 12
u.update(b=5)
assert u['b'] == 7
u.update(Counter('cc'))
assert u['c'] == 3
u.update()
assert u == {'a': 12, 'b': 7, 'c': 3}
s = Counter('aab')
s.subtract('ab')
assert s == {'a': 1, 'b': 0}
s.subtract({'a': 5})
assert s['a'] == -4                                # counts may go negative
s.subtract(b=1, z=1)
assert s['b'] == -1 and s['z'] == -1

# ---- item assignment / deletion behave like a dict ----
d = Counter()
d['k'] = 5
d['k'] += 1
assert d['k'] == 6
del d['k']
assert d['k'] == 0 and 'k' not in d
try:
    del d['nothing']
    assert False
except KeyError:
    pass
d['neg'] = -2
assert d['neg'] == -2 and 'neg' in d

# ---- multiset operations ----
a = Counter('aab')
b = Counter('abb')
assert a + b == Counter('aabbab')
assert a - b == Counter('a')                       # negative results are dropped
assert b - a == Counter('b')
assert a & b == Counter('ab')                      # min
assert a | b == Counter('aabb')                    # max
assert (a + b)['a'] == 3 and (a | b)['b'] == 2
assert Counter('a') - Counter('a') == Counter()
assert Counter(x=-1) + Counter() == Counter()      # unary plus semantics: strips non-positive
assert +Counter(x=-1, y=1) == Counter(y=1)
assert -Counter(x=-1, y=1) == Counter(x=1)
assert a + Counter() == a and a & Counter() == Counter()

# in-place forms
p = Counter('aab')
p += Counter('b')
assert p == Counter('aabb')
p -= Counter('aa')
assert p == Counter('bb')
p &= Counter('b')
assert p == Counter('b')
p |= Counter('bbb')
assert p == Counter('bbb')

# comparisons are multiset inclusion (3.10+)
assert Counter('ab') <= Counter('aab') and Counter('ab') < Counter('aab')
assert Counter('aab') >= Counter('ab') and Counter('aab') > Counter('ab')
assert not (Counter('ab') < Counter('ab')) and Counter('ab') <= Counter('ab')
assert not (Counter('ab') <= Counter('bc'))
assert Counter('ab') == Counter('ba') and Counter('ab') != Counter('abb')
assert Counter('ab') == {'a': 1, 'b': 1}

# ---- dict API still available ----
assert sorted(c.keys()) == ['a', 'b', 'c'] and sorted(c.items()) == [('a', 3), ('b', 2), ('c', 1)]
assert c.get('a') == 3 and c.get('z') is None and c.get('z', 7) == 7
assert dict(c) == {'a': 3, 'b': 2, 'c': 1}
cp = c.copy()
cp['a'] = 0
assert c['a'] == 3 and isinstance(cp, Counter)
assert c.pop('c') == 1 and 'c' not in c
c.setdefault('q', 4)
assert c['q'] == 4
assert Counter.fromkeys is not None
try:
    Counter.fromkeys('ab')
    assert False
except NotImplementedError:
    pass

# ---- repr ----
assert repr(Counter()) == 'Counter()'
assert repr(Counter('a')) == "Counter({'a': 1})"
assert repr(Counter('ab')) in ("Counter({'a': 1, 'b': 1})", "Counter({'b': 1, 'a': 1})")
assert repr(Counter('aab')).startswith("Counter({'a': 2")

# ---- idioms ----
words = 'the cat and the hat and the bat'.split()
wc = Counter(words)
assert wc.most_common(1) == [('the', 3)]
assert wc['and'] == 2
assert Counter(w[0] for w in words) == Counter('tcathatb')
assert Counter(len(w) for w in words) == {3: 8}
letters = Counter('mississippi')
assert letters.most_common(2) == [('i', 4), ('s', 4)]
assert sorted(letters.most_common()) == [('i', 4), ('m', 1), ('p', 2), ('s', 4)]
