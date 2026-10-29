# Adapted from CPython's test suite (Lib/test/test_json/test_fail.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: JSONDecodeError

import json
json.loads('{"open": [1, 2')
