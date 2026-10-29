# Adapted from CPython's test suite (Lib/test/test_dict.py: mutation during iteration).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: RuntimeError

d = {'a': 1}
for k in d:
    d['b' + k] = 2
