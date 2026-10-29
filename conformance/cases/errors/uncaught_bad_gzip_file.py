# Adapted from CPython's test suite (Lib/test/test_gzip.py: decompressing a non-gzip payload).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: BadGzipFile

import gzip
gzip.decompress(b'plain text, no gzip header')
