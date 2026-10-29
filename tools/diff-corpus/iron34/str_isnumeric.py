# CPython (and PyLite) read the Unicode numeric property: '½'.isnumeric() and '五'.isnumeric() are True;
# IronPython 3.4.2 answers False for them.
result = ['½'.isnumeric(), '五'.isnumeric()]
