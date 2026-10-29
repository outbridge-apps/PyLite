# Adapted from CPython's test suite (Lib/test/test_super.py: TestSuper.testBasicsWorking,
# testClassGetattrWorking, testSubclassNoOverrideWorking, testUnboundMethodWorking,
# test_super_with_closure, test___class___instancemethod, test_obscure_super_errors and
# test_super_argtype, reduced to single inheritance) and extended with the patterns copied code uses.
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# dialect: single inheritance, so the chain is the MRO; no classmethod/staticmethod, so no
# super(type, type2) and no one-argument super().
from dataclasses import dataclass

def raises(exc, f):
    try:
        f()
    except exc as e:
        return str(e)
    return None

# ---- the basics: each level adds its letter ----
class A:
    def f(self):
        return 'A'

class B(A):
    def f(self):
        return super().f() + 'B'

class C(B):
    def f(self):
        return super().f() + 'C'

class D(C):
    def f(self):
        return super().f() + 'D'

class E(D):
    pass

assert D().f() == 'ABCD'
assert D.f(D()) == 'ABCD'          # through the class, self passed explicitly
assert E().f() == 'ABCD'           # no override: the lookup still starts above D
assert E.f(E()) == 'ABCD'

# ---- the defining class, not type(self): a three-level __init__ chain reaches each parent once ----
calls = []
class Base:
    def __init__(self, name):
        calls.append('Base')
        self.name = name

class Mid(Base):
    def __init__(self, name, level):
        calls.append('Mid')
        super().__init__(name)
        self.level = level

class Leaf(Mid):
    def __init__(self, name, level, tag):
        calls.append('Leaf')
        super().__init__(name=name, level=level)
        self.tag = tag

x = Leaf('n', 2, 't')
assert calls == ['Leaf', 'Mid', 'Base']
assert (x.name, x.level, x.tag) == ('n', 2, 't')

# ---- explicit super(Class, self) skips to the class above Class ----
class G1:
    def f(self):
        return 'G1'
class G2(G1):
    def f(self):
        return 'G2'
class G3(G2):
    def f(self):
        return super(G2, self).f()
    def g(self):
        return super(G3, self).f()
    def h(self):
        return super(type(self), self).f()
assert G3().f() == 'G1' and G3().g() == 'G2' and G3().h() == 'G2'
assert super(G3, G3()).f() == 'G2'   # outside a method too

# ---- closures, comprehensions, lambdas and nested functions ----
class S(A):
    def f(self):
        def nested():
            return self                  # a closure over self does not change super()
        nested()
        return super().f() + 'S'
    def comp(self):
        return [super().f() + str(i) for i in range(2)]   # inlined comprehension (3.12)
    def in_lambda(self):
        g = lambda: super().f()
        return g()
    def in_def(self):
        def g():
            return super().f()
        return g()

assert S().f() == 'AS'
assert S().comp() == ['A0', 'A1']
assert raises(RuntimeError, S().in_lambda) == 'super(): no arguments'
assert raises(RuntimeError, S().in_def) == 'super(): no arguments'
assert raises(RuntimeError, lambda: super()) == 'super(): no arguments'

class NoArgs:
    def f(*args):
        return super()
assert raises(RuntimeError, NoArgs().f) == 'super(): no arguments'

# ---- __class__ is the defining class, in nested functions too ----
class X:
    def f(self):
        return __class__
    def g(self):
        def inner():
            return __class__
        return inner()
class Y(X):
    pass
assert X().f() is X and Y().f() is X and Y().g() is X
assert type(Y()) is Y and Y().__class__ is Y and Y().__class__.__name__ == 'Y'

# ---- what super() finds: methods and class attributes, not instance fields ----
class K1:
    LIMIT = 10
    def __init__(self):
        self.v = 1
class K2(K1):
    LIMIT = 20
    def limits(self):
        return (self.LIMIT, super().LIMIT)
    def field(self):
        return super().v
    def missing(self):
        return super().nothing
assert K2().limits() == (20, 10)
assert raises(AttributeError, K2().field) == "'super' object has no attribute 'v'"
assert raises(AttributeError, K2().missing) == "'super' object has no attribute 'nothing'"

# ---- object.__init__ at the top ----
class Plain:
    def __init__(self):
        super().__init__()
        self.ok = True
assert Plain().ok
class BadInit:
    def __init__(self):
        super().__init__(1)
assert raises(TypeError, BadInit) == 'object.__init__() takes exactly one argument (the instance to initialize)'

# ---- protocol methods ----
class R1:
    def __repr__(self):
        return 'R1'
    def __eq__(self, other):
        return isinstance(other, R1)
class R2(R1):
    def __repr__(self):
        return super().__repr__() + '+R2'
    def __eq__(self, other):
        return super().__eq__(other) and isinstance(other, R2)
assert repr(R2()) == 'R1+R2'
assert R2() == R2() and not (R2() == R1())

# ---- dataclasses: the generated __init__, __post_init__ chains ----
@dataclass
class Point:
    x: int
    y: int = 0

class Point3(Point):
    def __init__(self, x, y, z):
        super().__init__(x, y)
        self.z = z

p = Point3(1, 2, 3)
assert (p.x, p.y, p.z) == (1, 2, 3)
assert raises(TypeError, lambda: Point3(1, 2, 3).__init__()) is not None   # missing arguments

@dataclass
class Item:
    name: str
    def __post_init__(self):
        self.name = self.name.strip()

@dataclass
class Priced(Item):
    price: float = 0.0
    def __post_init__(self):
        super().__post_init__()
        self.price = round(self.price, 2)

i = Priced(' pen ', 1.234)
assert i.name == 'pen' and i.price == 1.23

@dataclass
class Money:
    amount: int
    def show(self):
        return str(self.amount)

@dataclass
class Fee(Money):
    rate: int = 1
    def show(self):
        return super().show() + '@' + str(self.rate)

assert Fee(5, 2).show() == '5@2'

# ---- a class created inside a function ----
def make(suffix):
    class Inner(A):
        def f(self):
            return super().f() + suffix
    return Inner

assert make('I')().f() == 'AI'

# ---- argument errors ----
assert raises(TypeError, lambda: super(1, A())) == 'super() argument 1 must be a type, not int'
assert raises(TypeError, lambda: super(B, A())) == 'super(type, obj): obj must be an instance or subtype of type'
assert raises(TypeError, lambda: super(A, 5)) == 'super(type, obj): obj must be an instance or subtype of type'
