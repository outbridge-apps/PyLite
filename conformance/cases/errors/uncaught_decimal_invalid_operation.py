# Adapted from CPython's test suite (Lib/test/test_decimal.py: an invalid literal).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: InvalidOperation

from decimal import Decimal
Decimal('not a number')
