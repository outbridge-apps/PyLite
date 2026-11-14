import xml.etree.ElementTree as ET
def t(f):
    try:
        return f()
    except Exception as e:
        return str(e) if type(e) is TypeError else type(e).__name__
def f(**k):
    return k
class M:
    def __init__(self, keys, exc):
        self.k, self.e = keys, exc
    def keys(self):
        return self.k
    def __getitem__(self, k):
        raise self.e
print(t(lambda: f(**M(['a'], KeyError('a')))), '|', t(lambda: f(**M(['a'], KeyError(5)))), '|', t(lambda: f(**M(['a'], KeyError()))), '|', t(lambda: f(**M(5, None))))
p = ET.Element('p')
print(t(lambda: ET.Element('a', None, attrib={'x': '1'})), '|', t(lambda: ET.SubElement(p, 'a', None, attrib={})), '|', t(lambda: ET.SubElement(p, 'a', 5, attrib={})), '|', ET.Element('a', {}, attrib={'x': '1'}).attrib, '|', ET.SubElement(p, 'a', {'k': 'v'}, attrib={}).attrib)
class BadIt:
    def __iter__(self):
        raise TypeError('no iter')
class K:
    def __init__(self, k):
        self.k = k
    def keys(self):
        if self.k is None:
            raise KeyError('z')
        return self.k
    def __getitem__(self, k):
        return 1
print(t(lambda: f(**K(None))), '|', t(lambda: f(**K(BadIt()))))
