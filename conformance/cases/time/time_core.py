# Adapted from CPython's test suite (Lib/test/test_time.py deterministic parts only).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Only wall-clock-independent functions are asserted exactly.

import time

# the epoch in UTC
t0 = time.gmtime(0)
assert t0.tm_year == 1970
assert t0.tm_mon == 1
assert t0.tm_mday == 1
assert t0.tm_hour == 0
assert t0.tm_wday == 3                     # Thursday (Monday=0)
assert t0.tm_yday == 1

# a known moment: 2024-03-05 14:30:15 UTC
known = time.gmtime(1709649015)
assert known.tm_year == 2024
assert known.tm_mon == 3
assert known.tm_mday == 5
assert known.tm_hour == 14
assert known.tm_min == 30
assert known.tm_sec == 15

# struct_time is indexable like a tuple
assert t0[0] == 1970
assert t0[1] == 1

# strftime over an explicit struct
assert time.strftime('%Y-%m-%d', time.gmtime(0)) == '1970-01-01'
assert time.strftime('%H:%M:%S', time.gmtime(3661)) == '01:01:01'

# asctime canonical format
assert time.asctime(time.gmtime(0)) == 'Thu Jan  1 00:00:00 1970'

# monotonic clocks move forward (property only)
a = time.monotonic()
b = time.monotonic()
assert b >= a
assert time.perf_counter() >= 0
