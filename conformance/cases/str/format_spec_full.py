# Adapted from CPython's test suite (Lib/test/test_format.py, Lib/test/test_types.py: test_int__format__/
# test_float__format__, Lib/test/test_unicode.py test_format, test_str.py __mod__ cases).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# The whole format-spec mini-language plus the % operator.

# ---- fill / align / width ----
assert format('x', '5') == 'x    ' and format(5, '5') == '    5'          # str left, numbers right
assert format('x', '<5') == 'x    ' and format('x', '>5') == '    x' and format('x', '^5') == '  x  '
assert format('x', '*<5') == 'x****' and format('x', '*>5') == '****x' and format('x', '*^5') == '**x**'
assert format(-42, '=8') == '-     42' and format(42, '0=8') == '00000042' and format(-42, '0=8') == '-0000042'
assert format('abc', '2') == 'abc' and format('abc', '.2') == 'ab' and format('abc', '5.2') == 'ab   '
assert format(5, '^6') == '  5   ' and format('ab', '^5') == ' ab  '
assert format('x', ' <3') == 'x  ' and format(1, '0<3') == '100'

# ---- sign / zero / grouping ----
assert format(42, '+') == '+42' and format(-42, '+') == '-42' and format(42, '-') == '42' and format(42, ' ') == ' 42'
assert format(42, '08') == '00000042' and format(-42, '08') == '-0000042' and format(42, '+08') == '+0000042'
assert format(1234567, ',') == '1,234,567' and format(1234567, '_') == '1_234_567' and format(-1234, ',') == '-1,234'
assert format(1234567.891, ',.2f') == '1,234,567.89' and format(123, ',') == '123' and format(1234, ',d') == '1,234'
assert format(255, '_x') == 'ff' and format(0xFFFFF, '_x') == 'f_ffff' and format(0b1111111, '_b') == '111_1111'
assert format(1234, '08,') == '0,001,234' and format(12, '0>5,') == '00012'
assert format('ab', '08') == 'ab000000'                                # 3.10+: '0' with str sets the fill only

# ---- integer presentation types ----
assert format(42, 'd') == '42' and format(42, 'b') == '101010' and format(42, 'o') == '52' and format(42, 'x') == '2a'
assert format(42, 'X') == '2A' and format(42, 'c') == '*' and format(42, 'n') == '42'
assert format(42, '#b') == '0b101010' and format(42, '#o') == '0o52' and format(42, '#x') == '0x2a' and format(42, '#X') == '0X2A'
assert format(-42, '#x') == '-0x2a' and format(0, '#x') == '0x0' and format(42, '#010x') == '0x0000002a'
assert format(2 ** 70, 'x') == '400000000000000000' and format(2 ** 70, ',') == '1,180,591,620,717,411,303,424'
assert format(True, 'd') == '1' and format(True, '') == 'True' and format(True, 'x') == '1'
assert format(42, 'e') == '4.200000e+01' and format(42, 'f') == '42.000000' and format(42, '.1%') == '4200.0%'
assert format(42, 'g') == '42' and format(42, '.3g') == '42'
try:
    format(42, 's')
    assert False
except ValueError:
    pass
try:
    format(42, '.2d')
    assert False
except ValueError:
    pass
try:
    format(42, ',x') and format(42, ',b')
    assert False
except ValueError:
    pass

# ---- float presentation types ----
x = 1234.5678
assert format(x, 'f') == '1234.567800' and format(x, '.2f') == '1234.57' and format(x, '.0f') == '1235'
assert format(x, 'F') == '1234.567800' and format(x, 'e') == '1.234568e+03' and format(x, '.2e') == '1.23e+03'
assert format(x, 'E') == '1.234568E+03' and format(x, 'g') == '1234.57' and format(x, '.3g') == '1.23e+03'
assert format(x, 'G') == '1234.57' and format(x, '%') == '123456.780000%' and format(0.5, '.1%') == '50.0%'
assert format(x, 'n') == '1234.57' and format(x, '') == '1234.5678' and format(x, '.3') == '1.23e+03'
assert format(1e20, 'g') == '1e+20' and format(1e-5, 'g') == '1e-05' and format(0.0001, 'g') == '0.0001'
assert format(100000.0, 'g') == '100000' and format(1000000.0, 'g') == '1e+06' and format(123456789.0, 'g') == '1.23457e+08'
assert format(1.0, 'g') == '1' and format(1.5, 'g') == '1.5' and format(1.0, '#g') == '1.00000' and format(1.0, '#.0f') == '1.'
assert format(0.1, '.20f') == '0.10000000000000000555' and format(2.5, '.0f') == '2' and format(3.5, '.0f') == '4'
assert format(1e308 * 10, 'f') == 'inf' and format(-1e308 * 10, 'F') == '-INF' and format(float('nan'), 'f') == 'nan'
assert format(float('inf'), '%') == 'inf%' and format(float('nan'), '.1%') == 'nan%'
assert format(float('nan'), 'F') == 'NAN' and format(float('inf'), 'e') == 'inf' and format(float('inf'), '>5') == '  inf'
assert format(-0.0, 'f') == '-0.000000' and format(-0.0, '+.1f') == '-0.0' and format(0.0, '+.1f') == '+0.0'
assert format(1.5, '+.1f') == '+1.5' and format(1.5, ' .1f') == ' 1.5' and format(12.5, '08.2f') == '00012.50'
assert format(12.5, '>8.2f') == '   12.50' and format(12.5, '<8.2f') == '12.50   ' and format(12.5, '^8.1f') == '  12.5  '
assert format(1e16, '') == '1e+16' and format(1e15, '') == '1000000000000000.0' and format(123456789012345680.0, '') == '1.2345678901234568e+17'
assert format(0.000001, '') == '1e-06' and format(0.0001, '') == '0.0001'
assert format(3.0, 'd' if False else '.0f') == '3'
try:
    format(1.5, 'd')
    assert False
except ValueError:
    pass
try:
    format(1.5, 'x')
    assert False
except ValueError:
    pass

# ---- str presentation ----
assert format('abc', 's') == 'abc' and format('abc', '') == 'abc' and format('abc', '>5s') == '  abc'
assert format('', '3') == '   ' and format('é', '3') == 'é  '
try:
    format('abc', 'd')
    assert False
except ValueError:
    pass
try:
    format('abc', '=5')
    assert False
except ValueError:
    pass

# ---- format() on other types ----
assert format(None, '') == 'None' and format([1, 2], '') == '[1, 2]'
try:
    format(None, '>5')
    assert False
except TypeError:
    pass

# ---- % operator ----
assert '%d' % 42 == '42' and '%5d' % 42 == '   42' and '%-5d|' % 42 == '42   |' and '%05d' % 42 == '00042'
assert '%+d' % 42 == '+42' and '% d' % 42 == ' 42' and '%d' % -42 == '-42' and '%d' % 3.99 == '3'
assert '%i' % 7 == '7' and '%u' % 7 == '7' and '%x' % 255 == 'ff' and '%X' % 255 == 'FF' and '%#x' % 255 == '0xff'
assert '%o' % 8 == '10' and '%#o' % 8 == '0o10' and '%e' % 1234.5 == '1.234500e+03' and '%E' % 1234.5 == '1.234500E+03'
assert '%f' % 1.5 == '1.500000' and '%.2f' % 1.005 == '1.00' and '%.0f' % 2.5 == '2' and '%10.3f' % 3.14159 == '     3.142'
assert '%g' % 1234567.0 == '1.23457e+06' and '%g' % 0.5 == '0.5' and '%G' % 1e-10 == '1E-10'
assert '%s' % 'x' == 'x' and '%s' % 42 == '42' and '%s' % None == 'None' and '%s' % [1] == '[1]'
assert '%r' % 'x' == "'x'" and '%a' % 'é' == "'\\xe9'" and '%c' % 65 == 'A' and '%c' % 'B' == 'B'
assert '%%' % () == '%' and '100%%' % () == '100%' and '%d%%' % 5 == '5%'
assert '%s and %s' % ('a', 'b') == 'a and b' and '%d-%d' % (1, 2) == '1-2'
assert '%(name)s is %(age)d' % {'name': 'Bob', 'age': 30} == 'Bob is 30'
assert '%(a)s' % {'a': 1, 'b': 2} == '1'
assert '%*d' % (5, 42) == '   42' and '%.*f' % (2, 3.14159) == '3.14' and '%-*d|' % (4, 7) == '7   |'
assert '%5s' % 'ab' == '   ab' and '%-5s|' % 'ab' == 'ab   |' and '%.2s' % 'abcdef' == 'ab'
assert '%s' % (1,) == '1' and '%s' % ((1, 2),) == '(1, 2)' and '%s %s' % ((1, 2), 3) == '(1, 2) 3'
assert '%d' % True == '1' and '%s' % True == 'True' and '%x' % 2 ** 70 == '400000000000000000'
assert '%.3e' % 0 == '0.000e+00' and '%d' % 2 ** 100 == str(2 ** 100)
assert 'no format' % () == 'no format'
try:
    '%d' % 'x'
    assert False
except TypeError:
    pass
try:
    '%d %d' % (1,)
    assert False
except TypeError:
    pass
try:
    '%d' % (1, 2)
    assert False
except TypeError:
    pass
try:
    '%(a)s' % {'b': 1}
    assert False
except KeyError:
    pass
try:
    '%q' % 1
    assert False
except ValueError:
    pass
try:
    '%z' % 1
    assert False
except ValueError:
    pass
try:
    '%' % ()
    assert False
except ValueError:
    pass
