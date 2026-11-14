# deadline: 20000
it = iter([1, 2, 3])
for i in range(85000):
    it = map(abs, it)
print(next(it))
