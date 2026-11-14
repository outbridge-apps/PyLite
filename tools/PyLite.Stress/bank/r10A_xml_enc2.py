import xml.etree.ElementTree as ET
for n in ["cp65001", "utf", "U8", "utf8-ucs2", "646", "us", "ansi_x3.4_1968", "csascii", "iso646-us", "cp367", "cp819", "8859", "iso-ir-100", "ibm819", "csisolatin1", "latin_1", "iso_8859_1", "ISO_8859-1", "windows-1251", "cp1251", "1252", "1251", "ms-ansi"]:
    src = b'<?xml version="1.0" encoding="' + n.encode("ascii") + b'"?><a>x</a>'
    try:
        print(repr(n), "->", repr(ET.fromstring(src).text))
    except Exception as e:
        print(repr(n), "->", type(e).__name__, e)
    try:
        print("  bytes.decode", repr(b"x".decode(n)))
    except Exception as e:
        print("  bytes.decode", type(e).__name__, e)
