# CPython 3.7+ (and PyLite) escape only the regex metacharacters: re.escape('1+1=2') == '1\\+1=2';
# IronPython 3.4.2 escapes every non-alphanumeric character: '1\\+1\\=2'.
import re
result = [re.escape('1+1=2'), re.escape('a-b c'), re.escape('x_y')]
