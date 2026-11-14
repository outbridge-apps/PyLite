# CPython oracle corpus

Each `name.py` here is a script and `name.out` is what CPython 3.14 printed for it. `Spec/OracleCorpusTests`
runs every script on the engine and compares the output exactly: as a host runs it, under a hostile caller
culture, sixteen at a time on one engine, and in the .NET 4.5 compatibility mode of an ASP.NET host.

The scripts came out of a sandbox review and were checked line by line against CPython 3.14.2 (Pyodide). They keep out what the dialect documents as
different (`docs/deviations.md`): the `random` stream, the `repr` of functions and classes, the order of a set,
and zone history the Windows time zone database does not hold. The zone lines read the machine's database, so
a Windows update that changes a zone's rules changes them too.

To add a case: write the script so it prints its answers (catching the exceptions it expects), run it under
CPython 3.14 with `python name.py > name.out`, and check that nothing in it depends on a documented deviation.

The `p6_*` scripts came from the review's P6 (CPython 3.14's texts and corners): `p6_runtime_texts` holds the
arithmetic, `math`, `datetime`, `re` and `StopIteration` texts, `p6_modules` the module corners, and
`p6_unicode16` the Unicode 16 normalization and predicates. Over every code point the predicates and the
four normal forms are too slow to run as a script, so `Values/Unicode16OracleTests` compares SHA-1 digests of
them with the digests CPython 3.14.2 printed for the same walk.

`r4_required` and `r4_wanted` hold what the fourth sandbox review found.
`r5_blockers`, `r5_wanted` and `r5_after` hold the answers the fifth review fixed.
`r10_xml_fuzz` is review 10's ElementPath fuzz (80 random trees with shared, moved, replaced and deleted elements, copies and
growth between searches; 27 paths from three starts), the lazy attrib and `iterparse` cases: the fast paths without a
parent map must answer as CPython's map does.
The `r10b_*` scripts hold what was fixed after review 10 from the lists the reviews had left open: `r10b_collections`
(an `attrib` of a dict subclass through a copy, a path with an unbound prefix, the `gzip` header's mtime, sizes
past 32 bits in `batched`, `deque` and `Counter.elements`, `Counter` over str counts), `r10b_calls` (a `deque` changed
under its iterator, `**` over a `ChainMap`, `ChainMap`'s `|`, `Counter`'s corners), `r10b_stop` and `r10b_stop2` (a `StopIteration` out
of a callback, or out of an item's `__bool__` under `filter(None, xs)`, ends `map`, `filter` and the `itertools`
iterators), `r10b_xml_tags*` (a tag whose `__eq__` changes the
tree during a search; tags that are not a str in a step and under a position; `itertext` over a comment),
`r10b_xml_changes*` (searches between changes to the tree: the parent map that is
kept must answer as a new one), `r10b_xml_star` (`*[N]` and `*[last()]` over every shape of children, eager and lazy)
and `r10b_xml_fuzz` (random trees and paths, an in-script generator; `[@a!='v']` is in it). `r10b_review` holds what
the review of that batch found: a rename between the items of a lazy search, `*[N]` over a run of one tag and then
others, a copy of a `reversed(deque)` iterator, `lcm` and `gcd` past a zero, the texts of `Counter` arithmetic over str counts.

`syntax-errors.tsv` is the syntax-error oracle that `Spec/SyntaxErrorOracleTests` reads: one row per source
(`id`, the source as Python's `unicode_escape` writes it, then CPython's exception type, line, column and
message, or `OK`). Each row is compiled by the engine and must give the same four values; the rows the dialect answers
differently are listed in the test with the reason, and the test fails when one of them starts to match, so
the list cannot go stale.
