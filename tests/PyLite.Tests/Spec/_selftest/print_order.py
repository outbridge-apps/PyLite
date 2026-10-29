# tags: selftest
# print: first
# print: second 2
for i, w in enumerate(['first', 'second']):
    if i:
        print(w, i + 1)
    else:
        print(w)
