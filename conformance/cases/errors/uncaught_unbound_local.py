# Adapted from CPython's test suite (Lib/test/test_scope.py: local referenced before assignment).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: UnboundLocalError

total = 1
def bump():
    total = total + 1
    return total
bump()
