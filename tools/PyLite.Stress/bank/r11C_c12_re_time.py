# deadline: 30000
import re, time
def t(name, f):
    t0 = time.perf_counter()
    try:
        r = f()
        print(name + '=' + repr(r)[:60], round(time.perf_counter() - t0, 3))
    except Exception as e:
        print(name + '!' + type(e).__name__ + ':' + str(e)[:80], round(time.perf_counter() - t0, 3))
log = ''.join('2026-11-10 12:%02d:%02d INFO user%d did thing id=%d\n' % (i % 60, i % 60, i, i * 7) for i in range(100000))
t('findall_ids', lambda: len(re.findall(r'id=(\d+)', log)))
#t('findall_w', lambda: len(re.findall(r'\w+', log)))
#t('sub_digits', lambda: len(re.sub(r'\d', '#', log)))
t('split_lines', lambda: len(re.split(r'\n', log)))
t('sub_cb', lambda: len(re.sub(r'user(\d+)', lambda m: m.group(1)[::-1], log)))
t('finditer', lambda: sum(1 for _ in re.finditer(r'(?m)^(\S+) (\S+) (\w+)', log)))
t('search_end', lambda: re.search(r'zzz', log * 2))
t('evil', lambda: re.match(r'(a+)+b', 'a' * 32))
t('evil_sub', lambda: re.sub(r'(a|aa)+c', '', 'a' * 40))
t('evil_findall', lambda: re.findall(r'(\w+\s?)+$', 'word ' * 30 + '!'))
def cb_slow(m):
    x = 0
    for i in range(200000):
        x += i
    return 'x'
t('sub_slow_cb', lambda: len(re.sub('a', cb_slow, 'a' * 10)))
t('evil_fullmatch', lambda: re.fullmatch(r'(x+x+)+y', 'x' * 30))
t('evil_split', lambda: re.split(r'(a*)*b', 'a' * 30))
t('evil_ignorecase', lambda: re.match(r'(?i)(k+)+z', 'K' * 30))
