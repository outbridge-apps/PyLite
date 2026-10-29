# Adapted from CPython's test suite (Lib/test/test_re.py: invalid pattern).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: error

import re
re.compile('(unclosed')
