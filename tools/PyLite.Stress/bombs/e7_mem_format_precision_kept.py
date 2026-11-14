# deadline: 3000
# allocmb: 256
s = 'a' * 8000000
L = []
for i in range(40):
    L.append(format(s, '.7999999'))
