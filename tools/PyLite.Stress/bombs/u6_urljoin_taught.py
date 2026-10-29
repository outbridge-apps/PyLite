# deadline: 20000
# allocmb: 1024
import urllib.parse as up
for i in range(200000):
    up.uses_relative.append('s' + str(i))
print(up.urljoin('zzz://h/a', 'b'))
