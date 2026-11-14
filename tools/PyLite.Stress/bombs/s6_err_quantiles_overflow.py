# deadline: 3000
# allocmb: 256
import statistics
print(round(statistics.quantiles(range(100001), n=30000, method='inclusive')[-1], 3))
