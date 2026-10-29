# CPython (and PyLite): Match.lastgroup is the NAME of the last matched group, None when it has none;
# IronPython 3.4.2 answers the group number as a string ('1').
import re
result = (re.search(r'(a)(b)?', 'a').lastgroup, re.search(r'(?P<x>a)', 'a').lastgroup)
