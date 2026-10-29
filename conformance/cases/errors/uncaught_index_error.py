# Adapted from CPython's test suite (error-type surfacing; Lib/test/test_exceptions.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: IndexError

a = [1, 2, 3]
x = a[10]
