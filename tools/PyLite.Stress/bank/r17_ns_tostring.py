# deadline: 60000
# Review 12, row 19: an ordinary namespaced tree serialized five times; names are paid by the character.
import xml.etree.ElementTree as ET
root = ET.Element("{urn:x}feed")
for i in range(6000):
    e = ET.SubElement(root, "{urn:x}entry", id=str(i))
    e.set("{urn:m}t", "ab"[i % 2])
    ET.SubElement(e, "{urn:x}title").text = "T & %d" % i
    ET.SubElement(e, "{urn:m}v").text = str(i * 3)
print("built")
for k in range(5):
    out = ET.tostring(root, encoding="unicode")
print(len(out))
