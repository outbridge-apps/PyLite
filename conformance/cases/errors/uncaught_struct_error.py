# Adapted from CPython's test suite (Lib/test/test_struct.py: value out of range).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: error

import struct
struct.pack('<h', 70000)
