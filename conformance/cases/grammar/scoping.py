# Adapted from CPython's test suite (Lib/test/test_scope.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# a local assignment shadows the global for the WHOLE function body
g = 'global'
def reads_global():
    return g
def shadows():
    local_g = 'local'
    return local_g
assert reads_global() == 'global'
assert shadows() == 'local'

# assigning in a function without 'global' does not touch the module name
def try_rebind():
    g = 'inner'
    return g
assert try_rebind() == 'inner'
assert g == 'global'

# UnboundLocalError: the name is local because it is assigned SOMEWHERE in the body
def unbound():
    try:
        v = trap        # 'trap' is local (assigned below) but not yet bound
        return 'no error'
    except NameError:   # UnboundLocalError is a subclass of NameError
        return 'caught'
    finally:
        trap = 1
assert unbound() == 'caught'

# nested functions see enclosing locals (closures), latest value at CALL time
def outer():
    n = 1
    def get():
        return n
    n = 2
    return get()
assert outer() == 2

# two-level nesting with nonlocal reaching the middle scope
def level1():
    state = []
    def level2():
        marker = 'L2'
        def level3():
            nonlocal marker
            marker = 'L3'
            state.append(marker)
        level3()
        return marker
    return level2(), state
assert level1() == ('L3', ['L3'])

# default parameter values are evaluated in the DEFINING scope
base = 10
def with_default(x=base):
    return x
base = 20
assert with_default() == 10

# comprehension has its own scope; lambda inside sees the comp variable
fns = [(lambda v=v: v * 10) for v in range(3)]
assert [f() for f in fns] == [0, 10, 20]

# global statement writes through from a nested call
counter = 0
def bump():
    global counter
    counter += 1
def bump_twice():
    bump()
    bump()
bump_twice()
assert counter == 2

# def / except-as / import / del on a nonlocal or a captured local act on that variable, never on a global
def rebinds_nonlocal():
    g = 1
    def inner():
        nonlocal g
        def g():
            return 2
    inner()
    return g()
assert rebinds_nonlocal() == 2 and g == 'global'

def except_as_nonlocal():
    e = 'outer'
    def inner():
        nonlocal e
        try:
            1 / 0
        except ZeroDivisionError as e:
            pass
    inner()
    try:
        return e
    except NameError:
        return 'deleted'
assert except_as_nonlocal() == 'deleted'

def import_nonlocal():
    m = None
    def inner():
        nonlocal m
        import math as m
    inner()
    return m.floor(2.5)
assert import_nonlocal() == 2

x = 'global x'
def del_captured():
    x = 1
    h = lambda: x
    del x
    try:
        return h()
    except NameError:
        return 'unbound'
assert del_captured() == 'unbound' and x == 'global x'

def del_nonlocal():
    y = 1
    def inner():
        nonlocal y
        del y
    inner()
    try:
        return y
    except NameError:
        return 'deleted'
assert del_nonlocal() == 'deleted'

def del_unbound_cell():
    z = 1
    h = lambda: z
    del z
    try:
        del z
    except NameError:
        return 'raised'
assert del_unbound_cell() == 'raised'
