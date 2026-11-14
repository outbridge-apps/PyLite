# deadline: 3000
# allocmb: 256
from urllib.parse import urljoin
x = urljoin('http://h/a/b', '../' * 5000000)
