# Adapted from CPython's test suite (Lib/test/test_format.py: test_bytes_and_bytearray_format,
# Lib/test/test_bytes.py). Python Software Foundation, PSF License Version 2 -- see README.
# bytes % args (PEP 461, CPython 3.5+).

assert b'%d' % 5 == b'5'
assert b'%s' % b'x' == b'x' and b'%b' % b'x' == b'x'
assert b'%s=%d' % (b'a', 1) == b'a=1'
assert b'%(k)s-%(n)d' % {'k': b'v', 'n': 2} == b'v-2'

# %a keeps the result ASCII whatever the operand is; %r is its alias here
assert b'%a' % 'x' == b"'x'"
assert b'%a' % 'ш' == b"'\\u0448'"
assert b'%r' % b'x' == b"b'x'"

# %c takes an int in range(256) or a single byte
assert b'%c' % 65 == b'A' and b'%c' % b'A' == b'A'

# the numeric conversions and the flag/width/precision grammar are the ones str already had
assert b'%x' % 255 == b'ff' and b'%#x' % 255 == b'0xff' and b'%o' % 8 == b'10'
assert b'%.2f' % 1.5 == b'1.50' and b'%e' % 1.5 == b'1.500000e+00'
assert b'%5d|' % 42 == b'   42|' and b'%-5d|' % 42 == b'42   |'
assert b'%05.1f' % -1.5 == b'-01.5' and b'%+d' % 3 == b'+3'
assert b'%*d' % (5, 42) == b'   42'
assert b'100%%' % () == b'100%'
assert b'%.2s' % b'abcd' == b'ab'

# both the format and the operand are byte-transparent
assert b'\xff%d\xfe' % 7 == b'\xff7\xfe'
assert b'%s' % b'\xff\x00\xfe' == b'\xff\x00\xfe'

# ---- the operand of %s / %b must BE bytes; str is refused ----
try:
    b'%s' % 'x'
    assert False
except TypeError as e:
    assert str(e) == "%b requires a bytes-like object, or an object that implements __bytes__, not 'str'"
try:
    b'%c' % 300
    assert False
except TypeError as e:
    assert str(e) == '%c requires an integer in range(256) or a single byte'
try:
    b'%s %s' % (b'a',)
    assert False
except TypeError as e:
    assert str(e) == 'not enough arguments for format string'
try:
    b'%s' % (b'a', b'b')
    assert False
except TypeError as e:
    assert str(e) == 'not all arguments converted during bytes formatting'

# ---- %b belongs to bytes only, and the operator does not reflect ----
try:
    '%b' % 1
    assert False
except ValueError:
    pass
try:
    5 % b'x'
    assert False
except TypeError:
    pass
