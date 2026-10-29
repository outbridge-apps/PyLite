# CPython's C decimal (and PyLite) refuse an unknown rounding mode with TypeError; IronPython 3.4.2 runs
# the pure-Python _pydecimal, whose table lookup raises KeyError.
from decimal import Decimal
try:
    Decimal('1.234').quantize(Decimal('0.01'), rounding='nope')
    result = 'ok'
except Exception as e:
    result = type(e).__name__
