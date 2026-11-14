# allocmb: 16
# deadline: 60000
# Review 16: .NET's prefix search also passes $, \z, \Z, a non-capturing group of only zero-width items and a group
# quantified {1}: the prefix and pages behind them are held as a leading a{1500} is (one leading \Z undid the hold).
import re
run = ''.join(chr(page << 8) for page in range(1, 216) if not 0xd8 <= page <= 0xdf)
keep = []
forms = [chr(92) + 'Za{1500}x%d', '$a{1500}x%d', '(?:^)a{1500}x%d', '(?:' + run + '){1}x%d', chr(92) + 'Z' + run + 'x%d']
for i in range(5000):
    p = re.compile(forms[i % len(forms)] % i)
    p.search('q')
    keep.append(p)
print(len(keep))
