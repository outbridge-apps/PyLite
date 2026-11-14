# deadline: 60000
# Review 12, row 6: the 4,300-digit cap is read on the bit length, before the conversion.
def t(label, v):
    try:
        print(label, len(str(v)))
    except Exception as e:
        print(label, type(e).__name__, str(e)[:70])
t('10**4300-1', 10 ** 4300 - 1)
t('10**4300', 10 ** 4300)
t('-(10**4300-1)', -(10 ** 4300 - 1))
t('2**14284', 2 ** 14284)
t('2**14285-1', 2 ** 14285 - 1)
t('2**14285', 2 ** 14285)
t('2**14463', 2 ** 14463)
t('2**14464', 2 ** 14464)
t('2**20000', 2 ** 20000)
big = 2 ** 14463
n = 0
for i in range(3000):
    try:
        str(big)
    except ValueError:
        n += 1
print(n)
for f in (repr, lambda v: '%d' % v, lambda v: format(v, 'd'), lambda v: f'{v}', lambda v: '{:,}'.format(v), lambda v: print(v)):
    try:
        f(big)
        print('ok')
    except Exception as e:
        print(type(e).__name__)
print(len(hex(big)), len(bin(big)), len(oct(big)))
