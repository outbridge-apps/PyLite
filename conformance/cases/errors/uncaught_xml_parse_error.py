# Adapted from CPython's test suite (Lib/test/test_xml_etree.py: malformed document).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: ParseError

import xml.etree.ElementTree as ET
ET.fromstring('<root><unclosed></root>')
