# Adapted from CPython's test suite (Lib/test/test_grammar.py: GrammarTests.test_funcdef, test_lambdef,
# Lib/test/test_positional_only_arg.py, Lib/test/test_keywordonlyarg.py, Lib/test/test_scope.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# ---- parameter kinds ----
def f0():
    return 'none'
def f1(a):
    return a
def f2(a, b=2):
    return (a, b)
def fv(*args):
    return args
def fk(**kw):
    return kw
def fall(a, b=1, *args, c, d=4, **kw):
    return (a, b, args, c, d, kw)

assert f0() == 'none'
assert f1(1) == 1 and f1(a=1) == 1
assert f2(1) == (1, 2) and f2(1, 3) == (1, 3) and f2(b=5, a=0) == (0, 5)
assert fv() == () and fv(1, 2, 3) == (1, 2, 3)
assert fk() == {} and fk(x=1, y=2) == {'x': 1, 'y': 2}
assert fall(0, c=3) == (0, 1, (), 3, 4, {})
assert fall(0, 9, 8, 7, c=3, d=5, z=6) == (0, 9, (8, 7), 3, 5, {'z': 6})

# keyword-only must be passed by name
def kwonly(a, *, key):
    return (a, key)
assert kwonly(1, key=2) == (1, 2)
try:
    kwonly(1, 2)
    assert False
except TypeError:
    pass
try:
    kwonly(1)
    assert False
except TypeError as e:
    assert 'key' in str(e)

# positional-only (PEP 570)
def posonly(a, b, /, c):
    return (a, b, c)
assert posonly(1, 2, 3) == (1, 2, 3)
assert posonly(1, 2, c=3) == (1, 2, 3)
try:
    posonly(1, b=2, c=3)
    assert False
except TypeError:
    pass

def both(a, /, b, *, c):
    return (a, b, c)
assert both(1, 2, c=3) == (1, 2, 3) and both(1, b=2, c=3) == (1, 2, 3)

# ---- call forms: star and double-star unpacking ----
def add3(a, b, c):
    return a + b + c
assert add3(*[1, 2, 3]) == 6
assert add3(*(1, 2), 3) == 6
assert add3(**{'a': 1, 'b': 2, 'c': 3}) == 6
assert add3(1, **{'b': 2, 'c': 3}) == 6
assert add3(*[1], *[2], **{'c': 3}) == 6
assert fv(*'ab', *[1]) == ('a', 'b', 1)
assert fk(**{'a': 1}, **{'b': 2}) == {'a': 1, 'b': 2}

try:
    add3(1, 2)
    assert False
except TypeError as e:
    assert 'missing' in str(e)
try:
    add3(1, 2, 3, 4)
    assert False
except TypeError as e:
    assert 'positional' in str(e)
try:
    add3(1, 2, 3, a=1)
    assert False
except TypeError as e:
    assert 'multiple values' in str(e)
try:
    add3(1, 2, 3, zz=1)
    assert False
except TypeError as e:
    assert 'unexpected keyword' in str(e)
try:
    fk(**{'a': 1}, **{'a': 2})
    assert False
except TypeError:
    pass

# ---- defaults are evaluated once, at definition time ----
def shared(x, acc=[]):
    acc.append(x)
    return acc
assert shared(1) == [1]
assert shared(2) == [1, 2]                    # the SAME list
def fresh(x, acc=None):
    if acc is None:
        acc = []
    acc.append(x)
    return acc
assert fresh(1) == [1] and fresh(2) == [2]
base = 10
def dflt(v=base):
    return v
base = 20
assert dflt() == 10

# ---- return forms ----
def none_ret():
    return
def bare():
    x = 1
def multi():
    return 1, 2
assert none_ret() is None and bare() is None
assert multi() == (1, 2)
r1, r2 = multi()
assert r1 == 1 and r2 == 2
def early(v):
    if v:
        return 'yes'
    return 'no'
assert early(1) == 'yes' and early(0) == 'no'

# ---- closures and late binding ----
def counter_factory():
    count = [0]
    def inc():
        count[0] += 1
        return count[0]
    return inc
inc = counter_factory()
inc(); inc()
assert inc() == 3

fns = [lambda: i for i in range(3)]
assert [fn() for fn in fns] == [2, 2, 2]     # late binding
fns2 = [lambda i=i: i for i in range(3)]
assert [fn() for fn in fns2] == [0, 1, 2]     # default captures the value

def adder(n):
    return lambda x: x + n
add5 = adder(5)
assert add5(1) == 6 and adder(10)(1) == 11

# closure over a variable assigned AFTER the inner def
def late():
    def get():
        return v
    v = 'late-bound'
    return get()
assert late() == 'late-bound'

# ---- lambda forms ----
assert (lambda: 1)() == 1
assert (lambda x, y=2: x * y)(3) == 6
assert (lambda *a: a)(1, 2) == (1, 2)
assert (lambda **k: k)(a=1) == {'a': 1}
assert (lambda x, *, k: (x, k))(1, k=2) == (1, 2)
assert sorted(['bb', 'a', 'ccc'], key=lambda s: len(s)) == ['a', 'bb', 'ccc']
assert (lambda: (yield_free := 5))() == 5

# ---- recursion ----
def fact(n):
    return 1 if n <= 1 else n * fact(n - 1)
assert fact(10) == 3628800
def fib(n):
    return n if n < 2 else fib(n - 1) + fib(n - 2)
assert fib(15) == 610
def is_even(n):
    return True if n == 0 else is_odd(n - 1)
def is_odd(n):
    return False if n == 0 else is_even(n - 1)
assert is_even(10) and is_odd(7)

# ---- functions are values ----
def apply(fn, v):
    return fn(v)
assert apply(abs, -3) == 3
assert apply(len, 'abcd') == 4
table = {'sq': lambda v: v * v, 'neg': lambda v: -v}
assert table['sq'](4) == 16 and table['neg'](4) == -4
assert callable(apply) and callable(len) and not callable(3)
assert list(map(fact, [1, 2, 3, 4])) == [1, 2, 6, 24]

# ---- decorators ----
def twice(fn):
    def wrapper(*a, **k):
        return fn(*a, **k) * 2
    return wrapper
def plus_one(fn):
    def wrapper(*a, **k):
        return fn(*a, **k) + 1
    return wrapper

@twice
def three():
    return 3
assert three() == 6

@plus_one
@twice
def four():
    return 4
assert four() == 9                     # applied bottom-up: (4*2)+1

def repeat(n):
    def deco(fn):
        def wrapper(*a):
            return [fn(*a) for _ in range(n)]
        return wrapper
    return deco
@repeat(3)
def hi(name):
    return 'hi ' + name
assert hi('x') == ['hi x', 'hi x', 'hi x']

# ---- annotations are accepted and ignored at runtime ----
def annotated(a: int, b: 'str' = 'x') -> bool:
    return str(a) + b == '1x'
assert annotated(1)

# ---- nested functions and shadowing ----
def level1():
    def level2():
        def level3():
            return 'deep'
        return level3()
    return level2()
assert level1() == 'deep'
def shadow(len):
    return len
assert shadow(5) == 5 and len('ab') == 2

# ---- docstring is a plain first statement ----
def documented():
    'This is a docstring.'
    return 1
assert documented() == 1
