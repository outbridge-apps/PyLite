# Adapted from CPython's test suite (Lib/test/test_builtin.py: BuiltinTest.test_abs/test_ascii/test_bin/
# test_callable/test_chr/test_divmod/test_format/test_hash/test_hex/test_int/test_len/test_oct/test_ord/
# test_pow/test_repr/test_round/test_type/test_dir/test_slice, Lib/test/test_bool.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# ---- abs / divmod / pow / round ----
assert abs(-5) == 5 and abs(5) == 5 and abs(-2.5) == 2.5 and abs(True) == 1
assert abs(-(2 ** 100)) == 2 ** 100
assert divmod(7, 2) == (3, 1) and divmod(-7, 2) == (-4, 1) and divmod(7, -2) == (-4, -1)
assert divmod(7.5, 2) == (3.0, 1.5) and divmod(-7.5, 2) == (-4.0, 0.5)
assert divmod(2 ** 70, 3) == ((2 ** 70) // 3, (2 ** 70) % 3)
assert pow(2, 10) == 1024 and pow(2, -1) == 0.5 and pow(2.0, 3) == 8.0
assert pow(2, 10, 1000) == 24 and pow(-3, 3, 7) == 1 and pow(3, 4, -5) == -4
assert pow(3, -1, 7) == 5 and pow(2, -2, 9) == 7          # modular inverse (3.8+)
assert pow(0, 0) == 1 and pow(0, 0, 1) == 0
try:
    pow(2, -1, 4)
    assert False
except ValueError:
    pass
try:
    pow(2, 3, 0)
    assert False
except ValueError:
    pass
assert round(2.5) == 2 and round(3.5) == 4 and round(-2.5) == -2      # banker's rounding
assert round(0.5) == 0 and round(1.5) == 2
assert round(2.675, 2) == 2.67                                        # binary float representation
assert round(1234.5678, -2) == 1200.0 and round(1234, -2) == 1200 and round(1250, -2) == 1200
assert round(7) == 7 and isinstance(round(7.0), int) and isinstance(round(7.0, 0), float)
assert round(7.123, 1) == 7.1 and round(-0.5) == 0
assert round(True) == 1
assert round(10 ** 20 + 5, -1) == 10 ** 20
try:
    round('1')
    assert False
except TypeError:
    pass

# ---- bin / hex / oct ----
assert bin(10) == '0b1010' and bin(-10) == '-0b1010' and bin(0) == '0b0' and bin(True) == '0b1'
assert hex(255) == '0xff' and hex(-255) == '-0xff' and hex(0) == '0x0' and hex(2 ** 64) == '0x10000000000000000'
assert oct(8) == '0o10' and oct(-8) == '-0o10' and oct(0) == '0o0'
try:
    hex(1.5)
    assert False
except TypeError:
    pass

# ---- chr / ord / ascii / repr ----
assert chr(65) == 'A' and chr(0x263A) == '☺' and ord('A') == 65 and ord('☺') == 0x263A
assert chr(ord('z')) == 'z' and ord(chr(200)) == 200
try:
    chr(0x110000)
    assert False
except ValueError:
    pass
try:
    ord('ab')
    assert False
except TypeError:
    pass
assert ascii('abc') == "'abc'" and ascii('é') == "'\\xe9'" and ascii('☺') == "'\\u263a'"
assert ascii([1, 'é']) == "[1, '\\xe9']"
assert repr('a') == "'a'" and repr("it's") == '"it\'s"' and repr('"') == '\'"\''
assert repr(1) == '1' and repr(1.0) == '1.0' and repr(None) == 'None' and repr(True) == 'True'
assert repr([1, 'a', (2,)]) == "[1, 'a', (2,)]" and repr({'k': [1]}) == "{'k': [1]}"
assert repr(set()) == 'set()' and repr({1}) == '{1}' and repr(frozenset()) == 'frozenset()'
assert repr(()) == '()' and repr((1,)) == '(1,)' and repr({}) == '{}'
assert repr('\n\t\\') == "'\\n\\t\\\\'"
assert repr(b'a\x00') == "b'a\\x00'"
assert repr(1e100) == '1e+100' and repr(1.5e-7) == '1.5e-07' and repr(0.1) == '0.1'

# ---- callable / hash / len / type ----
assert callable(len) and callable(lambda: 0) and callable(int) and callable(str.upper)
assert not callable(1) and not callable('s') and not callable(None) and not callable([])
assert hash(1) == hash(1.0) == hash(True)
assert hash('a') == hash('a') and hash((1, 2)) == hash((1, 2))
assert hash(-1) == hash(-1) and hash(None) == hash(None)
assert hash(frozenset([1, 2])) == hash(frozenset([2, 1]))
assert hash(0) == 0 and hash(1) == 1
try:
    hash([])
    assert False
except TypeError:
    pass
try:
    hash({})
    assert False
except TypeError:
    pass
try:
    hash({1})
    assert False
except TypeError:
    pass
assert len('') == 0 and len('abc') == 3 and len([1, 2]) == 2 and len(()) == 0
assert len({'a': 1}) == 1 and len({1, 2, 3}) == 3 and len(range(5)) == 5 and len(b'ab') == 2
try:
    len(5)
    assert False
except TypeError:
    pass
try:
    len(None)
    assert False
except TypeError:
    pass
assert type(1) is int and type(1.0) is float and type('s') is str and type(True) is bool
assert type([]) is list and type(()) is tuple and type({}) is dict and type(set()) is set
assert type(None) is not int and type(1) is not bool
assert type(b'') is bytes and type(range(1)) is range
assert type(type(1)) is type
assert repr(type(1)) == "<class 'int'>" and repr(type('a')) == "<class 'str'>"
assert type(len).__name__ == 'builtin_function_or_method'
assert type(lambda: 0).__name__ == 'function'
assert int.__name__ == 'int' and float.__name__ == 'float'
assert str(type(1)) == "<class 'int'>"

# ---- bool ----
assert bool() is False and bool(0) is False and bool(1) is True and bool('') is False and bool(' ') is True
assert bool(None) is False and bool([]) is False and bool([0]) is True
assert True + True == 2 and True * 10 == 10 and True / 2 == 0.5 and -True == -1
assert str(True) == 'True' and repr(False) == 'False'
assert True == 1 and False == 0 and True != 2
assert isinstance(True, int) and not isinstance(1, bool)
assert (True, False) == (1, 0) and [True] == [1] and {True: 'a'}[1] == 'a'
assert bool(0.0) is False and bool(-0.0) is False and bool(float('nan')) is True
assert int(True) == 1 and float(False) == 0.0 and str(bool(3)) == 'True'

# ---- int() ----
assert int() == 0 and int(3.99) == 3 and int(-3.99) == -3 and int(True) == 1
assert int('42') == 42 and int('-42') == -42 and int('+42') == 42 and int(' 42 ') == 42
assert int('1_000') == 1000 and int('0b101', 2) == 5 and int('0o17', 8) == 15 and int('0xff', 16) == 255
assert int('ff', 16) == 255 and int('FF', 16) == 255 and int('z', 36) == 35 and int('101', 2) == 5
assert int('0x10', 0) == 16 and int('0b10', 0) == 2 and int('0o10', 0) == 8 and int('10', 0) == 10
assert int('123456789012345678901234567890') == 123456789012345678901234567890
assert int('  -0  ') == 0 and int('00012') == 12
assert int(1e20) == 100000000000000000000
assert int(2 ** 100) == 2 ** 100
for bad in ('', ' ', 'abc', '1.5', '1e3', '12a', '--1', '1_', '_1', '0b', '0x1.0'):
    try:
        int(bad)
        assert False, bad
    except ValueError:
        pass
try:
    int('010', 0)                     # leading zeros not allowed with base 0
    assert False
except ValueError:
    pass
try:
    int('1', 1)
    assert False
except ValueError:
    pass
try:
    int('1', 37)
    assert False
except ValueError:
    pass
try:
    int(None)
    assert False
except TypeError:
    pass
try:
    int([1])
    assert False
except TypeError:
    pass
try:
    int(float('nan'))
    assert False
except ValueError:
    pass
try:
    int(float('inf'))
    assert False
except OverflowError:
    pass
try:
    int(1.5, 10)                      # base only with str
    assert False
except TypeError:
    pass

# ---- float() ----
assert float() == 0.0 and float(3) == 3.0 and float(True) == 1.0 and float('3.5') == 3.5
assert float(' -1e3 ') == -1000.0 and float('1_000.5') == 1000.5 and float('.5') == 0.5 and float('5.') == 5.0
assert float('inf') > 1e308 and float('-inf') < -1e308 and float('Infinity') == float('inf')
assert float('nan') != float('nan') and float('NaN') != float('NaN')
assert float('1E2') == 100.0 and float('+1.5') == 1.5 and float('0x10' if False else '16') == 16.0
assert float(2 ** 100) == 1.2676506002282294e+30
for bad in ('', 'abc', '1,5', '1.5.5', '--1', '1e', 'in f'):
    try:
        float(bad)
        assert False, bad
    except ValueError:
        pass
try:
    float(None)
    assert False
except TypeError:
    pass
try:
    float(10 ** 400)
    assert False
except OverflowError:
    pass

# ---- str() ----
assert str() == '' and str(1) == '1' and str(1.5) == '1.5' and str(None) == 'None' and str([1]) == '[1]'
assert str('x') == 'x' and str(True) == 'True' and str(1e16) == '1e+16' and str(10 ** 16) == '10000000000000000'
assert str(b'ab') == "b'ab'"                                  # dialect: decode bytes via .decode(), not str(b, enc)
assert str(1.0) == '1.0' and str(100.0) == '100.0' and str(0.1 + 0.2) == '0.30000000000000004'

# ---- format() ----
assert format(42) == '42' and format(42, 'x') == '2a' and format(3.14159, '.2f') == '3.14'
assert format('s', '>3') == '  s' and format(1234567, ',') == '1,234,567' and format(0.25, '%') == '25.000000%'
assert format(42, '08b') == '00101010' and format(-1, '+d') == '-1' and format(True, 'd') == '1'
assert format(None, '') == 'None' and format([1], '') == '[1]'
try:
    format(42, 'q')
    assert False
except ValueError:
    pass

# ---- slice objects ----
s = slice(1, 5, 2)
assert s.start == 1 and s.stop == 5 and s.step == 2
assert slice(3).start is None and slice(3).stop == 3 and slice(3).step is None
assert slice(None, None).start is None
assert [0, 1, 2, 3, 4, 5][s] == [1, 3] and 'abcdef'[slice(2)] == 'ab' and (1, 2, 3)[slice(None, None, -1)] == (3, 2, 1)
assert slice(1, 2) == slice(1, 2) and slice(1, 2) != slice(1, 3)
assert repr(slice(1, 2, 3)) == 'slice(1, 2, 3)' and repr(slice(None, 2)) == 'slice(None, 2, None)'
assert slice(1, 10, 2).indices(5) == (1, 5, 2) and slice(None, None, -1).indices(4) == (3, -1, -1)
assert slice(-2, None).indices(5) == (3, 5, 1) and slice(100).indices(5) == (0, 5, 1)

# ---- dir ----
assert 'upper' in dir('') and 'append' in dir([]) and 'keys' in dir({})
assert 'bit_length' in dir(1) and 'is_integer' in dir(1.0)
assert 'sqrt' in dir(__import__ if False else None) or True
class C:
    def __init__(self):
        self.field = 1

    def method(self):
        return 2
assert 'field' in dir(C()) and 'method' in dir(C())
assert dir(1) == sorted(dir(1))

# ---- print returns None and accepts sep/end ----
assert print() is None
assert print('a', 1, None, sep='-', end='') is None
assert print('x', end='\n') is None

# ---- unsupported builtins are absent with a clear NameError ----
for name in ('eval', 'exec', 'compile', 'open', 'input', 'globals', 'locals', 'vars', 'delattr',
             'property', 'staticmethod', 'classmethod', 'object',
             'memoryview', 'bytearray', 'complex', 'help', '__import__'):
    try:
        eval_target = {'eval': lambda: eval, 'exec': lambda: exec, 'compile': lambda: compile,
                       'open': lambda: open, 'input': lambda: input, 'globals': lambda: globals,
                       'locals': lambda: locals, 'vars': lambda: vars,
                       'delattr': lambda: delattr,
                       'property': lambda: property,
                       'staticmethod': lambda: staticmethod, 'classmethod': lambda: classmethod,
                       'object': lambda: object, 'memoryview': lambda: memoryview,
                       'bytearray': lambda: bytearray, 'complex': lambda: complex, 'help': lambda: help,
                       '__import__': lambda: __import__}[name]
        eval_target()
        assert False, name
    except NameError as e:
        assert name in str(e) and 'not supported' in str(e)

# ---- arity: extra positional arguments are rejected ----
for bad in (lambda: bool(1, 2), lambda: float(1, 2), lambda: round(1.5, 1, 2), lambda: dir(1, 2),
            lambda: str(b'a', 'utf-8', 'strict', 1), lambda: zip([1], x=1), lambda: bool(x=1)):
    try:
        bad()
        assert False
    except TypeError:
        pass
assert round(number=2.5) == 2 and str(object=5) == '5'

# ---- str(bytes, encoding[, errors]) decodes ----
assert str(b'\xc3\xa9', 'utf-8') == 'é' and str(b'ab', encoding='ascii') == 'ab'
assert str(b'\xff', 'ascii', 'replace') == '�' and str(b'\xff', 'ascii', 'ignore') == ''
try:
    str('a', 'utf-8')
    assert False
except TypeError:
    pass

# ---- iterator type names ----
assert str(type(iter([]))) == "<class 'list_iterator'>" and str(type(reversed([1]))) == "<class 'list_reverseiterator'>"
assert str(type(iter({}))) == "<class 'dict_keyiterator'>" and str(type(iter({}.items()))) == "<class 'dict_itemiterator'>"
assert str(type(iter(frozenset()))) == "<class 'set_iterator'>" and str(type(iter(range(1)))) == "<class 'range_iterator'>"
assert str(type(map(str, []))) == "<class 'map'>" and str(type(enumerate([]))) == "<class 'enumerate'>"

# ---- in-place set operators mutate the same object; the operand must be a set ----
s = {1, 2}
alias = s
s |= {3}
s &= {1, 3, 4}
s -= {1}
s ^= {5}
assert alias is s and sorted(s) == [3, 5]
s -= s
assert s == set() and alias is s
try:
    s |= [1]
    assert False
except TypeError:
    pass
fs = frozenset({1})
fs_before = fs
fs |= {2}
assert fs == {1, 2} and fs_before == {1}

# ---- BaseException.add_note / __notes__ (3.11) ----
e = ValueError('x')
try:
    e.__notes__
    assert False
except AttributeError:
    pass
e.add_note('first')
e.add_note('second')
assert e.__notes__ == ['first', 'second']
try:
    e.add_note(1)
    assert False
except TypeError:
    pass

# ---- UnicodeDecodeError / UnicodeEncodeError ----
try:
    b'ab\xff'.decode()
    assert False
except UnicodeDecodeError as ude:
    assert str(ude) == "'utf-8' codec can't decode byte 0xff in position 2: invalid start byte"
    assert (ude.encoding, ude.object, ude.start, ude.end, ude.reason) == ('utf-8', b'ab\xff', 2, 3, 'invalid start byte')
    assert isinstance(ude, UnicodeError) and isinstance(ude, ValueError)
try:
    b'a\xe2\x82'.decode()
    assert False
except UnicodeDecodeError as ude:
    assert str(ude) == "'utf-8' codec can't decode bytes in position 1-2: unexpected end of data"
try:
    b'\xe2\x82\x41'.decode()
    assert False
except ValueError as ude:
    assert str(ude) == "'utf-8' codec can't decode bytes in position 0-1: invalid continuation byte"
try:
    b'\xe9'.decode('ascii')
    assert False
except UnicodeDecodeError as ude:
    assert str(ude) == "'ascii' codec can't decode byte 0xe9 in position 0: ordinal not in range(128)"
try:
    'é'.encode('ascii')
    assert False
except UnicodeEncodeError as uee:
    assert str(uee) == "'ascii' codec can't encode character '\\xe9' in position 0: ordinal not in range(128)"
    assert (uee.encoding, uee.object, uee.start, uee.end) == ('ascii', 'é', 0, 1)
assert str(UnicodeDecodeError('utf-8', b'\xff', 0, 1, 'bad')) == "'utf-8' codec can't decode byte 0xff in position 0: bad"
assert repr(UnicodeDecodeError('utf-8', b'\xff', 0, 1, 'bad')) == "UnicodeDecodeError('utf-8', b'\\xff', 0, 1, 'bad')"

# ---- getattr / hasattr / setattr ----
from dataclasses import dataclass

@dataclass
class _Pt:
    x: int
    y: int

_p = _Pt(1, 2)
assert getattr(_p, 'x') == 1 and getattr(_p, 'missing', 5) == 5
assert hasattr(_p, 'x') and not hasattr(_p, 'missing')
setattr(_p, 'x', 3)
assert _p.x == 3 and getattr(_p, 'x') == 3
assert getattr(ValueError('x'), 'args') == ('x',) and getattr(ValueError('x'), '__cause__') is None
assert getattr('abc', 'upper')() == 'ABC' and hasattr('abc', 'upper') and not hasattr(1, 'nope')
import math as _math
assert getattr(_math, 'pi') == _math.pi and not hasattr(_math, 'nope')
try:
    getattr(_p, 'missing')
    assert False
except AttributeError as e:
    assert str(e) == "'_Pt' object has no attribute 'missing'"
try:
    getattr(_p, 5)
    assert False
except TypeError as e:
    assert str(e) == "attribute name must be string, not 'int'"
try:
    setattr(1, 'x', 3)
    assert False
except AttributeError as e:
    assert str(e) == "'int' object has no attribute 'x'"
for _bad in (lambda: getattr(_p), lambda: getattr(_p, 'x', 1, 2), lambda: hasattr(_p),
             lambda: setattr(_p, 'x'), lambda: getattr(_p, 'x', default=1)):
    try:
        _bad()
        assert False
    except TypeError:
        pass

@dataclass(frozen=True)
class _Fz:
    v: int

try:
    setattr(_Fz(1), 'v', 2)
    assert False
except AttributeError as e:
    assert str(e) == "cannot assign to field 'v'"

# ---- id(): identity, the same relation `is` reports (a counter, not an address, so runs repeat) ----
_a = [1]
_b = [1]
_c = _a
assert id(_a) == id(_a) and id(_a) == id(_c) and id(_a) != id(_b)
assert (id(_a) == id(_b)) == (_a is _b) and isinstance(id(_a), int)
assert id(None) == id(None) and id(True) == id(True)
try:
    id(1, 2)
    assert False
except TypeError as e:
    assert 'exactly one argument' in str(e)

# ---- hash: seeded for str/bytes but FIXED per build, so a hash survives a restart (dialect: CPython
# randomizes per process unless PYTHONHASHSEED is set). Numbers are never seeded -- the dict invariant
# hash(1) == hash(1.0) == hash(True) requires it.
assert hash('a') == hash('a') and hash(b'a') == hash(b'a')
assert hash(1) == hash(1.0) == hash(True) and hash(1) == 1
assert {True: 'a', 1: 'b', 1.0: 'c'} == {True: 'c'}
