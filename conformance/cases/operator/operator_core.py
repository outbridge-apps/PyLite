# Adapted from CPython's test suite (Lib/test/test_operator.py: OperatorTestCase.test_add .. test_xor,
# test_itemgetter / test_attrgetter / test_methodcaller). Python Software Foundation, PSF License
# Version 2 -- see README.
import operator
from operator import itemgetter, attrgetter, methodcaller

assert operator.add(3, 4) == 7 and operator.sub(3, 4) == -1 and operator.mul(3, 4) == 12
assert operator.truediv(7, 2) == 3.5 and operator.floordiv(7, 2) == 3 and operator.mod(7, 3) == 1
assert operator.pow(2, 5) == 32 and operator.neg(5) == -5 and operator.pos(-5) == -5
assert operator.lshift(1, 3) == 8 and operator.rshift(8, 3) == 1 and operator.invert(5) == -6
assert operator.and_(6, 3) == 2 and operator.or_(6, 3) == 7 and operator.xor(6, 3) == 5
assert operator.concat('ab', 'cd') == 'abcd' and operator.concat([1], [2]) == [1, 2]
assert operator.abs(-3) == 3 and operator.not_(1) is False and operator.truth('x') is True
assert operator.lt(1, 2) and operator.le(2, 2) and operator.eq(1, 1) and operator.ne(1, 2)
assert operator.gt(2, 1) and operator.ge(2, 2) and not operator.gt(1, 1)
assert operator.is_(None, None) and operator.is_not(1, None)
assert operator.contains([1, 2, 3], 2) and not operator.contains('abc', 'z')
assert operator.countOf([1, 2, 1, 3, 1], 1) == 3 and operator.indexOf('hello', 'l') == 2
assert operator.getitem([1, 2, 3], 1) == 2 and operator.getitem({'k': 'v'}, 'k') == 'v'
assert operator.index(7) == 7
a = [1, 2, 3]
operator.setitem(a, 0, 9)
operator.delitem(a, 1)
assert a == [9, 3]
try:
    operator.indexOf([1], 2)
    assert False
except ValueError:
    pass
try:
    operator.index(1.5)
    assert False
except TypeError:
    pass

# ---- the getter factories ----
get1 = itemgetter(1)
assert get1([10, 20, 30]) == 20 and get1('abc') == 'b'
assert itemgetter(2, 0)([10, 20, 30]) == (30, 10)
assert itemgetter('name')({'name': 'x'}) == 'x'
assert sorted([{'n': 2}, {'n': 1}, {'n': 3}], key=itemgetter('n')) == [{'n': 1}, {'n': 2}, {'n': 3}]
assert list(map(itemgetter(0), [(1, 'a'), (2, 'b')])) == [1, 2]


class P:
    x: int
    y: int

    def __init__(self, x, y):
        self.x = x
        self.y = y


class Q:
    p: P

    def __init__(self, p):
        self.p = p


assert attrgetter('x')(P(3, 4)) == 3 and attrgetter('y', 'x')(P(3, 4)) == (4, 3)
assert attrgetter('p.x')(Q(P(5, 6))) == 5                    # dotted names walk
assert sorted([P(2, 0), P(1, 0)], key=attrgetter('x'))[0].x == 1
assert methodcaller('upper')('ab') == 'AB'
assert methodcaller('split', ',')('a,b') == ['a', 'b']
assert methodcaller('split', ',', maxsplit=1)('a,b,c') == ['a', 'b,c']
assert methodcaller('get', 'k', 'dflt')({}) == 'dflt'
for bad in (lambda: itemgetter(), lambda: attrgetter(3), lambda: methodcaller()):
    try:
        bad()
        assert False
    except TypeError:
        pass
