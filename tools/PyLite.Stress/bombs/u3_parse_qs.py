# deadline: 20000
# allocmb: 1024
import urllib.parse as up
print(len(up.parse_qs('a=1&' * 1000000)))
