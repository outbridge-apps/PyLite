# CPython 3.12+ (and PyLite): sum() of floats is compensated (Neumaier), so sum([0.1] * 10) == 1.0;
# IronPython 3.4.2 adds plainly, as CPython did before 3.12: 0.9999999999999999.
result = (sum([0.1] * 10), sum([3.3, 1.1, 2.2]), sum([1e100, 1.0, -1e100, 1.0]))
