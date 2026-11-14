# charged bytes per compile of typical patterns (distinct each time so the cache misses)
import re
kinds = {
    'w': r'(\w+)@(\w+)\.(\w+)',
    'b': r'\bcustomer\b',
    'wI': r'(?i)\b(?:invoice|order|status|credit|risk)\s+(\w+)',
    'date': r'(\d{4})-(\d\d)-(\d\d)[T ](\d\d):(\d\d)',
    'csvq': r'"((?:[^"]|"")*)"|([^,]*)',
    'kwI': r'(?i)(?:' + '|'.join(['kiss', 'sisk', 'iris', 'skis', 'silk', 'kilns', 'mist', 'sink']) + ')',
    'cls': r'[A-Za-z_][A-Za-z0-9_]*\s*=\s*[^;]+;',
    'uni': r'[\w.-]+@[\w-]+(?:\.[\w-]+)+',
}
for k, base in kinds.items():
    c0 = charged()
    n = 200
    for i in range(n):
        re.compile(base + '(?:#%d)?' % i)
    print(k, len(base), (charged() - c0) // n)
