import re


def err(f):
    try:
        f()
        return 'ok'
    except Exception as e:
        return type(e).__name__


out = []
out.append(err(lambda: re.compile('(')))
out.append(err(lambda: re.compile('[a-')))
out.append(err(lambda: re.compile('*a')))
out.append(err(lambda: re.compile('a{2,1}')))
out.append(err(lambda: re.compile(r'(?P<1x>a)')))
out.append(err(lambda: re.search('(a)', 'a').group(2)))
out.append(err(lambda: re.search('(a)', 'a').group('nope')))
out.append(err(lambda: re.findall(5, 'a')))
out.append(err(lambda: re.match('a', None)))
out.append(issubclass(re.error, Exception))
result = out
