import xml.etree.ElementTree as ET
import copy
out = []
def t(name, f):
    try:
        r = f()
        out.append(name + '=' + repr(r)[:100])
    except Exception as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:80])
def ctor():
    d = {'x': '1'}
    e = ET.Element('a', d)
    d['y'] = '2'
    return (e.attrib, e.attrib is d)
t('ctor_copies', ctor)
def ctor_empty():
    d = {}
    e = ET.Element('a', d)
    d['y'] = '2'
    return (e.attrib, e.attrib is d)
t('ctor_empty', ctor_empty)
def sub():
    d = {'x': '1'}
    r = ET.Element('r')
    e = ET.SubElement(r, 'a', d)
    d['y'] = '2'
    return (e.attrib, e.attrib is d)
t('sub_copies', sub)
def mk():
    d = {'x': '1'}
    e = ET.Element('r').makeelement('a', d)
    d['y'] = '2'
    return (e.attrib, e.attrib is d)
t('makeelement', mk)
def mk_empty():
    d = {}
    e = ET.Element('r').makeelement('a', d)
    d['y'] = '2'
    return (e.attrib, e.attrib is d)
t('makeelement_empty', mk_empty)
def setter():
    d = {'x': '1'}
    e = ET.Element('a')
    e.attrib = d
    d['y'] = '2'
    return (e.attrib, e.attrib is d)
t('setter_live', setter)
def setter_nondict():
    e = ET.Element('a')
    e.attrib = [('x', '1')]
    return e.attrib
t('setter_list', setter_nondict)
def setter_then_get():
    e = ET.Element('a')
    e.attrib = {}
    return (e.get('x'), e.keys(), e.items(), ET.tostring(e))
t('setter_empty', setter_then_get)
def copy_shares():
    e = ET.Element('a', {'x': '1'})
    c = copy.copy(e)
    c.set('y', '2')
    return (e.attrib, c.attrib, e.attrib is c.attrib)
t('copy_shares', copy_shares)
def copy_empty_then_set():
    e = ET.Element('a')
    e.attrib
    c = copy.copy(e)
    c.set('y', '2')
    return (e.attrib, c.attrib)
t('copy_empty_attrib', copy_empty_then_set)
def deepcopy_attr():
    e = ET.Element('a', {'x': '1'})
    c = copy.deepcopy(e)
    c.set('y', '2')
    return (e.attrib, c.attrib)
t('deepcopy', deepcopy_attr)
def clear_keeps():
    e = ET.Element('a', {'x': '1'})
    a = e.attrib
    e.clear()
    a['z'] = '9'
    return (e.attrib, a, e.attrib is a)
t('clear_detaches', clear_keeps)
def kw_and_dict():
    d = {'x': '1'}
    e = ET.Element('a', d, y='2')
    return (e.attrib, d)
t('kw_and_dict', kw_and_dict)
def nonstr_keys():
    e = ET.Element('a', {1: 2})
    return ET.tostring(e)
t('nonstr_attr', nonstr_keys)
def attrib_none():
    return ET.Element('a', None)
t('attrib_none', attrib_none)
def attrib_list():
    return ET.Element('a', [('x', '1')]).attrib
t('attrib_list', attrib_list)
def iterparse_attrib():
    import io
    evs = list(ET.iterparse(io.StringIO('<r a="1"><c b="2"/></r>'), events=('start',)))
    return [(e.tag, e.attrib) for ev, e in evs]
t('iterparse_attrib', iterparse_attrib)
def xmlid():
    r, ids = ET.XMLID('<r><c id="a"/><c id="b" x="1"/></r>')
    return sorted((k, v.attrib) for k, v in ids.items())
t('xmlid', xmlid)
def xpath_attr_after_set():
    r = ET.Element('r')
    c = ET.SubElement(r, 'c')
    before = r.findall('c[@x]')
    c.attrib['x'] = '1'
    after = r.findall('c[@x="1"]')
    return (len(before), len(after))
t('xpath_attr_after_set', xpath_attr_after_set)
def tostring_attr_order():
    e = ET.Element('a')
    e.attrib['b'] = '1'
    e.set('a', '2')
    return ET.tostring(e)
t('tostring_order', tostring_attr_order)
print(chr(10).join(out))
