# CPython (and PyLite) round the binary value and print the shortest repr: round(2.675, 2) == 2.67
# (2.675 is stored as 2.67499999...), str(1e16) == '1e+16', str(1e-5) == '1e-05'.
# IronPython 3.4.2 answers 2.6800000000000002, '10000000000000000.0' and '1.0000000000000001e-05'.
result = (round(2.675, 2), str(1e16), str(1e-5))
