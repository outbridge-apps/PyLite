# Adapted from CPython's test suite (Lib/test/test_traceback.py: TracebackFormatTests.test_format_exc /
# test_exception_only, BaseExceptionReportingTests.test_cause / test_context_suppression).
# Python Software Foundation, PSF License Version 2 -- see README.
# dialect: the file name is "<script>" (CPython: the real file or "<string>"), no source line is
# echoed under a frame, and there is no traceback object (the (type, value, tb) triple takes an
# exception object in the value slot).
import traceback

assert traceback.format_exc() == 'NoneType: None\n'                 # nothing being handled

def inner():
    raise ValueError('bad value')

def outer():
    inner()

try:
    outer()
except ValueError:
    text = traceback.format_exc()
lines = text.split('\n')
assert lines[0] == 'Traceback (most recent call last):' and lines[-1] == '' and lines[-2] == 'ValueError: bad value'
assert lines[1].endswith(', in <module>') and lines[2].endswith(', in outer') and lines[3].endswith(', in inner')
assert all([l.startswith('  File "<script>", line ') for l in lines[1:4]])

# chained exceptions render both blocks; chain=False only the last
try:
    try:
        {}['k']
    except KeyError as e:
        raise RuntimeError('wrapped') from e
except RuntimeError as e:
    full = traceback.format_exc()
    last = traceback.format_exc(chain=False)
    err = e
assert 'The above exception was the direct cause of the following exception:' in full
assert full.startswith('Traceback (most recent call last):') and full.endswith('RuntimeError: wrapped\n')
assert "KeyError: 'k'" in full and last.count('Traceback') == 1 and last.endswith('RuntimeError: wrapped\n')

# format_exception / format_exception_only on an exception object
assert traceback.format_exception_only(err) == ['RuntimeError: wrapped\n']
assert traceback.format_exception_only(ValueError()) == ['ValueError\n']
parts = traceback.format_exception(err)
assert parts[0] == 'Traceback (most recent call last):\n' and parts[-1] == 'RuntimeError: wrapped\n' and ''.join(parts) == full
assert traceback.format_exception(type(err), err, None) == parts
e = ValueError('n')
e.add_note('note 1')
assert traceback.format_exception_only(e) == ['ValueError: n\n', 'note 1\n']

# the reporting idiom: last line of the traceback as the message
def describe():
    return traceback.format_exc().strip().split('\n')[-1]
try:
    int('x')
except ValueError:
    assert describe() == "ValueError: invalid literal for int() with base 10: 'x'"
