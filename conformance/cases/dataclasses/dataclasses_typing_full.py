# Adapted from CPython's test suite (Lib/test/test_dataclasses.py: TestCase.test_one_field/test_named_init_params/
# test_field_default/test_default_factory/test_frozen/test_compare/test_fields/test_asdict/test_inheritance,
# Lib/test/test_typing.py: TypeVar/NewType/cast/get_origin/get_args basics, Lib/test/test_copyreg.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Dialect (RFC-003 §2.10): @dataclass(init=, repr=, eq=, order=, frozen=, kw_only=) + field(default=,
# default_factory=, init=, repr=, compare=, kw_only=, hash=, metadata=) + fields() + asdict() + replace()
# + __post_init__. Field.type is the annotation NAME as a string.

from dataclasses import dataclass, field, fields, asdict, replace, MISSING
import typing
from typing import TYPE_CHECKING, cast, assert_type, TypeVar, ParamSpec, TypeVarTuple, NewType, NamedTuple, get_type_hints, get_args, get_origin
import copyreg

# ================= dataclasses =================
@dataclass
class Point:
    x: int
    y: int = 0

p = Point(1, 2)
assert p.x == 1 and p.y == 2 and Point(1).y == 0 and Point(y=5, x=3) == Point(3, 5) and Point(1, y=2) == p
assert repr(p) == 'Point(x=1, y=2)' and str(p) == 'Point(x=1, y=2)' and p == Point(1, 2) and p != Point(2, 1) and p != (1, 2)
assert [f.name for f in fields(p)] == ['x', 'y'] and [f.type for f in fields(Point)] == ['int', 'int']
assert fields(Point)[0].default is MISSING and fields(Point)[1].default == 0 and fields(Point)[0].default_factory is MISSING
assert asdict(p) == {'x': 1, 'y': 2} and list(asdict(p)) == ['x', 'y'] and isinstance(asdict(p), dict)
p.x = 10
assert p.x == 10 and asdict(p) == {'x': 10, 'y': 2}
try:
    Point()
    assert False
except TypeError:
    pass
try:
    Point(1, 2, 3)
    assert False
except TypeError:
    pass
try:
    Point(1, z=2)
    assert False
except TypeError:
    pass
try:
    p.z = 1
    assert False
except AttributeError:
    pass
try:
    hash(p)                                              # a mutable dataclass is unhashable
    assert False
except TypeError:
    pass

# default_factory and mutable defaults
@dataclass
class Bag:
    items: list = field(default_factory=list)
    tags: dict = field(default_factory=dict)
    name: str = 'bag'

b1, b2 = Bag(), Bag()
assert b1.items == [] and b1.items is not b2.items and b1.tags is not b2.tags and b1.name == 'bag'
b1.items.append(1)
assert b2.items == [] and Bag([9]).items == [9] and Bag(name='x').name == 'x' and asdict(b1) == {'items': [1], 'tags': {}, 'name': 'bag'}
assert [f.name for f in fields(Bag)] == ['items', 'tags', 'name'] and Bag(tags={'k': 1}).tags == {'k': 1}
counter = [0]
def next_id():
    counter[0] += 1
    return counter[0]

@dataclass
class Ticket:
    id: int = field(default_factory=next_id)
    note: str = field(default='')

t1, t2 = Ticket(), Ticket()
assert (t1.id, t2.id) == (1, 2) and Ticket(9).id == 9 and t1.note == '' and Ticket(note='n').note == 'n'
try:
    field(default=1, default_factory=list)
    assert False
except ValueError:
    pass
try:
    @dataclass
    class NoDefaultAfterDefault:
        a: int = 1
        b: int
    assert False
except TypeError:
    pass

# frozen dataclasses are immutable and hashable
@dataclass(frozen=True)
class Key:
    a: int
    b: str = 'x'

k = Key(1)
assert k.a == 1 and k.b == 'x' and k == Key(1, 'x') and hash(k) == hash(Key(1)) and {k: 'v'}[Key(1)] == 'v' and k in {Key(1)}
assert len({Key(1), Key(1), Key(2)}) == 2 and repr(k) == "Key(a=1, b='x')" and hash(Key(1)) != hash(Key(2))
try:
    k.a = 2
    assert False
except AttributeError:
    pass
try:
    k.c = 2
    assert False
except AttributeError:
    pass

# init=False keeps the class as a plain record class with explicit construction
@dataclass(init=False)
class Manual:
    v: int = 0

    def __init__(self, raw):
        self.v = int(raw) * 2

assert Manual('21').v == 42 and Manual('1') == Manual('1') and repr(Manual('1')) == 'Manual(v=2)'

# the eq / order / repr knobs and __post_init__
@dataclass(order=True)
class Ver:
    major: int
    minor: int = 0

assert Ver(1, 2) == Ver(1, 2) and Ver(1) != Ver(2)
assert Ver(1, 2) < Ver(1, 3) and Ver(2) > Ver(1, 9) and Ver(1) <= Ver(1) and sorted([Ver(2), Ver(1, 5), Ver(1)]) == [Ver(1), Ver(1, 5), Ver(2)]
try:
    Ver(1) < Point(1)
    assert False
except TypeError:
    pass
try:
    dataclass(order=True, eq=False)
    assert False
except ValueError:
    pass

@dataclass(eq=False, repr=False)
class Ident:
    a: int

same = Ident(1)
assert Ident(1) != Ident(1) and same == same and repr(same) == '<Ident object>'

@dataclass
class Normalized:
    name: str
    tags: list = field(default_factory=list)
    def __post_init__(self):
        self.name = self.name.strip().lower()
        self.tags.append('seen')

assert Normalized('  Bob ').name == 'bob' and Normalized('x').tags == ['seen']

# methods and nesting
@dataclass
class Line:
    start: Point
    end: Point
    label: str = ''

    def length2(self):
        return (self.end.x - self.start.x) ** 2 + (self.end.y - self.start.y) ** 2

    def with_label(self, label):
        return Line(self.start, self.end, label)

ln = Line(Point(0, 0), Point(3, 4))
assert ln.length2() == 25 and ln.with_label('d').label == 'd' and ln.label == '' and asdict(ln) == {'start': {'x': 0, 'y': 0}, 'end': {'x': 3, 'y': 4}, 'label': ''}
assert repr(ln) == "Line(start=Point(x=0, y=0), end=Point(x=3, y=4), label='')" and ln == Line(Point(0, 0), Point(3, 4))

# inheritance keeps base fields first
@dataclass
class Base:
    a: int
    b: int = 1

@dataclass
class Derived(Base):
    c: int = 2

d = Derived(0)
assert (d.a, d.b, d.c) == (0, 1, 2) and Derived(5, 6, 7) == Derived(5, 6, 7) and [f.name for f in fields(Derived)] == ['a', 'b', 'c']
assert isinstance(d, Base) and repr(d) == 'Derived(a=0, b=1, c=2)' and asdict(d) == {'a': 0, 'b': 1, 'c': 2}

assert Point.__match_args__ == ('x', 'y') and Point(1, 2).__match_args__ == ('x', 'y')
class Plain:
    n: int
    def __init__(self, n):
        self.n = n
try:
    _ = Plain.__match_args__                                     # a plain class has no __match_args__
    assert False
except AttributeError:
    pass
# match statements use field order as __match_args__
def where(pt):
    match pt:
        case Point(0, 0):
            return 'origin'
        case Point(x=0):
            return 'y-axis'
        case Point(x, y) if x == y:
            return 'diag'
        case Point():
            return 'point'
    return 'other'
assert where(Point(0, 0)) == 'origin' and where(Point(0, 5)) == 'y-axis' and where(Point(3, 3)) == 'diag' and where(Point(1, 2)) == 'point' and where(5) == 'other'

# containers of dataclasses
pts = [Point(2, 1), Point(1, 2), Point(1, 1)]
assert sorted(pts, key=lambda q: (q.x, q.y)) == [Point(1, 1), Point(1, 2), Point(2, 1)] and Point(1, 2) in pts and pts.index(Point(1, 1)) == 2
assert [asdict(q) for q in pts][0] == {'x': 2, 'y': 1} and {Key(1): 'a', Key(2): 'b'}[Key(2)] == 'b'
try:
    fields(5)
    assert False
except TypeError:
    pass
try:
    asdict(5)
    assert False
except TypeError:
    pass
try:
    dataclass(5)
    assert False
except TypeError:
    pass


# ---- field(init=, repr=, compare=, kw_only=, hash=, metadata=) and @dataclass(kw_only=) ----
@dataclass
class Knobs:
    a: int
    b: int = field(default=0, repr=False, compare=False)
    c: int = field(default=7, init=False)

assert repr(Knobs(1)) == 'Knobs(a=1, c=7)'                 # repr=False hides b
assert Knobs(1, 2) == Knobs(1, 3) and Knobs(1) != Knobs(2)  # compare=False drops b from __eq__
assert Knobs(1).c == 7 and Knobs(1, 5).b == 5
for bad in (lambda: Knobs(1, 2, 3), lambda: Knobs(1, c=3)):
    try:
        bad()
        assert False
    except TypeError:
        pass
assert [(f.name, f.init, f.repr, f.compare, f.kw_only, f.hash) for f in fields(Knobs)] == [
    ('a', True, True, True, False, None), ('b', True, False, False, False, None), ('c', False, True, True, False, None)]
assert fields(Knobs)[0].metadata == {} and fields(Knobs)[1].default == 0

@dataclass(kw_only=True)
class KwOnly:
    a: int
    b: int = 2

assert KwOnly(a=1) == KwOnly(a=1, b=2) and repr(KwOnly(a=1, b=3)) == 'KwOnly(a=1, b=3)'
try:
    KwOnly(1)
    assert False
except TypeError:
    pass

@dataclass
class Mixed:
    a: int = 1
    b: int = field(kw_only=True)          # no default AFTER a default: legal when keyword-only

assert Mixed(b=5) == Mixed(a=1, b=5) and Mixed(2, b=5).a == 2
try:
    Mixed()
    assert False
except TypeError as e:
    assert 'keyword-only' in str(e)

# compare=False also leaves the frozen hash and the ordering
@dataclass(frozen=True)
class FrozenKnob:
    a: int
    b: int = field(default=0, compare=False)

assert hash(FrozenKnob(1, 2)) == hash(FrozenKnob(1, 3)) and FrozenKnob(1, 2) == FrozenKnob(1, 3)

@dataclass(order=True)
class OrderKnob:
    a: int
    b: int = field(default=0, compare=False)

assert OrderKnob(1, 5) < OrderKnob(2, 0) and not (OrderKnob(1, 5) < OrderKnob(1, 9))

# unknown keywords are refused, not silently ignored
for bad in (lambda: field(nope=1), lambda: dataclass(nope=1), lambda: field(1)):
    try:
        bad()
        assert False
    except TypeError:
        pass

# ---- replace() ----
assert replace(Point(1, 2), y=5) == Point(1, 5) and replace(Point(1, 2)) == Point(1, 2)
assert replace(Point(1, 2), x=9, y=9) == Point(9, 9)
r_orig = Point(1, 2)
assert replace(r_orig, y=5) is not r_orig and r_orig.y == 2      # the original is untouched
try:
    replace(Point(1, 2), z=1)
    assert False
except TypeError:
    pass
try:
    replace(Knobs(1), c=2)                                        # init=False cannot be replaced
    assert False
except ValueError:
    pass
try:
    replace(5, a=1)
    assert False
except TypeError:
    pass

@dataclass
class Post:
    a: int
    doubled: int = field(init=False, default=0)

    def __post_init__(self):
        self.doubled = self.a * 2

assert Post(3).doubled == 6 and replace(Post(3), a=5).doubled == 10   # __post_init__ runs again
# ================= typing =================
assert TYPE_CHECKING is False
assert cast(int, 'not really') == 'not really' and cast('int', 5) == 5 and cast(list, None) is None
assert assert_type(5, int) == 5 and assert_type('x', str) == 'x'
T = TypeVar('T')
K = TypeVar('K', bound=str)
Ps = ParamSpec('P')
Ts = TypeVarTuple('Ts')
assert T is not None and K is not None and Ps is not None and Ts is not None and T != K
UserId = NewType('UserId', int)
assert UserId(5) == 5 and isinstance(UserId(5), int) and UserId(5) + 1 == 6 and callable(UserId)
Emp = NamedTuple('Emp', [('name', str), ('id', int)])
e = Emp('ann', 1)
assert e.name == 'ann' and e.id == 1 and e == ('ann', 1) and isinstance(e, tuple) and e._asdict() == {'name': 'ann', 'id': 1} and Emp._fields == ('name', 'id')
assert repr(e) == "Emp(name='ann', id=1)" and Emp(id=2, name='b') == ('b', 2)

def annotated(a: int, b: 'str' = 'x') -> bool:
    return True
assert get_type_hints(annotated) == {'a': 'int', 'return': 'bool'} and annotated(1) is True   # plain names only: 'str' (a string) is dropped
# dialect: a class keeps each field annotation as the NAME it was written with, so the hints are
# strings -- the shape CPython gives under `from __future__ import annotations`
assert get_type_hints(Point) == {'x': 'int', 'y': 'int'} and get_type_hints(Point(1, 2)) == {'x': 'int', 'y': 'int'}
assert get_type_hints(int) == {} and get_type_hints(len) == {}
assert get_origin(int) is None and get_args(int) == () and get_origin(5) is None and get_args('x') == ()
assert typing.cast is cast and typing.TypeVar is TypeVar and callable(typing.NamedTuple) and typing.TYPE_CHECKING is False

# ================= copyreg =================
assert isinstance(copyreg.dispatch_table, dict) and isinstance(copyreg._extension_registry, dict)
assert copyreg.pickle(int, lambda v: (int, (v,))) is None
assert copyreg.constructor(int) is None
copyreg.add_extension('mod', 'name', 1)
assert copyreg._extension_registry == {('mod', 'name'): 1} and copyreg._inverted_registry == {1: ('mod', 'name')}
copyreg.remove_extension('mod', 'name', 1)
assert copyreg._extension_registry == {} and copyreg.clear_extension_cache() is None
try:
    copyreg.add_extension('m', 'n', 0)
    assert False
except ValueError:
    pass
try:
    copyreg.remove_extension('m', 'n', 5)
    assert False
except ValueError:
    pass

# ---- astuple / is_dataclass ----
from dataclasses import astuple, is_dataclass


@dataclass
class Inner:
    a: int
    b: int


@dataclass
class Outer:
    i: Inner
    n: int


assert astuple(Inner(1, 2)) == (1, 2)
assert astuple(Outer(Inner(1, 2), 3)) == ((1, 2), 3)       # recurses like asdict
assert is_dataclass(Inner) and is_dataclass(Inner(1, 2)) and not is_dataclass(3)


class Plain:
    x: int


assert not is_dataclass(Plain)
try:
    astuple(3)
    assert False
except TypeError:
    pass

# ---- InitVar / ClassVar / make_dataclass ----
from dataclasses import InitVar, make_dataclass
from typing import ClassVar


@dataclass
class Scaled:
    value: int
    scale: InitVar[int] = 1
    limit: ClassVar[int] = 100

    def __post_init__(self, scale):
        self.value = self.value * scale


s = Scaled(3, 4)
assert s.value == 12 and Scaled(3).value == 3
assert [f.name for f in fields(Scaled)] == ['value']                # neither an InitVar nor a ClassVar is a field
assert repr(s) == 'Scaled(value=12)' and asdict(s) == {'value': 12} and astuple(s) == (12,)
assert Scaled(1, 2) == Scaled(2, 1)
# __match_args__ is ('value', 'scale'): the two-positional pattern looks scale up, finds nothing, fails
matched = 'no'
match Scaled(3, 4):
    case Scaled(v, s):
        matched = 'two'
    case Scaled(v):
        matched = v
assert matched == 12 and Scaled.__match_args__ == ('value', 'scale')
assert s.limit == 100 and Scaled.limit == 100                       # the class attribute reads both ways
try:
    s.scale
    assert False
except AttributeError:
    pass                                                            # an InitVar is never stored
try:
    Scaled(1, 2, 3)
    assert False
except TypeError:
    pass                                                            # a ClassVar is not a parameter

Rec = make_dataclass('Rec', ['x', ('y', int), ('z', int, field(default=5))])
r = Rec(1, 2)
assert r == Rec(1, 2, 5) and r.z == 5 and repr(r) == 'Rec(x=1, y=2, z=5)' and fields(Rec)[1].type == 'int'
Named = make_dataclass('Named', ['n'], namespace={'twice': lambda self: self.n * 2})
assert Named(4).twice() == 8
Base2 = make_dataclass('Base2', ['a'])
Derived2 = make_dataclass('Derived2', ['b'], bases=(Base2,), order=True)
assert Derived2(1, 2) < Derived2(1, 3) and asdict(Derived2(1, 2)) == {'a': 1, 'b': 2}
try:
    make_dataclass('Bad', ['x', 'x'])
    assert False
except TypeError:
    pass
