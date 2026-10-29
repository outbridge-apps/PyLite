# Adapted from CPython's test suite (Lib/test/test_format.py, test_fstring.py basics).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# str.format: positions and names
assert '{}-{}'.format(1, 2) == '1-2'
assert '{0}{1}{0}'.format('a', 'b') == 'aba'
assert '{name}={val}'.format(name='x', val=3) == 'x=3'

# alignment and width
assert '{:>5}'.format('ab') == '   ab'
assert '{:<5}'.format('ab') == 'ab   '
assert '{:^6}'.format('ab') == '  ab  '
assert '{:*^6}'.format('ab') == '**ab**'

# numeric formats
assert '{:05d}'.format(42) == '00042'
assert '{:+d}'.format(42) == '+42'
assert '{:.3f}'.format(2.5) == '2.500'
assert '{:.0f}'.format(2.5) == '2'          # ties-to-even in formatting too
assert '{:,}'.format(1234567) == '1,234,567'
assert '{:x}'.format(255) == 'ff'
assert '{:X}'.format(255) == 'FF'
assert '{:o}'.format(8) == '10'
assert '{:b}'.format(10) == '1010'
assert '{:%}'.format(0.25) == '25.000000%'
assert '{:.1%}'.format(0.25) == '25.0%'
assert '{:e}'.format(1234.5) == '1.234500e+03'

# f-strings
x = 42
name = 'world'
assert f'{x}' == '42'
assert f'hello {name}!' == 'hello world!'
assert f'{x + 1}' == '43'
assert f'{x:>5}' == '   42'
assert f'{x:06}' == '000042'
assert f'{3.14159:.2f}' == '3.14'
assert f'{name!r}' == "'world'"
assert f'{{literal}}' == '{literal}'
assert f'{"in" + "line"}' == 'inline'

# nested format spec (width taken from a variable)
w = 5
assert f'{x:{w}}' == '   42'

# conversions inside format()
assert '{!r}'.format('a') == "'a'"
assert '{!s}'.format(1.5) == '1.5'
