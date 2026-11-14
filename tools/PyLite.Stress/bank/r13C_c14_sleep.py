# deadline: 3000
import time
out = []
def t(name, f):
    try:
        t0 = time.perf_counter()
        r = f()
        out.append(name + '=' + repr(r)[:60] + ' %.2fs' % (time.perf_counter() - t0))
    except Exception as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:70])
t('neg', lambda: time.sleep(-1))
t('nan', lambda: time.sleep(float('nan')))
t('neg0', lambda: time.sleep(-0.0))
t('zero', lambda: time.sleep(0))
t('tiny', lambda: time.sleep(1e-300))
t('sub_ms_many', lambda: [time.sleep(0.0001) for i in range(200)] and None)
t('bool', lambda: time.sleep(True))
t('str', lambda: time.sleep('1'))
t('none', lambda: time.sleep(None))
t('big_int', lambda: time.sleep(10**400))
t('2_63', lambda: time.sleep(2**63))
t('inf', lambda: time.sleep(float('inf')))
print(chr(10).join(out))
