# allocmb: 40
# Review 14: what building a regex takes and the CLR collects after is not paid; 2,000 Unicode \b compile under 40 MB as
# 1.1.0 compiled them (the exact translation, its classes kept as shared sets, holds 4 MB).
import re
print(re.compile(r'\b' * 2000).search('a' + chr(0x301)) is not None)
