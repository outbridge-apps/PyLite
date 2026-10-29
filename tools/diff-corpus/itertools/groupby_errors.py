import itertools as it


def err(f):
    try:
        f()
        return 'ok'
    except Exception as e:
        return type(e).__name__


out = []
rows = [('fruit', 'apple'), ('fruit', 'pear'), ('veg', 'kale'), ('fruit', 'fig')]
out.append([(k, [v for _, v in g]) for k, g in it.groupby(rows, key=lambda r: r[0])])
out.append([(k, len(list(g))) for k, g in it.groupby(sorted(rows), key=lambda r: r[0])])
out.append([k for k, _ in it.groupby('aaabbbcca')])
out.append(err(lambda: list(it.islice(range(5), -1))))
out.append(err(lambda: list(it.permutations([1], -1))))
out.append(err(lambda: list(it.combinations([1], -1))))
out.append(err(lambda: it.repeat(1, 'x')))
out.append(err(lambda: list(it.chain(1))))
result = out
