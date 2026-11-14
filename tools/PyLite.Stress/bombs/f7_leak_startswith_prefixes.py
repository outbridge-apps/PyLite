# deadline: 3000
# allocmb: 256
s = 'a' * 8000000
t = (s[:-1] + 'b',) * 3000
x = s.startswith(t)
