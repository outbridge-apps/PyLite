# Adapted from CPython's test suite (Lib/test/test_iter.py: next() on an exhausted iterator).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: StopIteration

it = iter([])
next(it)
