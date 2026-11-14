import xml.etree.ElementTree as ET, copy, collections
class D:
    pass
for mk in (collections.OrderedDict, collections.defaultdict):
    try:
        e = ET.Element('a')
        d = mk()
        e.attrib = d
        c = copy.copy(e)
        print(mk.__name__, c.attrib is d, type(c.attrib).__name__, type(copy.deepcopy(e).attrib).__name__)
    except Exception as ex:
        print(mk.__name__, 'EXC', type(ex).__name__, ex)
