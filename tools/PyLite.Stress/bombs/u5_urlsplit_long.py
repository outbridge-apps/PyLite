# deadline: 20000
# allocmb: 1024
import urllib.parse as up
print(len(up.urlsplit('http://h/' + 'p/' * 2000000).path))
