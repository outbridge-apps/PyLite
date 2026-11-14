# deadline: 3000
# allocmb: 256
n = 2**999999
s = slice(None, None, 3**315000)
while True:
    s.indices(n)
