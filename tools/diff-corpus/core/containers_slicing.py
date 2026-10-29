out = []
L = list(range(10))
out.append((L[::-1], L[-3:], L[:-7:-2], L[8:2:-3], L[100:], L[-100:2]))
M = L[:]
M[2:5] = ['a']
out.append(M)
M[::3] = [None] * len(M[::3])
out.append(M)
del M[::2]
out.append(M)
s = slice(1, 8, 3)
out.append((L[s], s.indices(5)))
d = {'b': 1, 'a': 2}
d.update(c=3)
d.setdefault('a', 9)
out.append(sorted(d.items()))
out.append((d.pop('zz', 'dflt'), sorted(dict.fromkeys('abc', 0).items())))
out.append(sorted({k: v for k, v in zip('xyz', range(3)) if v}.items()))
st = {3, 1, 2}
out.append((sorted(st | {5}), sorted(st & {1, 9}), sorted(st - {1}), sorted(st ^ {1, 7}), {1, 2} <= st))
out.append(sorted(frozenset('hello')))
out.append([x * y for x in range(3) for y in range(x)])
out.append([[i * j for j in range(3)] for i in range(3)])
nums = [5, 3, 8, 1]
nums.sort(reverse=True)
out.append(nums)
nums.insert(1, 7)
nums.extend((0,))
nums.remove(8)
out.append(nums)
out.append(([1, 2] * 2, [[0]] * 2, (1,) + (2,), 'ab' in 'cab', 3 in range(5), list('hi')))
words = 'b a c b a b'.split()
out.append(sorted(set(words), key=words.index))
pairs = [(1, 'b'), (0, 'z'), (1, 'a'), (0, 'y')]
out.append(sorted(pairs, key=lambda p: p[0]))
result = out
