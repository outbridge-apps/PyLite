# CPython 3.7+ (and PyLite): an empty match right after a non-empty one is replaced and split on
# (re.sub('a*', '-', 'baac') == '-b--c-', re.split('x*', 'axbc') == ['', 'a', '', 'b', 'c', ''];
# IronPython 3.4.2 keeps the 3.4 rules: '-b-c-' and ['a', 'bc'].
import re
result = (re.sub(r'a*', '-', 'baac'), re.split(r'x*', 'axbc'))
