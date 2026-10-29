# Adapted from CPython's test suite (Lib/test/test_grammar.py: GrammarTests.test_with_statement and
# test_with_statement_with_parenthesized items). Python Software Foundation, PSF License Version 2 --
# see conformance/README.md.

class manager:
    def __enter__(self):
        return (1, 2)
    def __exit__(self, *args):
        pass

with manager():
    pass
with manager() as x:
    pass
with manager() as (x, y):
    pass
with manager(), manager():
    pass
with manager() as x, manager() as y:
    pass
with manager() as x, manager():
    pass

with (
    manager()
):
    pass

with (
    manager() as x
):
    pass

with (
    manager() as (x, y),
    manager() as z,
):
    pass

with (
    manager(),
    manager()
):
    pass

with (
    manager() as x,
    manager() as y
):
    pass

with (
    manager() as x,
    manager()
):
    pass

with (
    manager() as x,
    manager() as y,
    manager() as z,
):
    pass

with (
    manager() as x,
    manager() as y,
    manager(),
):
    pass

assert x == (1, 2) and y == (1, 2) and z == (1, 2)

# An expression that merely starts with a parenthesized group is one item, as in CPython.
m = manager()
with (m) as w:
    assert w == (1, 2)
with (m):
    pass
with (lambda: m)() as w:
    assert w == (1, 2)
with (m if True else None) as w:
    assert w == (1, 2)
managers = [manager()]
with (managers)[0] as w:
    assert w == (1, 2)

# A tuple is not a context manager.
try:
    with (m, m) as t:
        pass
    assert False
except TypeError as e:
    assert 'context manager' in str(e)

# One-line bodies and a with inside a function, a loop and a try.
with manager() as x: y = x
assert y == (1, 2)
def f():
    for i in range(3):
        with manager() as v:
            try:
                if i == 1:
                    return v[0] + i
            finally:
                pass
assert f() == 2
