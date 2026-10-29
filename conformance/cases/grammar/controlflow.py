# Adapted from CPython's test suite (Lib/test/test_grammar.py; exception basics from test_exceptions.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# for/else: else runs only without break
hits = []
for v in [1, 2, 3]:
    hits.append(v)
else:
    hits.append('else')
assert hits == [1, 2, 3, 'else']
hits = []
for v in [1, 2, 3]:
    if v == 2:
        break
    hits.append(v)
else:
    hits.append('else')
assert hits == [1]

# while/else, continue
i = 0
seen = []
while i < 5:
    i += 1
    if i % 2 == 0:
        continue
    seen.append(i)
else:
    seen.append('done')
assert seen == [1, 3, 5, 'done']

# nested loops: break only exits the inner one
pairs = []
for a in range(3):
    for b in range(3):
        if b > a:
            break
        pairs.append((a, b))
assert pairs == [(0, 0), (1, 0), (1, 1), (2, 0), (2, 1), (2, 2)]

# try/except/else/finally ordering
trace = []
try:
    trace.append('try')
except ValueError:
    trace.append('except')
else:
    trace.append('else')
finally:
    trace.append('finally')
assert trace == ['try', 'else', 'finally']

trace = []
try:
    trace.append('try')
    raise ValueError('boom')
except ValueError as e:
    trace.append('except:' + str(e))
else:
    trace.append('else')
finally:
    trace.append('finally')
assert trace == ['try', 'except:boom', 'finally']

# a tuple of exception types
def classify(fn):
    try:
        fn()
        return 'ok'
    except (TypeError, ValueError):
        return 'tv'
    except ZeroDivisionError:
        return 'zero'
assert classify(lambda: int('x')) == 'tv'
assert classify(lambda: 'a' + 1) == 'tv'
assert classify(lambda: 1 / 0) == 'zero'
assert classify(lambda: None) == 'ok'

# except Exception catches concrete errors
caught = None
try:
    _ = {}['missing']
except Exception as e:
    caught = type(e).__name__
assert caught == 'KeyError'

# finally runs on the exception path too
trace = []
def risky():
    try:
        raise ValueError('x')
    finally:
        trace.append('cleanup')
try:
    risky()
except ValueError:
    trace.append('caught')
assert trace == ['cleanup', 'caught']

# raise with a message; str() of the exception
try:
    raise ValueError('exact message')
except ValueError as e:
    assert str(e) == 'exact message'
