# Adapted from CPython's test suite (Lib/test/test_fstring.py: test_ast, test_conversions,
# test_format_specifier_expressions, test_debug_conversion, test_nested_fstrings, test_expressions).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

name = 'World'
n = 42
pi = 3.14159

# ---- basics ----
assert f'Hello {name}' == 'Hello World'
assert f'{n}' == '42'
assert f'{n + 1}' == '43'
assert f'' == ''
assert f'no fields' == 'no fields'
assert f'{{literal}}' == '{literal}'
assert f'{{{n}}}' == '{42}'
assert f'{name!r}' == "'World'"
assert f'{name!s}' == 'World'
assert f'{"é"!a}' == "'\\xe9'"
assert f'{n!r:>5}' == '   42'

# ---- format specs ----
assert f'{pi:.2f}' == '3.14'
assert f'{n:05d}' == '00042'
assert f'{n:x}' == '2a'
assert f'{n:#x}' == '0x2a'
assert f'{n:b}' == '101010'
assert f'{n:>6}' == '    42'
assert f'{n:<6}|' == '42    |'
assert f'{n:^6}|' == '  42  |'
assert f'{n:*^6}' == '**42**'
assert f'{1234567:,}' == '1,234,567'
assert f'{1234567:_}' == '1_234_567'
assert f'{0.5:%}' == '50.000000%'
assert f'{0.5:.0%}' == '50%'
assert f'{1e10:e}' == '1.000000e+10'
assert f'{12345.678:.3g}' == '1.23e+04'
assert f'{-n:+}' == '-42' and f'{n:+}' == '+42' and f'{n: }' == ' 42'
assert f'{name:>8}' == '   World'
assert f'{name:.3}' == 'Wor'
assert f'{name:_<8}' == 'World___'

# nested spec from variables
width = 8
prec = 3
assert f'{pi:{width}.{prec}f}' == '   3.142'
assert f'{pi:{width}}' == ' 3.14159'
fill = '-'
assert f'{n:{fill}>6}' == '----42'

# ---- expressions inside ----
assert f'{n * 2}' == '84'
assert f'{[1, 2, 3][1]}' == '2'
assert f'{ {"a": 1}["a"] }' == '1'
assert f'{name.lower()}' == 'world'
assert f'{len(name)}' == '5'
assert f'{"yes" if n > 40 else "no"}' == 'yes'
assert f'{(lambda v: v + 1)(n)}' == '43'
assert f'{n if n else "zero"}' == '42'
assert f'{n:{"d"}}' == '42'
assert f'{n!r:{width}}' == '42      '        # !r yields a str, which left-aligns
assert f'{n:{width}{"d"}}' == '      42'
assert f'{[v * 2 for v in range(3)]}' == '[0, 2, 4]'
assert f'{n}{n}{n}' == '424242'
assert f'{n}-{name}-{pi:.1f}' == '42-World-3.1'
assert f'{None}' == 'None' and f'{True}' == 'True'
assert f'{"a" "b"}' == 'ab'

# quotes: the other quote type inside the expression
assert f"{'single'}" == 'single'
assert f'{"double"}' == 'double'
assert f'{name!r}' == "'World'"

# ---- debug '=' specifier ----
assert f'{n=}' == 'n=42'
assert f'{n = }' == 'n = 42'
assert f'{n=:>5}' == 'n=   42'
assert f'{name=}' == "name='World'"           # = implies !r
assert f'{name=!s}' == 'name=World'
assert f'{n + 1=}' == 'n + 1=43'
assert f'{pi=:.2f}' == 'pi=3.14'

# ---- nested f-strings ----
assert f'{f"{n}"}' == '42'
assert f'{f"{name!r}"}' == "'World'"
inner = f'{n:03}'
assert f'[{inner}]' == '[042]'

# ---- prefixes: rf / fr, triple quotes, multi-line ----
assert rf'\n{n}' == '\\n42'
assert fr'{n}\t' == '42\\t'
assert f'''multi
{n}
line''' == 'multi\n42\nline'
assert f"""{name}""" == 'World'
assert F'{n}' == '42'                           # upper-case prefix

# implicit concatenation of f-strings and plain strings
assert f'{n}' 'x' f'{name}' == '42xWorld'
assert 'a' f'{n}' == 'a42'

# ---- escapes in literal parts ----
assert f'tab\t{n}' == 'tab\t42'
assert f'{n}\n' == '42\n'
assert f'\x41{n}' == 'A42'
assert f'é{n}' == 'é42'

# ---- objects with __repr__ ----
class P:
    def __init__(self, x):
        self.x = x

    def __repr__(self):
        return 'P(' + str(self.x) + ')'

assert f'{P(1)}' == 'P(1)'
assert f'{P(1)!r}' == 'P(1)'
assert f'{[P(1), P(2)]}' == '[P(1), P(2)]'

# ---- format() / str.format equivalence ----
assert f'{n:>6}' == format(n, '>6') == '{:>6}'.format(n)
assert f'{pi:.2f}' == '{:.2f}'.format(pi) == '%.2f' % pi

# ---- errors surface as ValueError, not crashes ----
try:
    f'{n:zz}'
    assert False
except ValueError:
    pass
try:
    f'{name:d}'
    assert False
except ValueError:
    pass
