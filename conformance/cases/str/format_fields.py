# Adapted from CPython's test suite (Lib/test/test_unicode.py test_format, Lib/test/test_str.py:
# the field_name grammar cases). Python Software Foundation, PSF License Version 2 -- see README.
# field_name ::= arg_name ("." attribute_name | "[" element_index "]")*
import dataclasses


@dataclasses.dataclass
class P:
    x: int
    name: str


p = P(3, 'bob')

# ---- "[" element_index "]": digits are an integer index, anything else a literal (unquoted) key ----
assert '{0[a]}'.format({'a': 1}) == '1'
assert '{0[k k]}'.format({'k k': 2}) == '2'
assert '{0[1]}'.format([10, 20]) == '20'
assert '{0[0]}'.format('xyz') == 'x'
assert '{0[5]}'.format({5: 'int', '5': 'str'}) == 'int'
assert '{0[a][0][b]}'.format({'a': [{'b': 7}]}) == '7'

# ---- "." attribute_name ----
assert '{0.x}'.format(p) == '3' and '{0.name}'.format(p) == 'bob'
assert '{q.x}'.format(q=p) == '3'
assert '{.x}'.format(p) == '3'                  # the arg_name may be empty (auto numbering)
assert '{0.x}{0.name}'.format(p) == '3bob'
assert '{k.x}'.format_map({'k': p}) == '3'

# ---- accessors compose with conversions and specs, incl. one level of nesting ----
assert '{0.x:>4}'.format(p) == '   3'
assert '{0.name!r}'.format(p) == "'bob'"
assert '{0[a]:{1}}'.format({'a': 3}, '>4') == '   3'

# ---- the accessors reach exactly what the dot operator and [] reach ----
try:
    '{0.nope}'.format(p)
    assert False
except AttributeError:
    pass
assert '{0.__class__.__name__}'.format(p) == 'P'  # what the dot operator reaches
try:
    '{0.__dict__}'.format(p)                    # dialect: no instance __dict__
    assert False
except AttributeError:
    pass
try:
    '{0[zz]}'.format({'a': 1})
    assert False
except KeyError:
    pass
try:
    '{0[a]}'.format(5)
    assert False
except TypeError:
    pass

# ---- malformed field names ----
try:
    '{0.}'.format(p)
    assert False
except ValueError as e:
    assert str(e) == 'Empty attribute in format string'
try:
    '{0[a}'.format({})
    assert False
except ValueError as e:
    assert str(e) == "Missing ']' in format string"
