# deadline: 20000
# allocmb: 1024
import operator
f = operator.itemgetter(*range(200000))
print(len(f(list(range(200000)))))
