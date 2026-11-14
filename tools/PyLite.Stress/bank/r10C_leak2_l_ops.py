# repeat: 2
import functools, operator
t = 0
for k in range(2000):
    g = operator.itemgetter(k % 3)
    a = operator.attrgetter("real")
    m = operator.methodcaller("bit_length")
    t += g((1, 2, 3)) + a(k) + m(k)
    f = functools.partial(max, k)
    t += f(1)
print(t)
