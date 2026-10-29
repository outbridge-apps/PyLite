# Adapted from CPython's test suite (Lib/test/test_grammar.py, test_functools-adjacent basics).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# defaults, positional and keyword calls
def f(a, b=10, c=20):
    return a + b + c
assert f(1) == 31
assert f(1, 2) == 23
assert f(1, c=2) == 13
assert f(c=1, a=2) == 13

# mutable default is evaluated ONCE (the classic)
def collect(x, acc=[]):
    acc.append(x)
    return acc
assert collect(1) == [1]
assert collect(2) == [1, 2]

# *args / **kwargs collection and forwarding
def g(*args, **kwargs):
    return (args, sorted(kwargs.items()))
assert g(1, 2, k=3, a=4) == ((1, 2), [('a', 4), ('k', 3)])
def add3(a, b, c):
    return a + b + c
pack = (1, 2, 3)
assert add3(*pack) == 6
named = {'b': 2, 'c': 3}
assert add3(1, **named) == 6

# closures and nonlocal
def make_counter():
    count = 0
    def bump():
        nonlocal count
        count += 1
        return count
    return bump
c1 = make_counter()
c2 = make_counter()
assert c1() == 1 and c1() == 2 and c2() == 1

# global
g_total = 0
def add_global(x):
    global g_total
    g_total += x
add_global(5)
add_global(7)
assert g_total == 12

# lambdas; the late-binding gotcha and the default-capture fix
double = lambda x: x * 2
assert double(21) == 42
late = [lambda: i for i in range(3)]
assert [fn() for fn in late] == [2, 2, 2]
bound = [lambda i=i: i for i in range(3)]
assert [fn() for fn in bound] == [0, 1, 2]

# functions are first-class
def apply_twice(fn, x):
    return fn(fn(x))
assert apply_twice(lambda v: v + 3, 10) == 16
ops = {'add': lambda a, b: a + b, 'mul': lambda a, b: a * b}
assert ops['add'](2, 3) == 5 and ops['mul'](2, 3) == 6

# recursion
def fib(k):
    return k if k < 2 else fib(k - 1) + fib(k - 2)
assert fib(10) == 55

# nested scopes read outer names
def outer():
    base = 100
    def inner(x):
        return base + x
    return inner(1)
assert outer() == 101
