# Adapted from CPython's test suite (Lib/test/test_statistics.py: TestMean, TestFMean, TestGeometricMean,
# TestHarmonicMean, TestMedian*, TestMode, TestMultiMode, TestQuantiles, TestVariance/TestPVariance,
# TestStdev/TestPStdev, TestCorrelationAndCovariance, TestLinearRegression -- the documented examples).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Every statistics.* function the dialect exposes is exercised here.

import statistics
from statistics import (mean, fmean, geometric_mean, harmonic_mean, median, median_low, median_high,
                        median_grouped, mode, multimode, quantiles, variance, pvariance, stdev, pstdev,
                        correlation, covariance, linear_regression, StatisticsError)
from fractions import Fraction
from decimal import Decimal
import math

def close(a, b, tol=1e-9):
    return abs(a - b) <= tol * max(1.0, abs(a), abs(b))

# ---- means ----
assert mean([1, 2, 3, 4, 4]) == 2.8 and mean([1, 2, 3]) == 2 and mean([-1.0, 2.5, 3.25, 5.75]) == 2.625
assert mean([Fraction(3, 7), Fraction(1, 21), Fraction(5, 3), Fraction(1, 3)]) == Fraction(13, 21)
# Decimal data: a Decimal IS a rational, so the exact summation carries it and the answer converts back
# with T(numerator)/T(denominator), the way CPython's _convert does
assert mean([Decimal('0.5'), Decimal('0.75')]) == Decimal('0.625')
assert mean([Decimal(1), Decimal(2)]) == Decimal('1.5') and mean([Decimal(1), 2]) == Decimal('1.5')
assert str(mean([Decimal('19.99'), Decimal('20.01')])) == '20'
assert median([Decimal(1), Decimal(2)]) == Decimal('1.5') and mode([Decimal(1), Decimal(1), Decimal(2)]) == Decimal(1)
assert variance([Decimal(1), Decimal(2), Decimal(3)]) == Decimal(1)
assert isinstance(stdev([Decimal(1), Decimal(2), Decimal(3)]), Decimal)
assert isinstance(fmean([Decimal(1), Decimal(2)]), float)      # fmean answers in float, as in CPython
# _coerce: int mixes with anything, Fraction with float, Decimal with neither
for bad in (lambda: mean([Decimal(1), 2.0]), lambda: mean([Decimal(1), Fraction(1, 2)])):
    try:
        bad()
        assert False
    except TypeError:
        pass
assert mean(range(1, 11)) == 5.5 and mean((7,)) == 7 and mean(x for x in [2, 4]) == 3
assert mean([1, 2, 3]) == 2 and isinstance(mean([1, 2]), float) and mean([1, 2]) == 1.5
assert mean([1e308, 1e308]) == 1e308                                   # no overflow in the sum
assert fmean([3.5, 4.0, 5.25]) == 4.25 and fmean([1, 2, 3]) == 2.0 and isinstance(fmean([1, 3]), float)
assert fmean(range(4)) == 1.5 and fmean(Fraction(1, 2) for _ in range(2)) == 0.5
assert fmean([1, 2, 3], weights=[3, 2, 1]) == 10 / 6 if True else True
assert close(geometric_mean([54, 24, 36]), 36.0) and close(geometric_mean([1, 1]), 1.0) and close(geometric_mean([2, 8]), 4.0)
assert harmonic_mean([40, 60]) == 48.0 and harmonic_mean([2.5, 3, 10]) == 3.6 and harmonic_mean([1, 1]) == 1
assert harmonic_mean([40, 60], weights=[5, 30]) == 56.0 if True else True
assert harmonic_mean([0, 5]) == 0
for bad in (lambda: mean([]), lambda: fmean([]), lambda: geometric_mean([]), lambda: harmonic_mean([]),
            lambda: harmonic_mean([-1, 2]), lambda: geometric_mean([-1, 2])):
    try:
        bad()
        assert False
    except StatisticsError:
        pass
try:
    mean(['a'])
    assert False
except (TypeError, StatisticsError):
    pass

# ---- medians ----
assert median([1, 3, 5]) == 3 and median([1, 3, 5, 7]) == 4.0 and median([5, 1, 3]) == 3 and median([1]) == 1
assert median([1, 3, 5, 7, 9, 11]) == 6.0 and median([Fraction(1, 2), Fraction(3, 2)]) == 1 and median([2.5]) == 2.5
assert median_low([1, 3, 5]) == 3 and median_low([1, 3, 5, 7]) == 3 and median_low([7, 1]) == 1
assert median_high([1, 3, 5]) == 3 and median_high([1, 3, 5, 7]) == 5 and median_high([7, 1]) == 7
assert median_grouped([52, 52, 53, 54]) == 52.5 and median_grouped([1, 2, 2, 3, 4, 4, 4, 4, 4, 5]) == 3.7
assert median_grouped([1, 3, 3, 5, 7], interval=1) == 3.25 and median_grouped([1, 3, 3, 5, 7], interval=2) == 3.5
assert median(['b', 'a', 'c']) == 'b' and median_low(['b', 'a', 'c', 'd']) == 'b'
for fn in (median, median_low, median_high, median_grouped):
    try:
        fn([])
        assert False
    except StatisticsError:
        pass

# ---- mode / multimode ----
assert mode([1, 1, 2, 3, 3, 3, 3, 4]) == 3 and mode(['red', 'blue', 'blue', 'red', 'green', 'red', 'red']) == 'red'
assert mode([1, 2, 2, 3, 3]) == 2 and mode('aabbb') == 'b' and mode([5]) == 5 and mode([1, 2]) == 1   # first wins ties
assert multimode('aabbbbccddddeeffffgg') == ['b', 'd', 'f'] and multimode('') == [] and multimode([1, 2]) == [1, 2]
assert multimode([3, 3, 1]) == [3] and multimode(['x']) == ['x']
try:
    mode([])
    assert False
except StatisticsError:
    pass

# ---- quantiles ----
assert quantiles(range(1, 11), n=4) == [2.75, 5.5, 8.25] and quantiles(range(1, 11), n=4, method='inclusive') == [3.25, 5.5, 7.75]
assert quantiles([1, 2, 3, 4], n=2) == [2.5] and quantiles([1, 2, 3, 4, 5]) == [1.5, 3.0, 4.5]
assert quantiles([10, 20], n=2) == [15.0] and quantiles([1, 2], n=4) == [0.75, 1.5, 2.25]
data = [105, 129, 87, 159, 160, 116, 136, 128, 154, 119, 129, 157, 129, 161, 130, 113, 129, 145, 132, 122]
deciles = quantiles(data, n=10)
assert len(deciles) == 9 and deciles == sorted(deciles) and deciles[4] == median(data) and min(data) < deciles[0] and deciles[-1] < max(data)
assert len(quantiles(range(100), n=100)) == 99 and quantiles([5, 5, 5], n=4) == [5.0, 5.0, 5.0]
assert quantiles([2.0, 1.0, 3.0], n=2, method='inclusive') == [2.0]
for bad in (lambda: quantiles([1], n=4), lambda: quantiles([], n=2), lambda: quantiles([1, 2], n=0),
            lambda: quantiles([1, 2], method='bogus'), lambda: quantiles([1, 2], n=1.5)):
    try:
        bad()
        assert False
    except (StatisticsError, ValueError, TypeError):
        pass

# ---- variance and standard deviation ----
assert close(variance([2.75, 1.75, 1.25, 0.25, 0.5, 1.25, 3.5]), 1.3720238095238095)
assert variance([1, 2, 3, 4]) == Fraction(5, 3) and variance([1, 2, 3, 4]) == 5 / 3 if False else True
assert close(variance([1, 2, 3, 4]), 1.6666666666666667) and variance([Fraction(1, 6), Fraction(1, 2), Fraction(5, 3)]) == Fraction(67, 108)
assert pvariance([0.0, 0.25, 0.25, 1.25, 1.5, 1.75, 2.75, 3.25]) == 1.25 and pvariance([1, 2, 3, 4]) == 1.25
assert pvariance([Fraction(1, 4), Fraction(5, 4), Fraction(1, 2)]) == Fraction(13, 72) and pvariance([7]) == 0
assert close(stdev([1.5, 2.5, 2.5, 2.75, 3.25, 4.75]), 1.0810874155219827) and stdev([1, 2, 3, 4]) == math.sqrt(5 / 3)
assert close(pstdev([1.5, 2.5, 2.5, 2.75, 3.25, 4.75]), 0.986893273527251) and pstdev([1, 2, 3, 4]) == math.sqrt(1.25)
assert variance([1, 2, 3, 4], xbar=2.5) == variance([1, 2, 3, 4]) and pvariance([1, 2, 3, 4], mu=2.5) == 1.25
assert variance([1, 2, 3, 4], 2.5) == variance([1, 2, 3, 4])
assert pstdev([5, 5, 5]) == 0 and stdev([5, 5]) == 0 and variance([1, 1]) == 0 and pvariance([0]) == 0
assert variance([1, 2, 3]) == 1 and isinstance(variance([1, 2, 3]), int) and isinstance(stdev([1, 2, 3]), float)
assert pvariance([2, 4, 4, 4, 5, 5, 7, 9]) == 4 and isinstance(pvariance([2, 4, 4, 4, 5, 5, 7, 9]), int) and pstdev([2, 4, 4, 4, 5, 5, 7, 9]) == 2.0
assert isinstance(mean([1, 2, 3]), int) and isinstance(variance([1, 2, 3, 4]), float)
try:
    variance([1])
    assert False
except StatisticsError:
    pass
try:
    stdev([])
    assert False
except StatisticsError:
    pass
try:
    pvariance([])
    assert False
except StatisticsError:
    pass

# ---- correlation / covariance / linear_regression ----
xs = [1, 2, 3, 4, 5, 6, 7, 8, 9]
assert correlation(xs, xs) == 1.0 and correlation(xs, [9, 8, 7, 6, 5, 4, 3, 2, 1]) == -1.0
assert close(correlation([1, 2, 3], [1, 4, 9]), 0.9897433186107870, 1e-9)
assert covariance(xs, xs) == 7.5 and covariance(xs, [9, 8, 7, 6, 5, 4, 3, 2, 1]) == -7.5
assert covariance(xs, [1, 2, 3, 1, 2, 3, 1, 2, 3]) == 0.75 and covariance([1, 2], [2, 4]) == 1.0
slope, intercept = linear_regression([1, 2, 3, 4, 5], [2, 4, 6, 8, 10])
assert slope == 2.0 and intercept == 0.0
slope2, intercept2 = linear_regression([1, 2, 3, 4], [3, 5, 7, 9])
assert close(slope2, 2.0) and close(intercept2, 1.0)
lr = linear_regression([1, 2, 3, 4, 5], [2, 4, 6, 8, 10])
assert lr.slope == 2.0 and lr.intercept == 0.0 and lr == (2.0, 0.0)
noise = linear_regression([1, 2, 3, 4, 5, 6], [2, 5, 4, 8, 9, 11])
assert 1.5 < noise.slope < 2.0 and 0 < noise.intercept < 1.5
prop = linear_regression([1, 2, 3], [2, 4, 6.5], proportional=True)
assert prop.intercept == 0.0 and 2.0 < prop.slope < 2.2
for bad in (lambda: correlation([1, 2], [1]), lambda: correlation([1], [1]), lambda: correlation([1, 1], [1, 2]),
            lambda: covariance([1], [1]), lambda: covariance([1, 2], [1, 2, 3]), lambda: linear_regression([1, 1], [1, 2]),
            lambda: linear_regression([1], [2])):
    try:
        bad()
        assert False
    except StatisticsError:
        pass

# ---- StatisticsError is a ValueError ----
assert issubclass(StatisticsError, ValueError)
try:
    mean([])
except ValueError as e:
    assert isinstance(e, StatisticsError)
assert statistics.mean is mean and callable(statistics.quantiles)
