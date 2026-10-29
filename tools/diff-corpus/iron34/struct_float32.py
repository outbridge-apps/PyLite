# CPython (and PyLite): a float packed as 32 bits comes back as that float32 widened to a double,
# 0.10000000149011612 for 0.1; IronPython 3.4.2 answers 0.1.
import struct
result = struct.unpack('<f', struct.pack('<f', 0.1))[0]
