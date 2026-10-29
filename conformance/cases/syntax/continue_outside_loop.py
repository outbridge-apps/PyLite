# Adapted from CPython's test suite (Lib/test/test_syntax.py: 'continue' not properly in loop).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: SyntaxError

def f():
    continue
