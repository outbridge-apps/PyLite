# Adapted from CPython's test suite (Lib/test/test_class.py: the class-attribute fragments of
# ClassTests - testSetattrWrapperNameIntern's class attribute, class attribute lookup through
# instances and subclasses) and written for the 1.1.0 class-body constants. Python Software
# Foundation, PSF License Version 2 -- see conformance/README.md.
# dialect: an instance gets its own value for a class attribute only by assigning it in __init__
# (instances have fixed fields); assigning it elsewhere is an AttributeError naming that rule.
from dataclasses import dataclass

def raises(exc, f):
    try:
        f()
    except exc:
        return True
    return False

# ---- read through the class and through an instance ----
class Color:
    """Colours by name."""
    RED = 1
    GREEN = 2
    BLUE = RED + GREEN          # an earlier constant of the same body
    NAMES = {RED: 'red', GREEN: 'green'}
    def name(self, c):
        return self.NAMES.get(c, '?')

assert Color.RED == 1 and Color.GREEN == 2 and Color.BLUE == 3
c = Color()
assert c.RED == 1 and c.BLUE == 3
assert c.name(Color.GREEN) == 'green'
assert Color.NAMES is c.NAMES   # mutable values are shared, not copied

# ---- a subclass shadows, and inherits the rest ----
class Paint(Color):
    RED = 10
    SHADE = RED * 2             # this body's RED

assert Paint.RED == 10 and Paint.GREEN == 2 and Paint.SHADE == 20
assert Paint().RED == 10 and Color.RED == 1
assert Paint.BLUE == 3          # computed once in Color's body, from Color's RED

# ---- a class attribute changed on the class is seen by every instance ----
class Counter:
    count = 0
    def __init__(self):
        Counter.count += 1
        self.serial = Counter.count

a = Counter()
b = Counter()
assert Counter.count == 2 and a.count == 2 and b.serial == 2 and a.serial == 1
Counter.count = 10
assert a.count == 10
Counter.label = 'added later'
assert b.label == 'added later'

# ---- an instance value shadows the class one for that instance only ----
class Settings:
    timeout = 30
    def __init__(self, timeout=None):
        if timeout is not None:
            self.timeout = timeout

d = Settings()
e = Settings(5)
assert d.timeout == 30 and e.timeout == 5 and Settings.timeout == 30

class Late:
    level = 1
    def raise_level(self):
        self.level = 2          # dialect: not assigned in __init__, so not an instance field
l = Late()
assert raises(AttributeError, l.raise_level)
assert l.level == 1

# ---- with fields and @dataclass: constants are not fields ----
@dataclass
class Order:
    PREFIX = 'ORD-'
    MAX_LINES = 50
    number: int = 0
    lines: list = None
    def code(self):
        return self.PREFIX + str(self.number)

o = Order(7)
assert o.code() == 'ORD-7'
assert repr(o) == 'Order(number=7, lines=None)'
assert Order.MAX_LINES == 50 and o.MAX_LINES == 50
assert Order(1) == Order(1)

# ---- a plain class's repr does not show them either ----
class Point:
    ORIGIN = (0, 0)
    def __init__(self, x, y):
        self.x = x
        self.y = y
assert 'ORIGIN' not in repr(Point(1, 2))

# ---- defaults of methods are evaluated in the class body, where the constants are visible ----
class Paging:
    SIZE = 25
    def page(self, n, size=SIZE):
        return n * size
assert Paging().page(2) == 50
assert Paging().page(2, 10) == 20

# ---- a method body does not see the class body's names (CPython's scoping) ----
LIMIT = 'module'
class Scoped:
    LIMIT = 'class'
    def which(self):
        return LIMIT
assert Scoped().which() == 'module'
assert Scoped.LIMIT == 'class'

# ---- a function stored as a class attribute is a method through an instance ----
def describe(self):
    return 'I am ' + type(self).__name__
class Thing:
    describe = describe
    shout = lambda self: 'HEY'
assert Thing().describe() == 'I am Thing'
assert Thing().shout() == 'HEY'
assert Thing.shout(None) == 'HEY'

# ---- keyword arguments named like a constant stay keywords ----
class Config:
    NAME = 'n'
    DEFAULTS = dict(NAME='default', other=1)
assert Config.DEFAULTS == {'NAME': 'default', 'other': 1}

# ---- __match_args__, __hash__ = None, __slots__ ----
class Pair:
    __match_args__ = ('left', 'right')
    __slots__ = ('left', 'right')
    def __init__(self, left, right):
        self.left = left
        self.right = right
match Pair(1, 2):
    case Pair(1, r):
        got = r
assert got == 2
assert Pair.__match_args__ == ('left', 'right')

class Mutable:
    __hash__ = None
    def __init__(self):
        self.v = 1
assert raises(TypeError, lambda: hash(Mutable()))

# ---- dir() lists them ----
assert 'RED' in dir(Color) and 'BLUE' in dir(Color()) and 'name' in dir(Color)

# ---- an exception class may carry constants ----
class ApiError(Exception):
    CODE = 500
assert ApiError.CODE == 500
def boom():
    raise ApiError('server')
assert raises(ApiError, boom)

# ---- a missing class attribute ----
assert raises(AttributeError, lambda: Color.PURPLE)
assert raises(AttributeError, lambda: Color().PURPLE)

# ---- an exception instance reads its class's constants, inherited ones too ----
class NotFound(ApiError):
    CODE = 404
class Gone(NotFound):
    pass
try:
    raise Gone('x')
except ApiError as e:
    assert e.CODE == 404 and Gone.CODE == 404 and e.args == ('x',)
