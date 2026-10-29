# Adapted from CPython's test suite (custom exception classes, Lib/test/test_exceptions.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

class AppError(Exception):
    pass

class ConfigError(AppError):
    pass

# raise and catch the exact class
try:
    raise AppError('broken')
except AppError as e:
    assert str(e) == 'broken'
    assert isinstance(e, Exception)

# the subclass is caught by its base
caught = None
try:
    raise ConfigError('cfg')
except AppError as e:
    caught = type(e).__name__
assert caught == 'ConfigError'

# but the base is NOT caught by the subclass clause
def classify():
    try:
        raise AppError('base')
    except ConfigError:
        return 'sub'
    except AppError:
        return 'base'
assert classify() == 'base'

# custom exceptions work in the generic machinery
def boom():
    raise ConfigError('x')

def collect_errors(fns):
    out = []
    for fn in fns:
        try:
            fn()
        except Exception as e:
            out.append(type(e).__name__)
    return out

assert collect_errors([lambda: 1 / 0, boom]) == ['ZeroDivisionError', 'ConfigError']
