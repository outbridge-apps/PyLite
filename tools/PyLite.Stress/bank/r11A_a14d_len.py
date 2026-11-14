kws = ['kw%d_%d' % (999, i) for i in range(400)]
print(len(r'\b(?:' + '|'.join(kws) + r')\b'))
