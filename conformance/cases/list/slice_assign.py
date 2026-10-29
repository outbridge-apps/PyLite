# Adapted from CPython's test suite (Lib/test/test_list.py slice-assignment cases).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

a = [1, 2, 3, 4]
a[1:3] = [9]
assert a == [1, 9, 4]

a = [1, 2, 3]
a[0:0] = [0]                 # insert at the front
assert a == [0, 1, 2, 3]

a = [1, 2, 3]
a[1:2] = [8, 9]              # replacement may change length
assert a == [1, 8, 9, 3]

a = [1, 2, 3]
a[:] = [7]                   # replace the whole content in place
assert a == [7]

a = [1, 2, 3, 4]
del a[1:3]
assert a == [1, 4]

a = [1, 2, 3]
del a[0]
assert a == [2, 3]
