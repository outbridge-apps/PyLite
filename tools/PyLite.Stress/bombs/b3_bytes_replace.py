# deadline: 20000
# allocmb: 1024
b = b'abc ' * 2000000
print(len(b.replace(b'a', b'xy')))
