
import xml.etree.ElementTree as ET
out = []
for n in ['us', '646', 'ANSI_X3.4-1968', 'ansi_x3_4_1968', 'cp367', 'ISO646-US', 'iso_646.irv_1991', 'latin', '8859', 'cp819',
          'iso8859', 'ISO_8859-1:1987', 'iso_8859_1_1987', 'utf', 'CP65001', 'utf8_ucs2', 'UnicodeLittleUnmarked',
          'unicodebigunmarked', 'csIBM866', 'iso_ir_144', 'iso-ir-6', 'utf 8', 'utf-8-', 'U8', 'l1', 'latin_1']:
    try:
        out.append((n, 'é'.encode(n, 'replace'), b'\xc3\xa9'.decode(n, 'replace')))
    except LookupError as e:
        out.append((n, 'LookupError'))
for n in ['us', 'latin', 'cp65001', 'utf']:
    out.append(ET.fromstring(('<?xml version="1.0" encoding="%s"?><a>x</a>' % n).encode('ascii')).text)
print(out)
