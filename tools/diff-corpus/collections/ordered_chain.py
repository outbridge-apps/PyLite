from collections import OrderedDict, ChainMap

out = []
od = OrderedDict()
od['b'] = 1
od['a'] = 2
od['c'] = 3
od.move_to_end('b')
out.append(list(od.items()))
od.move_to_end('c', last=False)
out.append(list(od.keys()))
out.append(od.popitem())
out.append(od.popitem(last=False))
out.append(OrderedDict([(1, 1), (2, 2)]) == OrderedDict([(2, 2), (1, 1)]))
out.append(OrderedDict([(1, 1), (2, 2)]) == {2: 2, 1: 1})
defaults = {'color': 'red', 'size': 'M'}
user = {'size': 'L'}
cm = ChainMap(user, defaults)
out.append((cm['color'], cm['size'], len(cm), sorted(cm)))
child = cm.new_child({'color': 'blue'})
out.append((child['color'], child.parents['color']))
cm['extra'] = 1
out.append(sorted(user.items()))
out.append(len(cm.maps))
result = out
