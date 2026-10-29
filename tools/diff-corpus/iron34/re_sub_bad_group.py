# CPython (and PyLite): a replacement that names a group the pattern lacks is re.error
# ("invalid group reference 2"); IronPython 3.4.2 substitutes without complaint.
import re
try:
    result = re.sub('(a)', r'\2', 'a')
except re.error:
    result = 'error'
