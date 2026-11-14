# deadline: 20000
# Review 17: .NET keeps an item quantified {1} as it is, and the prefix walk stopped at one: the stop stood in front
# of ^ behind (?:){1} and hid the start anchor (a search on 1.6 MB ran past its timeout, 28 ms before).
import re
text = ('x' * 80 + '\n') * 20000
out = []
for p in [r'(?:){1}^(?:.{80}\n){60}END', r'(?=x){1}^(?:x{80}\n){60}END', r'(?:){1}\A(?:[^\n]{80}\n){60}END']:
    out.append(re.search(p, text))
print(out)
