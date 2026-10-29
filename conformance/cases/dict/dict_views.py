# Adapted from CPython's test suite (Lib/test/test_dictviews.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

d = {'b': 1, 'a': 2}

# ---- the three views: type, repr, size, truth ----
assert str(type(d.keys())) == "<class 'dict_keys'>"
assert str(type(d.values())) == "<class 'dict_values'>"
assert str(type(d.items())) == "<class 'dict_items'>"
assert repr(d.keys()) == "dict_keys(['b', 'a'])"
assert repr(d.values()) == 'dict_values([1, 2])'
assert repr(d.items()) == "dict_items([('b', 1), ('a', 2)])"
assert len(d.keys()) == len(d.values()) == len(d.items()) == 2
assert bool(d.keys()) is True and bool({}.keys()) is False

# ---- membership: values and items too, an item being the (key, value) pair ----
assert 'a' in d.keys() and 'z' not in d.keys()
assert 2 in d.values() and 3 not in d.values()
assert ('a', 2) in d.items()
assert ('a', 1) not in d.items()            # right key, wrong value
assert 'a' not in d.items()                 # a bare key is not a pair

# ---- iteration follows insertion order, and consuming a view does not exhaust it ----
assert [k for k in d.keys()] == ['b', 'a']
assert list(d.values()) == [1, 2]
assert list(d.items()) == [('b', 1), ('a', 2)]
ks = d.keys()
assert list(ks) == list(ks) == ['b', 'a']   # a view is re-iterable, unlike an iterator
assert sorted(d.keys()) == ['a', 'b']
assert sorted(set(d.values())) == [1, 2]

# ---- a view is a live window on the dict, not a snapshot taken at call time ----
live = {'a': 1}
k, v, it = live.keys(), live.values(), live.items()
live['b'] = 2
assert len(k) == len(v) == len(it) == 2
assert 'b' in k and 2 in v and ('b', 2) in it
live['a'] = 9                               # update is visible through the value/item views
assert sorted(v) == [2, 9]
assert sorted(it) == [('a', 9), ('b', 2)]
del live['a']
assert sorted(k) == ['b'] and 'a' not in k and len(it) == 1
live.clear()
assert len(k) == 0 and list(k) == []

# ---- what a view is NOT ----
try:
    d.keys()[0]
    raise AssertionError('subscript should fail')
except TypeError:
    pass

# ---- keys() and items() are set-like: operators with any iterable on either side, result is a set ----
assert d.keys() & {'a'} == {'a'}
assert d.keys() | {'z'} == {'a', 'b', 'z'}
assert d.keys() - {'a'} == {'b'}
assert d.keys() ^ {'a', 'z'} == {'b', 'z'}
assert d.keys() | ['c', 'c'] == {'a', 'b', 'c'}          # right operand: any iterable
assert ['a', 'q'] & d.keys() == {'a'}                    # left operand: any iterable
assert {'a'} | d.keys() == {'a', 'b'}
assert frozenset({'b'}) - d.keys() == set()
assert str(type({'a'} | d.keys())) == "<class 'set'>"    # never a frozenset, never a view
assert d.items() & {('a', 2), ('a', 3)} == {('a', 2)}
assert d.items() | {('c', 3)} == {('a', 2), ('b', 1), ('c', 3)}
assert d.keys() & d.keys() == {'a', 'b'}
assert d.keys() - d.items() == {'a', 'b'}
assert {'a': 1, 'c': 3}.keys() & d.keys() == {'a'}

# values() is not set-like
for op in ('and', 'or', 'sub', 'xor'):
    try:
        if op == 'and':
            d.values() & {1}
        elif op == 'or':
            d.values() | {1}
        elif op == 'sub':
            d.values() - {1}
        else:
            d.values() ^ {1}
        raise AssertionError('values() set operator should fail: ' + op)
    except TypeError:
        pass
try:
    d.keys() | 5                                         # not an iterable
    raise AssertionError('non-iterable operand should fail')
except TypeError:
    pass
try:
    {'a': []}.items() | set()                            # the pair ('a', []) is unhashable
    raise AssertionError('unhashable item should fail')
except TypeError:
    pass

# ---- equality: with sets and with each other, by contents ----
assert d.keys() == {'a', 'b'} and {'b', 'a'} == d.keys()
assert d.keys() == frozenset('ab')
assert d.items() == {('a', 2), ('b', 1)}
assert d.keys() == d.keys()
assert d.keys() == {'a': 0, 'b': 0}.keys()
assert d.items() == {'b': 1, 'a': 2}.items()             # order does not matter
assert d.items() != {'a': 1, 'b': 2}.items()
assert d.keys() != {'a'} and d.keys() != {'a', 'b', 'c'}
assert {}.keys() == set() and {}.keys() == {}.items()
assert (d.keys() == ['b', 'a']) is False                 # a list is not set-like
assert (d.keys() == d.items()) is False
assert {'a': []}.items() == {'a': []}.items()            # equality never hashes the pairs
assert d.values() != d.values()                          # values() compares by identity
v = d.values()
assert v == v
assert (d.values() == [1, 2]) is False

# ---- subset ordering ----
assert d.keys() <= {'a', 'b'} and d.keys() >= {'a', 'b'}
assert not (d.keys() < {'a', 'b'}) and not (d.keys() > {'a', 'b'})
assert d.keys() < {'a', 'b', 'c'} and {'a'} < d.keys()
assert {'a', 'b', 'c'} > d.keys() and d.keys() > {'a'}
assert d.keys() <= d.keys() and {'a': 0}.keys() < d.keys()
assert not (d.keys() <= {'a'}) and not (d.keys() < {'x', 'y', 'z'})
assert d.items() <= {('a', 2), ('b', 1), ('c', 3)}
assert {'a': []}.items() <= {'a': [], 'b': []}.items()
for bad in ([], d.values()):
    try:
        d.keys() < bad
        raise AssertionError('ordering against a non-set should fail')
    except TypeError:
        pass

# ---- isdisjoint takes any iterable ----
assert d.keys().isdisjoint({'x', 'y'}) and d.keys().isdisjoint([])
assert not d.keys().isdisjoint(['x', 'a'])
assert d.items().isdisjoint([('a', 1)]) and not d.items().isdisjoint([('a', 2)])
assert d.keys().isdisjoint({'c': 3}.keys()) and not d.keys().isdisjoint({'a': 3})
try:
    d.values().isdisjoint({1})
    raise AssertionError('values() has no isdisjoint')
except AttributeError:
    pass

# ---- views are still unhashable and still live after the algebra ----
try:
    {d.keys(): 1}
    raise AssertionError('views are unhashable')
except TypeError:
    pass
d['c'] = 3
assert d.keys() == {'a', 'b', 'c'} and d.keys() & {'c'} == {'c'}
del d['c']

# ---- reversed(): reverse insertion order for all three views ----
assert list(reversed(d.keys())) == ['a', 'b']
assert list(reversed(d.values())) == [2, 1]
assert list(reversed(d.items())) == [('a', 2), ('b', 1)]
assert list(reversed({}.keys())) == []
assert repr(reversed(d.keys())).startswith('<dict_reversekeyiterator')
assert repr(reversed(d.values())).startswith('<dict_reversevalueiterator')
assert repr(reversed(d.items())).startswith('<dict_reverseitemiterator')
assert list(reversed(d.keys())) == list(reversed(d))
rk = reversed(d.keys())
d['z'] = 0
try:
    next(rk)                                             # the dict changed under the iterator
    raise AssertionError('reversed view iterator should notice the change')
except RuntimeError:
    pass
del d['z']

# ---- views as ordinary iterables feed the usual builtins ----
assert dict(d.items()) == d
assert sorted(d.items()) == [('a', 2), ('b', 1)]
assert max(d.values()) == 2 and min(d.values()) == 1 and sum(d.values()) == 3
assert list(zip(d.keys(), d.values())) == list(d.items())
assert {k: val * 10 for k, val in d.items()} == {'b': 10, 'a': 20}
