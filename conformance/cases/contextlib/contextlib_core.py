# Adapted from CPython's test suite (Lib/test/test_contextlib.py: TestSuppress, NullcontextTestCase,
# ClosingTestCase). Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# dialect: contextlib has suppress, nullcontext and closing only (no generators, so no contextmanager).
from contextlib import suppress, nullcontext, closing
import io

def raises(exc, f):
    try:
        f()
    except exc as e:
        return str(e)
    return None

# ---- suppress ----
with suppress(ValueError) as enter_result:
    assert enter_result is None

with suppress(ValueError):
    assert pow(2, 5) == 32

with suppress(TypeError):
    len(5)

with suppress(LookupError):           # the hierarchy: IndexError is a LookupError
    'Hello'[50]

def other_exception():
    with suppress(TypeError):
        1 / 0
assert raises(ZeroDivisionError, other_exception) is not None

def no_args():
    with suppress():
        1 / 0
assert raises(ZeroDivisionError, no_args) is not None

with suppress(ZeroDivisionError, TypeError):
    1 / 0
with suppress(ZeroDivisionError, TypeError):
    len(5)

ignore_exceptions = suppress(Exception)      # reentrant
with ignore_exceptions:
    pass
with ignore_exceptions:
    len(5)
outer_continued = False
with ignore_exceptions:
    with ignore_exceptions:
        len(5)
    outer_continued = True
    1 / 0
assert outer_continued

reached = []
for i in range(3):
    with suppress(KeyError):
        reached.append(i)
        {}[i]
        reached.append('never')
assert reached == [0, 1, 2]

def suppressed_value():
    with suppress(ValueError):
        return int('x')
    return 'after'
assert suppressed_value() == 'after'

# a user exception class
class AppError(Exception):
    pass
with suppress(AppError):
    raise AppError('quiet')

# ---- nullcontext ----
class C:
    pass
c = C()
with nullcontext(c) as c_in:
    assert c_in is c
with nullcontext() as nothing:
    assert nothing is None
with nullcontext(enter_result=5) as five:
    assert five == 5

def maybe_open(data=None):
    return io.StringIO(data) if data is not None else nullcontext('default')
with maybe_open() as f:
    assert f == 'default'
with maybe_open('text') as f:
    assert f.read() == 'text'

# ---- closing ----
state = []
class Resource:
    def close(self):
        state.append(1)
x = Resource()
assert state == []
with closing(x) as y:
    assert x is y
assert state == [1]

def closing_error():
    with closing(x) as y:
        assert x is y
        1 / 0
assert raises(ZeroDivisionError, closing_error) is not None
assert state == [1, 1]

buf = io.BytesIO(b'abc')
with closing(buf) as b:
    assert b.read() == b'abc'
assert buf.closed
