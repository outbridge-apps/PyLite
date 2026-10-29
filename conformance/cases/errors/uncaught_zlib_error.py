# Adapted from CPython's test suite (Lib/test/test_zlib.py: decompressing garbage).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: error

import zlib
zlib.decompress(b'not a zlib stream')
