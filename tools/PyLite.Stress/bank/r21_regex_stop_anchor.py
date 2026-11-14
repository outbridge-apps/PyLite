# deadline: 20000
# Review 16: the stop that ends .NET's prefix search stands where that search meets the loop, after the anchors: in
# front of ^ it hid the start anchor, and an anchored fixed-width search ran from every position past its timeout.
import re
text = ('x' * 80 + '\n') * 20000
out = []
for p in [r'^(?:.{80}\n){60}END', r'\A(?:[^\n]{80}\n){60}END', r'^HDR(?:.{80}\n){60}']:
    out.append(re.search(p, text))
print(out)
