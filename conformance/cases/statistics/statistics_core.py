# Adapted from CPython's test suite (Lib/test/test_statistics.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import statistics

# mean
assert statistics.mean([1, 2, 3, 4]) == 2.5
assert statistics.mean([1, 1, 1]) == 1
assert statistics.mean([-1, 1]) == 0

# median: odd picks the middle, even averages the two middles
assert statistics.median([1, 3, 5]) == 3
assert statistics.median([1, 3, 5, 7]) == 4.0
assert statistics.median([5, 1, 3]) == 3            # sorts internally
assert statistics.median_low([1, 3, 5, 7]) == 3
assert statistics.median_high([1, 3, 5, 7]) == 5

# mode
assert statistics.mode([1, 1, 2, 3]) == 1
assert statistics.mode('aabbbc') == 'b'

# population variance/stdev on the classic dataset
data = [2, 4, 4, 4, 5, 5, 7, 9]
assert statistics.pvariance(data) == 4.0
assert statistics.pstdev(data) == 2.0

# sample variance uses n-1
assert statistics.variance([1, 2, 3, 4, 5]) == 2.5
assert abs(statistics.stdev([1, 2, 3, 4, 5]) - 2.5 ** 0.5) < 1e-12

# errors on empty input
raised = False
try:
    statistics.mean([])
except statistics.StatisticsError:
    raised = True
assert raised

raised = False
try:
    statistics.median([])
except statistics.StatisticsError:
    raised = True
assert raised
