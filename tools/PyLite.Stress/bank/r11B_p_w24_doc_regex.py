import re, time
# document processing: sub with callbacks, split, named groups, finditer over a 600 KB text
text = "".join("Invoice INV-%05d dated 2026-%02d-%02d for ACME Corp. Total: EUR %d.%02d (VAT 21%%). Contact: sales%d@acme.example.example.com\n"
               % (i, i % 12 + 1, i % 28 + 1, i * 3 % 9999, i % 100, i % 40) for i in range(6000))
t = time.perf_counter()
inv = re.compile(r"(?P<no>INV-\d{5}) dated (?P<y>\d{4})-(?P<m>\d\d)-(?P<d>\d\d)")
dates = [m.groupdict() for m in inv.finditer(text)]
t1 = time.perf_counter()
masked = re.sub(r"[\w.]+@[\w.]+", lambda m: m.group(0)[0] + "***", text)
t2 = time.perf_counter()
amounts = [float(a.replace(",", ".")) for a in re.findall(r"EUR (\d+\.\d\d)", text)]
words = re.split(r"\W+", text[:100000])
t3 = time.perf_counter()
swapped = re.sub(r"(\d{4})-(\d\d)-(\d\d)", r"\3.\2.\1", text)
caps = len(re.findall(r"\b[A-Z][a-z]+\b", text))
t4 = time.perf_counter()
ws = re.sub(r"\s+", " ", text)
cls = len(re.findall(r"[A-Za-z]+\.", text, re.I))
t5 = time.perf_counter()
print(len(dates), dates[1], masked[60:120], round(sum(amounts), 2), len(words), swapped[18:40], caps, len(ws), cls)
print([round((b - a) * 1000) for a, b in ((t, t1), (t1, t2), (t2, t3), (t3, t4), (t4, t5))])
