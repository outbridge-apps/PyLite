import itertools as it

out = []
out.append(list(it.permutations('abc', 2)))
out.append(len(list(it.permutations(range(5)))))
out.append(list(it.combinations([1, 2, 3, 4], 3)))
out.append(list(it.combinations_with_replacement('ab', 2)))
out.append(list(it.product('ab', [0, 1])))
out.append(list(it.product([0, 1], repeat=3))[-2:])
out.append(list(it.combinations([], 0)))
out.append(list(it.permutations([1, 2], 3)))
result = out
