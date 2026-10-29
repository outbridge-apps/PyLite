from collections import Counter

out = []
c = Counter('abracadabra')
out.append(sorted(c.items()))
out.append(c.most_common(1))
out.append(c['z'])
c.update('aaz')
c.subtract('b')
out.append(sorted(c.items()))
a = Counter(x=3, y=1)
b = Counter(x=1, y=2, z=1)
out.append(sorted((a + b).items()))
out.append(sorted((a - b).items()))
out.append(sorted((a & b).items()))
out.append(sorted((a | b).items()))
out.append(sorted(Counter(x=2, y=1).elements()))
out.append(sum(Counter([1, 1, 2]).values()))
out.append(sorted((+Counter({'p': 0, 'q': -1, 'r': 2})).items()))
out.append(sorted((-Counter({'p': 0, 'q': -1, 'r': 2})).items()))
d = Counter(a=1)
del d['a']
out.append(len(d))
result = out
