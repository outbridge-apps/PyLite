import xml.etree.ElementTree as ET
names = ["utf-8", "UTF-8", "utf_8", "utf8", "", " utf-8", "utf-8 ", "1utf", "utf-32", "UTF-32LE", "utf32", "iso-8859-1",
         "ISO-8859-2", "iso8859_15", "iso-8859-12", "iso-8859-17", "latin2", "latin-2", "L2", "l11", "cp437", "CP850",
         "windows-1250", "ibm037", "037", "mac-roman", "MacRoman", "koi8-u", "koi8-r", "ascii", "US-ASCII", "cp1252",
         "shift_jis", "x-unknown", "utf-16", "utf-8-sig", "ebcdic-cp-us", "tis-620", "hebrew", "iso-8859-", "iso8859",
         "latin", "l", "cp", "windows-", "ibm", "cp-1252", "a.b_c-d", "utf-7", "UTF-7", "idna", "punycode", "rot13",
         "hex", "base64", "unicode-escape", "raw-unicode-escape", "charmap", "mbcs", "oem", "cp65001", "utf-8-variants"]
for n in names:
    for body in (b"<a>x</a>", "<a>é</a>".encode("utf-8"), b"<a>\xb1</a>"):
        src = b'<?xml version="1.0" encoding="' + n.encode("ascii") + b'"?>' + body
        try:
            r = ET.fromstring(src)
            print(repr(n), body, "->", repr(r.text))
        except Exception as e:
            print(repr(n), body, "->", type(e).__name__, e)
