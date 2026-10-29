import itertools as it

out = []
out.append(list(it.islice(range(100), 5, 20, 4)))
out.append(list(it.islice('abcdef', 3)))
out.append(list(it.takewhile(lambda x: x < 4, [1, 3, 5, 2])))
out.append(list(it.dropwhile(lambda x: x < 4, [1, 3, 5, 2])))
out.append(list(it.compress('abcdef', [1, 0, 1, 0, 1, 1])))
out.append(list(it.filterfalse(lambda x: x % 2, range(8))))
out.append(list(it.starmap(pow, [(2, 3), (3, 2)])))
out.append(list(it.zip_longest('ab', [1, 2, 3], fillvalue='-')))
out.append(list(it.chain([1], (2, 3), 'ab')))
out.append(list(it.chain.from_iterable([[1, 2], [3]])))
out.append(list(it.accumulate([1, 2, 3, 4])))
out.append(list(it.accumulate([3, 1, 4, 1, 5], max)))
out.append(list(it.islice(it.count(10, 2.5), 4)))
out.append(list(it.islice(it.cycle('xy'), 5)))
out.append(list(it.repeat('z', 3)))
a, b = it.tee([1, 2, 3])
out.append((list(a), list(b)))
result = out
