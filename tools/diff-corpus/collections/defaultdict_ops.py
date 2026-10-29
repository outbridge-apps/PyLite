from collections import defaultdict

out = []
groups = defaultdict(list)
for word in ['apple', 'avocado', 'banana', 'blueberry', 'cherry']:
    groups[word[0]].append(word)
out.append(sorted(groups.items()))
counts = defaultdict(int)
for ch in 'mississippi':
    counts[ch] += 1
out.append(sorted(counts.items()))
nested = defaultdict(lambda: defaultdict(int))
nested['x']['y'] += 2
out.append(nested['x']['y'])
out.append('missing' in counts)
out.append(counts.get('missing'))
out.append(counts['missing'])
out.append('missing' in counts)
dd = defaultdict(None)
try:
    dd['k']
    out.append('no error')
except KeyError:
    out.append('KeyError')
out.append(groups.default_factory is list)
result = out
