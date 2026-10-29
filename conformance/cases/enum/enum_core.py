# Adapted from CPython's test suite (Lib/test/test_enum.py: the TestEnum-family basics - members,
# lookup, iteration, aliases, unique, auto, IntEnum, StrEnum, the functional API - without
# metaclasses, pickling, _missing_, _generate_next_value_ overrides and Flag). Python Software
# Foundation, PSF License Version 2 -- see conformance/README.md.
# dialect: members are NAME = value lines (no annotations); an enum class may define methods,
# __str__ and __repr__.
from enum import Enum, IntEnum, StrEnum, auto, unique
import json

def raises(exc, f):
    try:
        f()
    except exc as e:
        return str(e)
    return None

class Season(Enum):
    SPRING = 1
    SUMMER = 2
    AUTUMN = 3
    WINTER = 4

# ---- members ----
assert Season.SPRING.name == 'SPRING' and Season.SPRING.value == 1
assert repr(Season.WINTER) == '<Season.WINTER: 4>'
assert str(Season.WINTER) == 'Season.WINTER'
assert format(Season.WINTER) == 'Season.WINTER' and f'{Season.WINTER}' == 'Season.WINTER'
assert f'{Season.WINTER:>15}' == '  Season.WINTER'
assert type(Season.SPRING) is Season and Season.SPRING.__class__ is Season
assert isinstance(Season.SPRING, Season) and isinstance(Season.SPRING, Enum)
assert not isinstance(Season.SPRING, int) and not isinstance(1, Season)
assert issubclass(Season, Enum) and not issubclass(Season, IntEnum)
assert repr(Season) == "<enum 'Season'>" and Season.__name__ == 'Season'

# ---- iteration, len, in ----
assert list(Season) == [Season.SPRING, Season.SUMMER, Season.AUTUMN, Season.WINTER]
assert [s.name for s in Season] == ['SPRING', 'SUMMER', 'AUTUMN', 'WINTER']
assert len(Season) == 4
assert Season.SUMMER in Season
assert 2 in Season and 7 not in Season       # 3.12: values too

# ---- lookup ----
assert Season(2) is Season.SUMMER
assert Season['AUTUMN'] is Season.AUTUMN
assert Season(Season.WINTER) is Season.WINTER
assert raises(ValueError, lambda: Season(7)) == '7 is not a valid Season'
assert raises(ValueError, lambda: Season('x')) == "'x' is not a valid Season"
assert raises(KeyError, lambda: Season['FALL']) == "'FALL'"
assert raises(AttributeError, lambda: Season.FALL) is not None
assert getattr(Season, 'SPRING') is Season.SPRING

# ---- identity, equality, hashing ----
assert Season.SPRING is Season.SPRING and Season.SPRING == Season.SPRING
assert Season.SPRING != Season.SUMMER and Season.SPRING != 1
d = {Season.SPRING: 'warm', Season.WINTER: 'cold'}
assert d[Season.WINTER] == 'cold'
assert len({Season.SPRING, Season.SPRING, Season.SUMMER}) == 2
assert raises(TypeError, lambda: Season.SPRING < Season.SUMMER) is not None
assert sorted(Season, key=lambda s: -s.value)[0] is Season.WINTER

# ---- members are read-only ----
def set_value():
    Season.SPRING.value = 5
assert raises(AttributeError, set_value) is not None
def reassign():
    Season.SPRING = 5
assert raises(AttributeError, reassign) == "cannot reassign member 'SPRING'"

# ---- aliases ----
class Shape(Enum):
    SQUARE = 2
    DIAMOND = 1
    CIRCLE = 3
    ALIAS_FOR_SQUARE = 2
assert Shape.ALIAS_FOR_SQUARE is Shape.SQUARE
assert Shape.ALIAS_FOR_SQUARE.name == 'SQUARE'
assert Shape(2) is Shape.SQUARE and Shape['ALIAS_FOR_SQUARE'] is Shape.SQUARE
assert list(Shape) == [Shape.SQUARE, Shape.DIAMOND, Shape.CIRCLE] and len(Shape) == 3
assert list(Shape.__members__) == ['SQUARE', 'DIAMOND', 'CIRCLE', 'ALIAS_FOR_SQUARE']
assert Shape.__members__['ALIAS_FOR_SQUARE'] is Shape.SQUARE

# ---- unique ----
def make_dup():
    @unique
    class Mistake(Enum):
        ONE = 1
        TWO = 2
        THREE = 3
        FOUR = 3
assert raises(ValueError, make_dup) == "duplicate values found in <enum 'Mistake'>: FOUR -> THREE"
@unique
class Fine(Enum):
    A = 1
    B = 2
assert Fine.B.value == 2

# ---- auto ----
class Color(Enum):
    RED = auto()
    BLUE = auto()
    GREEN = auto()
assert [c.value for c in Color] == [1, 2, 3]
class Mixed(Enum):
    A = 10
    B = auto()
assert Mixed.B.value == 11

# ---- methods, __str__, and functions that are not members ----
class Planet(Enum):
    MERCURY = 1
    VENUS = 2
    def describe(self):
        return self.name.lower() + ':' + str(self.value)
    def __str__(self):
        return 'planet ' + self.name
assert Planet.VENUS.describe() == 'venus:2'
assert str(Planet.MERCURY) == 'planet MERCURY' and repr(Planet.MERCURY) == '<Planet.MERCURY: 1>'
assert Planet.describe(Planet.MERCURY) == 'mercury:1'

class WithHelper(Enum):
    A = 1
    helper = lambda self: 'help ' + self.name
assert WithHelper.A.helper() == 'help A' and len(WithHelper) == 1

def reuse():
    class Bad(Enum):
        A = 1
        A = 2
assert raises(TypeError, reuse) == "'A' already defined as 1"

# ---- IntEnum: an int ----
class Shape2(IntEnum):
    CIRCLE = 1
    SQUARE = 2
assert Shape2.CIRCLE == 1 and Shape2.SQUARE > Shape2.CIRCLE
assert Shape2.SQUARE + 1 == 3 and type(Shape2.SQUARE + 1) is int
assert isinstance(Shape2.CIRCLE, int) and isinstance(Shape2.CIRCLE, Shape2) and isinstance(Shape2.CIRCLE, IntEnum)
assert isinstance(Shape2.CIRCLE, Enum) and type(Shape2.CIRCLE) is Shape2
assert repr(Shape2.CIRCLE) == '<Shape2.CIRCLE: 1>' and repr([Shape2.CIRCLE]) == '[<Shape2.CIRCLE: 1>]'
assert str(Shape2.CIRCLE) == '1' and format(Shape2.CIRCLE) == '1' and f'{Shape2.SQUARE:03d}' == '002'
assert ['a', 'b', 'c'][Shape2.SQUARE] == 'c'
assert Shape2(1) is Shape2.CIRCLE and Shape2.CIRCLE.value == 1 and type(Shape2.CIRCLE.value) is int
assert hash(Shape2.SQUARE) == hash(2) and {2: 'two'}[Shape2.SQUARE] == 'two'
assert int(Shape2.SQUARE) == 2 and repr(int(Shape2.SQUARE)) == '2'
assert sorted([Shape2.SQUARE, 0, Shape2.CIRCLE]) == [0, 1, 2]
assert json.dumps({'shape': Shape2.SQUARE}) == '{"shape": 2}'
assert json.dumps({Shape2.SQUARE: 1}) == '{"2": 1}'
assert Shape2.SQUARE.name == 'SQUARE'
class Num(IntEnum):
    ONE = '1'
    TWO = 2.0
assert Num.ONE == 1 and Num.TWO == 2 and type(Num.TWO.value) is int   # IntEnum takes int(value)

# ---- StrEnum: a str ----
class Fruit(StrEnum):
    APPLE = 'apple'
    PEAR = auto()
assert Fruit.PEAR == 'pear' and Fruit.PEAR.value == 'pear'
assert str(Fruit.APPLE) == 'apple' and f'{Fruit.APPLE}' == 'apple' and format(Fruit.APPLE, '>7') == '  apple'
assert repr(Fruit.APPLE) == "<Fruit.APPLE: 'apple'>" and repr(str(Fruit.APPLE)) == "'apple'"
assert Fruit.APPLE.upper() == 'APPLE' and Fruit.APPLE + 's' == 'apples' and 'pp' in Fruit.APPLE
assert isinstance(Fruit.APPLE, str) and Fruit('apple') is Fruit.APPLE and type(Fruit.APPLE) is Fruit
assert json.dumps([Fruit.APPLE]) == '["apple"]' and json.dumps({Fruit.APPLE: 1}) == '{"apple": 1}'
assert {'apple': 1}[Fruit.APPLE] == 1
assert '-'.join([Fruit.APPLE, Fruit.PEAR]) == 'apple-pear'
assert raises(TypeError, lambda: StrEnum('Bad', [('X', 1)])) == '1 is not a string'

# ---- the pre-3.11 idiom: str or int mixed into Enum ----
class Status(str, Enum):
    NEW = 'new'
    DONE = 'done'
assert Status.NEW == 'new' and Status('done') is Status.DONE and isinstance(Status.NEW, str)
assert Status.NEW.value == 'new' and str(Status.NEW) == 'Status.NEW'
assert repr(Status.NEW) == "<Status.NEW: 'new'>"
assert json.dumps({'status': Status.DONE}) == '{"status": "done"}'
class Level(int, Enum):
    LOW = 1
    HIGH = 2
assert Level.HIGH > Level.LOW and Level.HIGH == 2 and str(Level.LOW) == 'Level.LOW'
assert isinstance(Level.LOW, int) and isinstance(Level.LOW, Level)

# ---- match ----
def season_word(s):
    match s:
        case Season.SPRING | Season.SUMMER:
            return 'warm'
        case Season(name='AUTUMN'):
            return 'mild'
        case Season():
            return 'cold'
        case _:
            return 'other'
assert [season_word(s) for s in Season] == ['warm', 'warm', 'mild', 'cold'] and season_word(3) == 'other'

# ---- functional API ----
Animal = Enum('Animal', 'ANT BEE CAT')
assert Animal.BEE.value == 2 and list(Animal)[2].name == 'CAT' and repr(Animal.ANT) == '<Animal.ANT: 1>'
Codes = IntEnum('Codes', [('OK', 200), ('NOT_FOUND', 404)])
assert Codes(404) is Codes.NOT_FOUND and Codes.OK == 200
Pair = Enum('Pair', {'LEFT': 'l', 'RIGHT': 'r'})
assert Pair('r') is Pair.RIGHT and Pair.LEFT.value == 'l'
Words = Enum('Words', ['ONE', 'TWO'])
assert Words.TWO.value == 2

# ---- an enum without members can be extended; one with members cannot ----
class Ordered(Enum):
    def rank(self):
        return list(type(self)).index(self)
class Size(Ordered):
    S = 's'
    M = 'm'
assert Size.M.rank() == 1 and isinstance(Size.S, Ordered)
def extend():
    class Bigger(Size):
        L = 'l'
assert raises(TypeError, extend) == "<enum 'Bigger'> cannot extend <enum 'Size'>"
