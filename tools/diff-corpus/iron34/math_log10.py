# CPython (C's log10, and PyLite) give exact powers of ten: math.log10(1000) == 3.0;
# IronPython 3.4.2 answers 2.9999999999999996.
import math
result = [math.log10(1000), math.log10(10 ** 6)]
