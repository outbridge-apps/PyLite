# Adapted from CPython's test suite (Lib/test/test_syntax.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: SyntaxError

def f(a, a):
    pass
