# Adapted from CPython's test suite (Lib/test/test_exceptions.py hierarchy cases).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# LookupError is the shared base of KeyError and IndexError
def catch_lookup(fn):
    try:
        fn()
        return 'ok'
    except LookupError:
        return 'lookup'
assert catch_lookup(lambda: {}['k']) == 'lookup'
assert catch_lookup(lambda: [][0]) == 'lookup'
assert catch_lookup(lambda: 1) == 'ok'

# ArithmeticError is the base of ZeroDivisionError and OverflowError
try:
    _ = 1 // 0
except ArithmeticError as e:
    assert type(e).__name__ == 'ZeroDivisionError'

# the FIRST matching except clause wins, subclass listed first
order = []
try:
    raise KeyError('k')
except KeyError:
    order.append('specific')
except LookupError:
    order.append('base')
assert order == ['specific']

# a base-class clause catches the subclass when listed alone
try:
    raise IndexError('i')
except LookupError as e:
    assert isinstance(e, IndexError)
    assert isinstance(e, LookupError)
    assert isinstance(e, Exception)

# exceptions raised in except propagate; finally still runs
trace = []
def inner():
    try:
        raise ValueError('first')
    except ValueError:
        trace.append('inner-except')
        raise TypeError('second')
    finally:
        trace.append('inner-finally')
try:
    inner()
except TypeError as e:
    trace.append('outer:' + str(e))
assert trace == ['inner-except', 'inner-finally', 'outer:second']

# return inside finally is legal-but-overriding? avoid; instead: finally runs on return
def with_finally(log):
    try:
        return 'ret'
    finally:
        log.append('fin')
log = []
assert with_finally(log) == 'ret'
assert log == ['fin']

# bare raise re-raises the active exception
def reraise():
    try:
        raise ValueError('orig')
    except ValueError:
        raise
try:
    reraise()
except ValueError as e:
    assert str(e) == 'orig'

# exception instance carries its args
try:
    raise ValueError('a', 'b')
except ValueError as e:
    assert e.args == ('a', 'b')

# KeyError repr keeps the missing key visible
try:
    _ = {'x': 1}['y']
except KeyError as e:
    assert str(e) == "'y'"

# the name bound by 'as' is scoped to the except block (py3 deletes it after)
try:
    raise ValueError('gone')
except ValueError as err:
    captured = str(err)
assert captured == 'gone'
