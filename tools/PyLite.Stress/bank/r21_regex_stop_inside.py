# deadline: 20000
# Review 16: an earlier fix put the prefix stop between a lookahead and its quantifier, (?=a)?b{5000} turned (?=a)(?>)?b{5000}
# and stopped matching, and inside the group the loops merge through, which then backtracked past the timeout.
import re
print(re.match('(?=a)?b{5000}', 'b' * 5000).span(), re.fullmatch('(?!b)*(?:b{80}){60}', 'b' * 4800).span())
print(re.search('(?:a{10,}){500}b', 'a' * 6000), re.search('(?:(?:a{60,}){60,}){2}b', 'a' * 8000))
print(re.search('^(?:[ab]{10,}){500}c', 'ab' * 3000), re.search('(?:a{10,}){500}', 'a' * 6000).span())
