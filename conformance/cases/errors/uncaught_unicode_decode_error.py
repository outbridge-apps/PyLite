# Adapted from CPython's test suite (error-type surfacing; Lib/test/test_codecs.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: UnicodeDecodeError

x = b'\xff'.decode('utf-8')
