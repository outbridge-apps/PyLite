# deadline: 20000
# allocmb: 1024
import urllib.parse as up
print(len(up.quote('a b/' * 2000000)))
