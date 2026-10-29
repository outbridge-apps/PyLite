# Outbridge.PyLite.UnicodeGen

Generates three tables from the Unicode Character Database, into the layer that owns each:
`src/PyLite/Runtime/Values/Text/PyUnicodeCase.Tables.g.cs` (case mapping),
`src/PyLite/Syntax/PyUnicodeNames.Tables.g.cs` (the character names behind `\N{NAME}` and
`unicodedata.name/lookup`) and `src/PyLite/Runtime/Values/Text/PyUnicodeProps.Tables.g.cs` (East Asian
Width, combining classes, bidi classes, mirroring and decompositions for `unicodedata`). When the
input directory also holds CLDR's `windowsZones.xml` (plus `bcp47/timezone.xml` for the aliases), it
writes `src/PyLite/Modules/Support/Time/WindowsZones.g.cs` too: the IANA -> Windows time zone id map behind
`zoneinfo.ZoneInfo` on .NET Framework, whose `TimeZoneInfo` knows Windows ids only.

```bash
curl -o ucd/windowsZones.xml https://raw.githubusercontent.com/unicode-org/cldr/main/common/supplemental/windowsZones.xml
curl -o ucd/timezone.xml https://raw.githubusercontent.com/unicode-org/cldr/main/common/bcp47/timezone.xml
```

Run by hand, only when bumping the Unicode version. The **output is what ships**: the build never
touches these files and never needs a network, exactly as CPython carries tables generated from the
same source rather than asking the OS.

## Why not just use the BCL

`char.ToUpperInvariant` and friends are a table too — an old, incomplete one we do not control:

- it disagrees with Unicode on **427 of the 2373** cased BMP characters. Whole alphabets are affected
  (Cherokee 92/92, Georgian 48/86, the Cyrillic supplement 12/48, Latin Ext-D/E 72/165), while basic
  Cyrillic (0/248), Armenian (0/76) and Vietnamese (2/248) are fine;
- it has **no full mappings at all** — the ones that change length, so `'ß'.upper()` stayed `'ß'`
  where CPython gives `'SS'`, and `casefold()` could not differ from `lower()`;
- it is not pinned to the version CPython uses, so the same script could answer differently on
  another machine.

## Running it

Fetch the seven files for the version you want (CPython 3.14 ships Unicode 16.0.0):

```bash
for f in UnicodeData SpecialCasing CaseFolding DerivedCoreProperties NameAliases NamedSequences EastAsianWidth; do
  curl -o ucd/$f.txt https://www.unicode.org/Public/16.0.0/ucd/$f.txt
done
```

Then generate, and commit the result:

```bash
dotnet run -c Release --project tools/Outbridge.PyLite.UnicodeGen -- ucd src/PyLite
```

Re-run the suite afterwards: `conformance/cases/str/case_mapping.py`, `str/named_escapes.py`,
`BuiltinsStrMethodsTests.UnicodeCaseMapping` and `LexerStringTests.NamedEscapes` pin the behaviour a
version bump could move.

## What it emits

| Table | Shape | Source |
|---|---|---|
| `Upper` / `Lower` / `Title` / `Fold` | 256-entry page index + the non-identity pages as one string, indexed directly | `UnicodeData.txt` fields 13-15, `CaseFolding.txt` status `C` |
| `Flag` | per character: Cased, Uppercase, Lowercase, titlecase-category, Case_Ignorable, has-full-mapping | `DerivedCoreProperties.txt` — the same properties CPython's `makeunicodedata.py` reads, so `islower`/`isupper`/`title` agree on Other_Lowercase characters too |
| `Full*` | the 104 characters whose mapping changes length | `SpecialCasing.txt` unconditional rows, `CaseFolding.txt` status `F` |
| `Astral*` | the 564 case pairs above the BMP | `UnicodeData.txt` |
| `XidStart` / `XidContinue` | the identifier properties: a flat BMP bitmap (16 bits per emitted char) plus sorted astral ranges, because these are dense across whole scripts | `DerivedCoreProperties.txt` — drives `str.isidentifier` per PEP 3131 |

Deliberately skipped, because CPython skips them too: `SpecialCasing.txt`'s locale-conditional rows
(`lt`, `tr`, `az`) and `CaseFolding.txt` status `T`. `Final_Sigma` is the one conditional rule that IS
applied — as a context check in `PyUnicodeCase.LowerSigma`, not as a table entry.

### The name table

`\N{NAME}` needs name -> character, so that is all the second file carries. It costs 395 KB because
most of the database is not stored at all:

| | |
|---|---|
| names in `UnicodeData.txt` | 40 013 |
| of them a prefix plus their own code point in hex | 5 876 -> 4 rules + their exact ranges |
| collapsed ranges (`CJK UNIFIED IDEOGRAPH-`, `TANGUT IDEOGRAPH-`) | rules; Hangul syllables are composed from jamo |
| stored rows: names + aliases + named sequences | 34 137 + 477 + 461 |

The stored rows are sorted and front-coded — a record keeps only the part of its name it does NOT
share with its predecessor, plus a 3-byte value — which takes 872 KB of text down to 395 KB, packed
two bytes per `char`. Every 32nd record starts a block with a full name, so `NameIndex` binary-searches
the blocks and only the block is walked. A rule that produces a name outside its range must fail, so
the ranges are exact rather than a span with holes.

