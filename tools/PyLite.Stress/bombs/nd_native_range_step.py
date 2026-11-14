# deadline: 3000
# allocmb: 1024
r = range(0, 2 ** 999000, 3 ** 300000)
x = r[-1]   # the length, a big division (len() of it is an OverflowError, as in CPython)
