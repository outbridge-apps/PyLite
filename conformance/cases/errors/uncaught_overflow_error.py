# Adapted from CPython's test suite (Lib/test/test_float.py: int(float('inf'))).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: OverflowError

x = int(float('inf'))
