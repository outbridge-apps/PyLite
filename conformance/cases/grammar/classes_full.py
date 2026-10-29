# Adapted from CPython's test suite (Lib/test/test_class.py, Lib/test/test_descr.py inheritance basics).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Dialect: single inheritance, explicit base calls (Base.__init__(self, ...)), __init__/__eq__/__repr__.

class Animal:
    def __init__(self, name):
        self.name = name
        self.sound = 'generic'

    def speak(self):
        return self.name + ' says ' + self.sound

    def kind(self):
        return 'animal'

class Dog(Animal):
    def __init__(self, name):
        Animal.__init__(self, name)
        self.sound = 'woof'

    def kind(self):
        return 'dog'

class Puppy(Dog):
    def __init__(self, name):
        Dog.__init__(self, name)
        self.sound = 'yip'

    def kind(self):
        return 'puppy(' + Dog.kind(self) + ')'

a = Animal('x')
d = Dog('rex')
p = Puppy('bit')
assert a.speak() == 'x says generic'
assert d.speak() == 'rex says woof'           # inherited method, overridden state
assert p.speak() == 'bit says yip'
assert [o.kind() for o in (a, d, p)] == ['animal', 'dog', 'puppy(dog)']

# isinstance / issubclass through the chain
assert isinstance(p, Puppy) and isinstance(p, Dog) and isinstance(p, Animal)
assert not isinstance(a, Dog)
assert issubclass(Puppy, Animal) and issubclass(Dog, Animal) and not issubclass(Animal, Dog)
assert isinstance(p, (int, Animal))
assert type(p) is not None

# attribute errors
try:
    a.no_such_attr
    assert False
except AttributeError as e:
    assert 'no_such_attr' in str(e)
try:
    Animal.no_such_method
    assert False
except AttributeError:
    pass

# methods via the class: explicit self
assert Animal.speak(d) == 'rex says woof'
assert Dog.kind(p) == 'dog'

# bound method objects are values
say = d.speak
assert say() == 'rex says woof'
d.sound = 'grr'
assert say() == 'rex says grr'                # bound to the instance, sees mutation

# ---- __eq__ / __repr__ ----
class Money:
    def __init__(self, amount, cur):
        self.amount = amount
        self.cur = cur

    def __eq__(self, other):
        return isinstance(other, Money) and self.amount == other.amount and self.cur == other.cur

    def __repr__(self):
        return 'Money(' + repr(self.amount) + ', ' + repr(self.cur) + ')'

m = Money(5, 'EUR')
assert m == Money(5, 'EUR') and m != Money(5, 'USD') and m != 5
assert repr(m) == "Money(5, 'EUR')"
assert str(m) == "Money(5, 'EUR')"            # str falls back to __repr__
assert repr([m]) == "[Money(5, 'EUR')]"
assert Money(1, 'a') in [Money(0, 'a'), Money(1, 'a')]
assert [Money(1, 'a'), Money(2, 'a')].index(Money(2, 'a')) == 1
assert [Money(1, 'a'), Money(1, 'a')].count(Money(1, 'a')) == 2

# A bare class compares and hashes by IDENTITY, as in CPython; field equality is what @dataclass
# generates. The dialect deviation left here is the automatic field repr, which CPython spells
# <Plain object at 0x...> -- this engine keeps addresses out of everything it prints.
class Plain:
    def __init__(self, v):
        self.v = v
p1 = Plain(1); p2 = Plain(1)
assert p1 == p1 and p1 != p2 and not (p1 == p2)
assert p1 is p1 and p1 is not p2
assert repr(p1) == 'Plain(v=1)'               # dialect: no address
assert {p1: 'one'}[p1] == 'one' and len({Plain(1), Plain(1)}) == 2
assert hash(p1) == hash(p1) and hash(p1) == id(p1)
p1.v = 99
assert {p1: 'one'}[p1] == 'one'               # the key survives a field assignment
assert p1 in [p1] and p1 not in [p2]

# ---- methods calling methods, state machines ----
class Stack:
    def __init__(self):
        self.items = []

    def push(self, v):
        self.items.append(v)
        return self

    def pop(self):
        if self.is_empty():
            raise IndexError('pop from empty stack')
        return self.items.pop()

    def is_empty(self):
        return len(self.items) == 0

    def __repr__(self):
        return 'Stack(' + repr(self.items) + ')'

st = Stack().push(1).push(2)                  # chaining via return self
assert st.pop() == 2 and st.pop() == 1 and st.is_empty()
try:
    st.pop()
    assert False
except IndexError:
    pass
assert repr(st) == 'Stack([])'

# ---- class-body field annotations feed @dataclass (a plain class ignores them) ----
from dataclasses import dataclass

@dataclass
class Config:
    retries: int = 3
    name: str = 'svc'

c1 = Config()
c2 = Config(name='other')
assert c1.retries == 3 and c1.name == 'svc' and c2.name == 'other' and c2.retries == 3
assert c1 == Config(3, 'svc') and c1 != c2

# ---- __init__ with defaults / kwargs ----
class Opt:
    def __init__(self, a, b=2, *rest, **kw):
        self.a = a; self.b = b; self.rest = rest; self.kw = kw
o = Opt(1, 3, 4, 5, z=6)
assert (o.a, o.b, o.rest, o.kw) == (1, 3, (4, 5), {'z': 6})
assert Opt(1).b == 2
try:
    Opt()
    assert False
except TypeError:
    pass

# ---- instances inside containers and comprehensions ----
dogs = [Dog(n) for n in ('a', 'b')]
assert sorted(dg.name for dg in dogs) == ['a', 'b']
by_name = {dg.name: dg for dg in dogs}
assert by_name['b'].speak() == 'b says woof'

# ---- a class used before an instance exists (recursion through the class name) ----
class Node:
    def __init__(self, val, nxt=None):
        self.val = val
        self.nxt = nxt

    def length(self):
        return 1 + (self.nxt.length() if self.nxt is not None else 0)

chain = Node(1, Node(2, Node(3)))
assert chain.length() == 3
assert chain.nxt.nxt.val == 3
