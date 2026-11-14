def kw(**k):
    return len(k)
out=[]
def t(name, f):
    try:
        r = f()
        out.append(name + "=" + repr(r)[:80])
    except Exception as e:
        out.append(name + "!" + type(e).__name__ + ":" + str(e)[:80])
class M:
    def keys(self):
        return ['a', 'b']
    def __getitem__(self, k):
        return k * 2
t('star_star_script_map', lambda: kw(**M()))
class MK:
    def keys(self):
        return [1]
    def __getitem__(self, k):
        return k
t('star_star_script_badkey', lambda: kw(**MK()))
class MR:
    def keys(self):
        raise KeyError('boom')
    def __getitem__(self, k):
        return k
t('star_star_script_raise', lambda: kw(**MR()))
class MG:
    def keys(self):
        return ['a']
    def __getitem__(self, k):
        raise IndexError(k)
t('star_star_getitem_raise', lambda: kw(**MG()))
t('star_star_nonmap', lambda: kw(**[1]))
t('star_nonit', lambda: pos(*1))
print(chr(10).join(out))
