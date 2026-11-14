# repeat: 2
# deadline: 60000
import xml.etree.ElementTree as ET
import random, copy

SEED0 = 1000
ROUNDS = 40
TAGS = ['a', 'b', 'c']
out = []

def run_case(seed):
    random.seed(seed); rnd = random
    labels = {}
    allel = []
    cnt = [0]
    def new(tag=None):
        e = ET.Element(tag or rnd.choice(TAGS))
        if rnd.random() < 0.4:
            e.set('x', rnd.choice(['1', '2']))
        if rnd.random() < 0.3:
            e.text = rnd.choice(['t', 'u'])
        if rnd.random() < 0.2:
            e.tail = 't'
        labels[id(e)] = cnt[0]
        cnt[0] += 1
        allel.append(e)
        return e
    def lab(e):
        return labels.get(id(e), -1)
    root = new('r')
    for i in range(rnd.randint(3, 25)):
        p = rnd.choice(allel)
        p.append(new())
    def subtree_has(x, p):
        for q in x.iter():
            if q is p:
                return True
        return False
    log = []
    paths = ['a', 'a[1]', 'a[2]', 'a[last()]', 'a[last()-1]', '*[1]', '*[last()]', './/a[1]', './/b[last()]',
             'a/b[1]', '*/a[2]', 'a[@x]', 'a[@x="1"][1]', 'a[1][@x]', 'b[@x!="1"]', 'a[b]', 'a[b="t"]', 'a[.="t"]',
             './/a/..', '*/..', 'a/../b[1]', './/*[2]', './/a[@x][last()]', '.', '..', './/c[1]/..', 'a[2]/b',
             '*[b][1]', 'c[last()][@x="2"]', './/*[last()]', 'b[1][b]', '{}a[1]', '*/*[1]']
    for step in range(40):
        op = rnd.random()
        try:
            if op < 0.12:
                p = rnd.choice(allel); p.append(new())
            elif op < 0.18:
                p = rnd.choice(allel); p.insert(rnd.randint(0, len(p)), new())
            elif op < 0.24:
                p = rnd.choice(allel)
                if len(p):
                    p.remove(p[rnd.randrange(len(p))])
            elif op < 0.28:
                p = rnd.choice(allel)
                if len(p):
                    p[rnd.randrange(len(p))] = new()
            elif op < 0.31:
                p = rnd.choice(allel); x = rnd.choice(allel)
                if x is not root and not subtree_has(x, p):
                    p.append(x)
            elif op < 0.33:
                p = rnd.choice(allel); x = copy.copy(rnd.choice(allel))
                labels[id(x)] = cnt[0]; cnt[0] += 1; allel.append(x)
                if not subtree_has(x, p):
                    p.append(x)
            elif op < 0.35:
                p = rnd.choice(allel); x = copy.deepcopy(rnd.choice(allel))
                for q in x.iter():
                    if id(q) not in labels:
                        labels[id(q)] = cnt[0]; cnt[0] += 1; allel.append(q)
                p.append(x)
            elif op < 0.37:
                p = rnd.choice(allel)
                if len(p) > 1:
                    p[0:1] = [new(), new()]
            elif op < 0.38:
                p = rnd.choice(allel)
                if p is not root:
                    p.clear()
            elif op < 0.40:
                p = rnd.choice(allel)
                ET.SubElement(p, rnd.choice(TAGS), {'x': '1'} if rnd.random() < 0.5 else {})
            elif op < 0.42:
                p = rnd.choice(allel)
                p.extend([new(), new()])
            elif op < 0.44:
                p = rnd.choice(allel)
                if len(p) > 0:
                    x = p[0]; p.remove(x)
                    q = rnd.choice(allel)
                    if not subtree_has(x, q):
                        q.append(x)
            else:
                s = rnd.choice(allel) if rnd.random() < 0.3 else root
                path = rnd.choice(paths)
                kind = rnd.random()
                if kind < 0.4:
                    r = [lab(e) for e in s.findall(path)]
                elif kind < 0.6:
                    f = s.find(path); r = lab(f) if f is not None else None
                elif kind < 0.7:
                    r = s.findtext(path, 'D')
                else:
                    it = s.iterfind(path)
                    r = []
                    for e in it:
                        r.append(lab(e))
                        if rnd.random() < 0.3:
                            p = rnd.choice(allel); p.append(new())
                        if rnd.random() < 0.1:
                            break
                log.append((step, path, r))
        except Exception as ex:
            log.append((step, 'EXC', type(ex).__name__, str(ex)[:60]))
    return log

h = 0
for k in range(ROUNDS):
    lg = run_case(SEED0 + k)
    s = repr(lg)
    print(k, s)
