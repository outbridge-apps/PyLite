import operator
g = operator.attrgetter("real" + ".real" * 100000)
print(g(1))
h = operator.itemgetter(*([0] * 100000))
print(len(h([1])))
