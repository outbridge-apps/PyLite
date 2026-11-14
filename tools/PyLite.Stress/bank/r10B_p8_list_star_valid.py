a = [1, 2]
print([*a], [*a, *a], (*a,), {*a}, [*a, 3], [x for x in a], {x for x in a}, [*a][0], [*(a)], (*a, *a), [ * a ])
print([a, [x for x in a]], {1, *a}, [(x, y) for x in a for y in a], [lambda: 1 for _ in a][0]())
print([a for a in a if a])
