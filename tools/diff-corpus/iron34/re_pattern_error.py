# A bad pattern raises re.PatternError since CPython 3.13 (and in PyLite); re.error is the same class.
# IronPython 3.4.2's class is named error.
import re
try:
    re.compile('(')
    result = 'ok'
except re.error as e:
    result = type(e).__name__
