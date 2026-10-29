# Adapted from CPython's test suite (Lib/test/test_patma.py: TestPatma literal/capture/sequence/mapping/
# class/or/as/guard cases). Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

def lit(v):
    match v:
        case True:                 # True/False/None match by identity, so they go first
            return 'true'
        case None:
            return 'none'
        case 0:
            return 'zero'
        case 1 | 2 | 3:
            return 'small'
        case -1:
            return 'neg'
        case 1.5:
            return 'float'
        case 'txt':
            return 'str'
        case _:
            return 'other'

assert lit(0) == 'zero'
assert lit(2) == 'small'
assert lit(-1) == 'neg'
assert lit(1.5) == 'float'
assert lit('txt') == 'str'
assert lit(True) == 'true'
assert lit(1) == 'small'                   # 1 is not True (identity), so it reaches the int literal
assert lit(False) == 'zero'                # False == 0 by equality
assert lit(None) == 'none'
assert lit(99) == 'other'
assert lit(1.0) == 'small'                 # literal patterns compare with ==

# ---- capture and wildcard ----
def cap(v):
    match v:
        case 0:
            return 'zero'
        case x:
            return ('captured', x)
assert cap(0) == 'zero'
assert cap('a') == ('captured', 'a')

# ---- sequence patterns ----
def seq(v):
    match v:
        case []:
            return 'empty'
        case [x]:
            return ('one', x)
        case [x, y]:
            return ('two', x, y)
        case [x, *rest]:
            return ('many', x, rest)
assert seq([]) == 'empty'
assert seq((7,)) == ('one', 7)             # tuples match sequence patterns too
assert seq([1, 2]) == ('two', 1, 2)
assert seq([1, 2, 3, 4]) == ('many', 1, [2, 3, 4])
assert seq('ab') is None                   # str is NOT a sequence for patterns

def star_mid(v):
    match v:
        case [first, *mid, last]:
            return (first, mid, last)
        case _:
            return None
assert star_mid([1, 2, 3, 4]) == (1, [2, 3], 4)
assert star_mid([1, 2]) == (1, [], 2)
assert star_mid([1]) is None

def nested(v):
    match v:
        case [[a, b], [c, d]]:
            return a + b + c + d
        case [(a, b), *_]:
            return a * b
        case _:
            return None
assert nested([[1, 2], [3, 4]]) == 10
assert nested([(3, 4), 'x', 'y']) == 12

def with_literal(v):
    match v:
        case ['add', x, y]:
            return x + y
        case ['neg', x]:
            return -x
        case ['neg', *_]:
            return 'bad-neg'
assert with_literal(['add', 2, 3]) == 5
assert with_literal(['neg', 2]) == -2
assert with_literal(['neg']) == 'bad-neg'
assert with_literal(['mul', 1, 2]) is None

# ---- mapping patterns ----
def mp(v):
    match v:
        case {'type': 'point', 'x': x, 'y': y}:
            return ('point', x, y)
        case {'type': 'circle', 'r': r, **rest}:
            return ('circle', r, rest)
        case {}:
            return 'any-mapping'
        case _:
            return 'not-mapping'
assert mp({'type': 'point', 'x': 1, 'y': 2}) == ('point', 1, 2)
assert mp({'type': 'point', 'x': 1, 'y': 2, 'extra': 0}) == ('point', 1, 2)   # extra keys ignored
assert mp({'type': 'circle', 'r': 5, 'c': 'red'}) == ('circle', 5, {'c': 'red'})
assert mp({'other': 1}) == 'any-mapping'
assert mp({}) == 'any-mapping'
assert mp([1, 2]) == 'not-mapping'
assert mp('str') == 'not-mapping'

def mp_nested(v):
    match v:
        case {'user': {'name': nm, 'tags': [first_tag, *_]}}:
            return (nm, first_tag)
    return None
assert mp_nested({'user': {'name': 'ann', 'tags': ['a', 'b']}}) == ('ann', 'a')
assert mp_nested({'user': {'name': 'ann', 'tags': []}}) is None

# ---- value patterns (dotted names) ----
# dialect: class bodies hold only defs and annotations, so the constants live on an instance
class Palette:
    def __init__(self):
        self.RED = 1
        self.GREEN = 2
Color = Palette()

import math
def val(v):
    match v:
        case Color.RED:
            return 'red'
        case Color.GREEN:
            return 'green'
        case math.pi:
            return 'pi'
        case _:
            return '?'
assert val(1) == 'red' and val(2) == 'green' and val(math.pi) == 'pi' and val(3) == '?'

# ---- class patterns ----
# dialect: __match_args__ comes from @dataclass field order (class bodies cannot assign it)
from dataclasses import dataclass

@dataclass
class Point:
    x: int
    y: int

@dataclass
class Point3(Point):
    z: int

def cls(v):
    match v:
        case Point(x=0, y=0):
            return 'origin'
        case Point(0, y):
            return ('on-y-axis', y)
        case Point(x, 0):
            return ('on-x-axis', x)
        case Point(x, y) if x == y:
            return ('diagonal', x)
        case Point():
            return 'point'
        case _:
            return 'not-a-point'
assert cls(Point(0, 0)) == 'origin'
assert cls(Point(0, 5)) == ('on-y-axis', 5)
assert cls(Point(5, 0)) == ('on-x-axis', 5)
assert cls(Point(3, 3)) == ('diagonal', 3)
assert cls(Point(1, 2)) == 'point'
assert cls(Point3(0, 0, 9)) == 'origin'     # subclass instances match the base pattern
assert cls(42) == 'not-a-point'

def cls_kw(v):
    match v:
        case Point3(z=zz, x=xx):
            return (xx, zz)
        case Point(x=xx):
            return xx
assert cls_kw(Point3(1, 2, 3)) == (1, 3)
assert cls_kw(Point(7, 8)) == 7

# builtin class patterns: isinstance check; int/str/list/... take one positional sub-pattern matching the
# subject itself, other builtins (exceptions) accept keyword sub-patterns only
def builtin(v):
    match v:
        case int(i) if i > 5:
            return ('big', i)
        case bool():
            return 'bool'
        case int():
            return 'int'
        case str(s):
            return ('str', s)
        case [int(a), str(b)]:
            return ('pair', a, b)
        case dict():
            return 'dict'
        case ValueError():
            return 'verr'
        case _:
            return 'none'
assert builtin(7) == ('big', 7) and builtin(True) == 'bool' and builtin(1) == 'int'
assert builtin('a') == ('str', 'a') and builtin([1, 'b']) == ('pair', 1, 'b') and builtin({}) == 'dict'
assert builtin(ValueError()) == 'verr' and builtin(2.5) == 'none' and builtin(None) == 'none'
def two_positional(v):
    match v:
        case int(a, b):
            return 1
try:
    two_positional(1)
    assert False
except TypeError:
    pass
def exc_positional(v):
    match v:
        case ValueError(x):
            return 1
try:
    exc_positional(ValueError())
    assert False
except TypeError:
    pass

# ---- or patterns with captures, as patterns ----
def orp(v):
    match v:
        case [x, 0] | [0, x]:
            return x
        case ('a' | 'b') as letter:
            return letter.upper()
        case [1, 2] as whole:
            return whole
    return None
assert orp([5, 0]) == 5 and orp([0, 6]) == 6
assert orp('a') == 'A' and orp('b') == 'B'
assert orp([1, 2]) == [1, 2]
assert orp('c') is None

def as_nested(v):
    match v:
        case [(1 | 2) as small, *rest] as all_items:
            return (small, rest, all_items)
    return None
assert as_nested([2, 3]) == (2, [3], [2, 3])

# ---- guards ----
def guard(v):
    match v:
        case x if x > 100:
            return 'big'
        case x if x > 10:
            return 'medium'
        case x if x < 0:
            return 'negative'
        case _:
            return 'small'
assert guard(500) == 'big' and guard(50) == 'medium' and guard(-1) == 'negative' and guard(3) == 'small'

def guard_uses_capture(v):
    match v:
        case [a, b] if a + b == 10:
            return 'ten'
        case [a, b]:
            return a + b
    return None
assert guard_uses_capture([4, 6]) == 'ten' and guard_uses_capture([1, 2]) == 3

# ---- no match at all: falls through silently ----
def nothing(v):
    match v:
        case 1:
            return 1
    return 'fell-through'
assert nothing(2) == 'fell-through'

# ---- captured names persist after the match ----
match [1, 2]:
    case [p1, p2]:
        pass
assert p1 == 1 and p2 == 2

# match on a computed subject, with a tuple literal subject
match (1, 'x'):
    case (1, 'x'):
        got = 'tuple'
    case _:
        got = None
assert got == 'tuple'
