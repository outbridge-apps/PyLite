# CPython 3.7+ (and PyLite) break a hyphenated word only after two letters (or letter-hyphen-letter):
# textwrap.wrap('a-b-c-d-e', 4) == ['a-b-', 'c-', 'd-e']; IronPython 3.4.2 keeps the older rule and
# answers ['a-b-', 'c-d-', 'e'].
import textwrap
result = textwrap.wrap('a-b-c-d-e', 4)
