# Adapted from CPython's test suite (Lib/test/test_with.py: FailureTestCase.testAssignmentToTupleContainingNoneError).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: SyntaxError

with m as (foo, None, bar):
    pass
