import json


def err(f):
    try:
        f()
        return 'ok'
    except ValueError:
        return 'ValueError'
    except Exception as e:
        return type(e).__name__


out = []
for doc in ['', '{', '[1,]', '{"a" 1}', "{'a': 1}", '01', '[1] x', 'nul', '"\\x"']:
    out.append(err(lambda: json.loads(doc)))
out.append(err(lambda: json.dumps({1, 2})))
out.append(err(lambda: json.dumps(float('nan'), allow_nan=False)))
out.append(err(lambda: json.dumps({(1, 2): 3})))
cyc = []
cyc.append(cyc)
out.append(err(lambda: json.dumps(cyc)))
result = out
