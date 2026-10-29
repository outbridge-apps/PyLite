# Adapted from CPython's test suite (Lib/test/test_statistics.py: mean of empty data).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: StatisticsError

import statistics
statistics.mean([])
