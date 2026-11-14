# math.factorial of a float is a TypeError since CPython 3.10 (and in PyLite): 'float' object cannot be
# interpreted as an integer. IronPython 3.4.2 raises ValueError, as 3.4 did for a non-integral float.
import math
try:
    math.factorial(2.5)
    result = 'ok'
except Exception as e:
    result = type(e).__name__
