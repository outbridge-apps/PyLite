# Review 12, row 18: the empty path, as CPython 3.14 answers it.
import xml.etree.ElementTree as ET
root = ET.fromstring('<r><a/></r>')
def t(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, type(e).__name__, e)
t('find ns', lambda: root.find('', {}))
t('find ns2', lambda: root.find('', {'p': 'urn:x'}))
t('findall ns', lambda: root.findall('', {'p': 'urn:x'}))
t('findtext ns', lambda: root.findtext('', 'dflt', {'p': 'urn:x'}))
t('findtext dflt', lambda: root.findtext('', 'dflt'))
t('iterfind ns', lambda: root.iterfind('', {'p': 'urn:x'}))
t('iterfind', lambda: root.iterfind(''))
t('tree find', lambda: ET.ElementTree(root).find(''))
t('tree findall', lambda: ET.ElementTree(root).findall(''))
t('tree iterfind', lambda: ET.ElementTree(root).iterfind(''))
t('tree findtext', lambda: ET.ElementTree(root).findtext(''))
e = ET.SubElement(root, '')
e.text = 'T'
t('empty tag find', lambda: root.find('') is e)
t('empty tag findall', lambda: len(root.findall('')))
t('empty tag findtext', lambda: root.findtext(''))
t('empty tag iterfind', lambda: root.iterfind(''))
t('space', lambda: root.find(' '))
t('slash', lambda: root.find('/'))
