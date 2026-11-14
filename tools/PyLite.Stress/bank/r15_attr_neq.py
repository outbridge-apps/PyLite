import xml.etree.ElementTree as ET
r = ET.fromstring('<r><b x="2"/><b/><b x="1"/><b x="1" y="3"/><b y="1"/></r>')
def n(p):
    try:
        return [list(r).index(e) for e in r.findall(p)]
    except BaseException as e:
        return type(e).__name__ + ':' + str(e)
print(repr([n('b[@x!="1"]'), n("b[@x!='1']"), n('b[@x="1"]'), n("b[@x != '1']"), n('b[@x!="2"]'), n('b[@y!="1"]'), n('b[x!="1"]'), n('b[.!="1"]'), n('*[@x!="1"]')]))
