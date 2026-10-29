# float //, % and divmod agree with each other (CPython's _float_div_mod), and float() keeps a sign on zero.
pairs = [(1, 0.1), (-6.0, 3), (6.0, -3), (-7.5, 2), (7.5, -2), (0.0, -3), (1e300, 1e-300), (-1.0, float('inf'))]
floordiv = [x // y for x, y in pairs[1:7]]   # 1 // 0.1 and -1.0 // inf: see float_floordiv_iron.py
result = [(x % y, divmod(x, y)) for x, y in pairs] + floordiv + [float('-0.0'), -0.0 % 5, 5 % -0.5]
