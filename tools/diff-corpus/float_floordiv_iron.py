# IronPython 3.4 computes float // as floor(x / y): 10.0 and -0.0 here, while its own divmod() and
# CPython 3.x (and PyLite) give 9.0 and -1.0 (_float_div_mod). Listed in known-differences.txt.
result = (1 // 0.1, -1.0 // float('inf'), divmod(1, 0.1)[0], divmod(-1.0, float('inf'))[0])
