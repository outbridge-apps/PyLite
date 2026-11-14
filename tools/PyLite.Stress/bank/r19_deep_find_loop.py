# Review 13, row 24: find('.//leaf') in a 61-deep document, 100,000 times. Each walk paid 12 B a frame past its
# eighth, never given back (before the fix: 68 MB charged; 1.1.0 and now: 6).
import xml.etree.ElementTree as ET
root = ET.Element('r'); n = root
for d in range(60):
    n = ET.SubElement(n, 'c')
ET.SubElement(n, 'leaf')
k = 0
for i in range(100000):
    k += root.find('.//leaf') is not None
print(k)
