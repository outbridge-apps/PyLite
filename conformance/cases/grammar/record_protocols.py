# User-defined protocol methods on record classes. Adapted from the CPython test suite
# (Lib/test/test_class.py, test_descr.py, test_dataclasses.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Dialect: a record class may define these; everything else (__add__, __getattr__, __enter__, ...)
# is a SyntaxError, and equality without __eq__ stays structural (see docs/deviations.md).

from dataclasses import dataclass

# ---- __str__: str() and format use it, repr() keeps the generated one ----

class S:
    def __init__(self, v):
        self.v = v
    def __str__(self):
        return 'S<' + str(self.v) + '>'

assert str(S(1)) == 'S<1>' and f'{S(2)}' == 'S<2>' and '{}'.format(S(3)) == 'S<3>'
assert '%s' % S(4) == 'S<4>'
assert repr(S(1)) == 'S(v=1)' and f'{S(1)!r}' == 'S(v=1)'
assert str([S(1)]) == '[S(v=1)]'                      # a container renders its items with repr

# ---- __hash__ with __eq__: the record becomes a key ----

class H:
    def __init__(self, k):
        self.k = k
    def __eq__(self, o):
        return self.k == o.k
    def __hash__(self):
        return hash(self.k)

assert {H(1): 'x'}[H(1)] == 'x'
assert len({H(1), H(1), H(2)}) == 2
assert H(1) in {H(1)} and hash(H(5)) == hash(5)
assert {H(1): 1}.get(H(2), 'miss') == 'miss'

# A bare class hashes by identity, as in CPython. What cannot have a hash is FIELD equality over
# mutable slots, and there CPython agrees: @dataclass(eq=True, frozen=False) sets __hash__ to None.
class P:
    def __init__(self, v):
        self.v = v
assert len({P(1), P(1)}) == 2

@dataclass
class Mutable:
    v: int
try:
    {Mutable(1)}
    assert False
except TypeError:
    pass

# a bare class that WRITES __eq__ loses the hash, which is the data model's rule for any class
class Q:
    def __init__(self, v):
        self.v = v
    def __eq__(self, o):
        return self.v == o.v
assert Q(1) == Q(1)
try:
    {Q(1)}
    assert False
except TypeError:
    pass

# an explicit __hash__ survives @dataclass, frozen or not, as it does in CPython
@dataclass(frozen=True)
class FH:
    v: int
    def __hash__(self):
        return 999
assert hash(FH(1)) == 999

@dataclass
class DH:
    v: int
    def __hash__(self):
        return self.v
assert {DH(3): 'y'}[DH(3)] == 'y'

# ---- __bool__ / __len__ ----

class B:
    def __init__(self, b):
        self.b = b
    def __bool__(self):
        return self.b

class L:
    def __init__(self, n):
        self.n = n
    def __len__(self):
        return self.n

assert bool(B(False)) is False and bool(B(True)) is True
assert ('y' if B(False) else 'n') == 'n'
assert len(L(3)) == 3 and bool(L(0)) is False and bool(L(2)) is True
assert bool(P(1)) is True                              # neither method: always true
try:
    len(L(-1))
    assert False
except ValueError:
    pass

# ---- ordering: the left operand's method, else the right one's reflected ----

class V:
    def __init__(self, n):
        self.n = n
    def __lt__(self, o):
        return self.n < o.n
    def __repr__(self):
        return 'V' + str(self.n)

assert V(1) < V(2) and not (V(2) < V(1))
assert repr(sorted([V(3), V(1), V(2)])) == '[V1, V2, V3]'
assert repr(min([V(3), V(1)])) == 'V1' and repr(max([V(3), V(1)])) == 'V3'
assert V(2) > V(1) and not (V(1) > V(2))               # reflected: V(1).__lt__(V(2))
try:
    V(1) <= V(2)                                       # no __le__, no reflected __ge__
    assert False
except TypeError:
    pass

# __ne__ answers the OPERATOR only; membership and container comparison go through __eq__
class N:
    def __init__(self, n):
        self.n = n
    def __eq__(self, o):
        return True
    def __ne__(self, o):
        return True
assert (N(1) != N(2)) is True and (N(1) == N(2)) is True
assert N(1) in [N(2)] and [N(1)] == [N(2)]

# order=True generates all four comparisons, so CPython refuses to drop a written one
try:
    @dataclass(order=True)
    class Clash:
        v: int
        def __lt__(self, o):
            return True
    assert False
except TypeError:
    pass

# ---- __call__ ----

class C:
    def __init__(self, base):
        self.base = base
    def __call__(self, x, k=0):
        return self.base + x + k

assert C(10)(5) == 15 and C(10)(5, 1) == 16 and C(10)(5, k=2) == 17
assert callable(C(1)) and not callable(P(1))
assert list(map(C(100), [1, 2])) == [101, 102]
assert sorted([3, 1], key=C(0)) == [1, 3]

# ---- __getitem__ / __setitem__ / __delitem__ / __contains__ ----

class Map:
    def __init__(self):
        self.d = {}
    def __getitem__(self, k):
        return self.d[k]
    def __setitem__(self, k, v):
        self.d[k] = v
    def __delitem__(self, k):
        del self.d[k]
    def __contains__(self, k):
        return k in self.d
    def __len__(self):
        return len(self.d)

m = Map()
m['a'] = 1
m[(1, 2)] = 'tuple key'
assert m['a'] == 1 and m[(1, 2)] == 'tuple key'
assert 'a' in m and 'zz' not in m and len(m) == 2
del m['a']
assert len(m) == 1 and 'a' not in m
try:
    m['zz']                                            # the hook's own error surfaces
    assert False
except KeyError:
    pass

# ---- __iter__ / __next__ / __reversed__ ----

class Count:
    def __init__(self, n):
        self.n = n
        self.i = 0
    def __iter__(self):
        self.i = 0
        return self
    def __next__(self):
        if self.i >= self.n:
            raise StopIteration
        self.i = self.i + 1
        return self.i - 1

assert list(Count(3)) == [0, 1, 2] and tuple(Count(2)) == (0, 1) and list(Count(0)) == []
assert [x * 2 for x in Count(3)] == [0, 2, 4] and sum(Count(4)) == 6
assert max(Count(5)) == 4 and 2 in Count(5)
assert list(enumerate(Count(2))) == [(0, 0), (1, 1)]
assert [(a, b) for a in Count(2) for b in Count(2)] == [(0, 0), (0, 1), (1, 0), (1, 1)]

class W:
    def __init__(self, xs):
        self.xs = xs
    def __iter__(self):
        return iter(self.xs)
    def __reversed__(self):
        return iter(self.xs[::-1])
assert list(W([1, 2])) == [1, 2] and list(reversed(W([1, 2, 3]))) == [3, 2, 1]

# the legacy sequence protocol: only __getitem__, indexed from 0 until IndexError
class Legacy:
    def __init__(self, n):
        self.n = n
    def __getitem__(self, i):
        if i >= self.n:
            raise IndexError(i)
        return i * 10
assert list(Legacy(3)) == [0, 10, 20] and 20 in Legacy(5)

# what __iter__ returns must BE an iterator, as in CPython
class BadIter:
    def __init__(self):
        pass
    def __iter__(self):
        return [1, 2]
try:
    list(BadIter())
    assert False
except TypeError:
    pass

# ---- id(): identity, the same relation `is` reports ----

a = P(1)
b = P(1)
c = a
assert id(a) == id(a) and id(a) == id(c) and id(a) != id(b)
assert (id(a) == id(b)) == (a is b) and (id(a) == id(c)) == (a is c)
assert id(None) == id(None) and id(True) == id(True)
assert isinstance(id(a), int) and {id(a): 'x'}[id(a)] == 'x'
lst = [1]
assert id(lst) == id(lst) and id([1]) != id([1])

# @dataclass(eq=False) keeps identity on BOTH sides, as in CPython: no generated __eq__ means the
# inherited identity equality AND the identity hash, so the key survives a field assignment. It wins
# over frozen, which only decides whether a field hash would be safe.
@dataclass(eq=False)
class NoEq:
    v: int
n = NoEq(1)
assert NoEq(1) != NoEq(1) and n == n
assert {n: 'x'}[n] == 'x' and len({NoEq(1), NoEq(1)}) == 2 and len({n, n}) == 1
m = {n: 'kept'}
n.v = 99
assert m[n] == 'kept'

@dataclass(eq=False, frozen=True)
class NoEqFrozen:
    v: int
assert len({NoEqFrozen(1), NoEqFrozen(1)}) == 2      # identity, not the field values

# a written __eq__ still costs the hash, eq=False or not
@dataclass(eq=False)
class NoEqButEq:
    v: int
    def __eq__(self, o):
        return True
try:
    {NoEqButEq(1)}
    assert False
except TypeError:
    pass
