# deadline: 20000
# allocmb: 1024
import html
print(len(html.unescape('&amp;&lt;&#1046;' * 500000)))
