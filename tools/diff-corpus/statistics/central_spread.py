import statistics as st
from decimal import Decimal
from fractions import Fraction

out = []
data = [2, 4, 4, 4, 5, 5, 7, 9]
out.append((st.mean(data), st.median(data), st.median_low(data), st.median_high(data), st.mode(data)))
out.append((st.pvariance(data), st.pstdev(data), st.variance(data), st.stdev(data)))
out.append(st.median([3, 1, 2]))
out.append(st.median_grouped([1, 2, 2, 3, 4, 4, 4, 4, 4, 5]))
out.append(st.median_grouped([52, 52, 53, 54], interval=2))
out.append(st.mean([1.5, 2.25, 3.125]))
out.append(st.mean([Fraction(1, 3), Fraction(2, 3), Fraction(1, 2)]))
out.append(st.mean([Decimal('1.10'), Decimal('2.20'), Decimal('3.30')]))
out.append(st.variance([Decimal('1.5'), Decimal('2.5'), Decimal('3.5')]))
out.append(st.mode(['red', 'blue', 'red']))
out.append(st.variance([1, 2, 3, 4], xbar=2.5))
out.append(st.pstdev([1.5, 2.5, 2.5, 2.75, 3.25, 4.75]))
result = out
