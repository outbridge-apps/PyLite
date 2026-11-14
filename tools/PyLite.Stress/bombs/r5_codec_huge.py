# deadline: 2000
n = 2 ** 999000
try:
    print(len(str(UnicodeDecodeError('utf-8', b'', n, n + 1, 'x'))))
except OverflowError as e:
    print(e)
