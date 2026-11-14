# Fixed defects

Every defect fixed since version 1.0.0, with the test that keeps it fixed. Test classes are under
`tests/PyLite.Tests/`; conformance cases under `conformance/cases/`. A defect found by the differential
corpus, the adversarial probes or a review gets its row here in the commit that fixes it.

## In 1.2.0

### Found by review 6 (regressions of review 5's fixes and older defects)

| defect | what happened | held by |
|---|---|---|
| `root.find('{' * 150000)` | each `{` searched the rest of the path: 5.8 s in one native call, the runner leaked | `Runtime/Review6FixTests` `UnclosedBracesInAPathAreLinear` |
| `ET.tostring` of a long `{uri}` tag, `ET.indent` over long blank tails | per-element work uncharged; the runner leaked | `ALongNamespaceTagIsChargedPerElement`, `AnUnclosedNamespaceTagIsChargedPerElement`, `ABlankTailIsChargedByIndent` |
| `hashlib` with a million one-byte updates and `copy()` | 1.8 GB held for 256 MB charged | `GatheredUpdatesKeepTheDigestAndTheCopies`, `AHashTailIsChargedForItsBuffer` |
| `Decimal(19) ** -1` | rounded twice: a last digit `1 / Decimal(19)` does not have | `Review6FixTests` `AnswersAsCPython` |
| encode errors | positions in code points while strings index by UTF-16 units: the message named half a pair | `AnAstralCharacterIsOneCharacterInTheCodecMessage`, `TheRunOfAnEncodeErrorSlicesOutOfTheObject` |
| a caught encode or decode error | charged the scan a step per character and a copy of the whole input | `AStrictEncodeErrorChargesItsScanLinearly`, `ADecodeErrorSharesItsBytes` |
| `[\ud800-\udfff]` in a regex, `strptime` of year 0000, `calendar.weekheader(-1)`, `ZipFile.NameToInfo` after a rename, a dataclass `==` with an unset field, an XPath predicate whose `__bool__` changes the children | engine faults | `AnswersAsCPython`, `OldHighRowsAnswerAsCPython`, `AScriptBoolThatChangesTheChildrenDoesNotFaultAPredicate` |
| `sum([], True)`, a hash of an `lru_cache` function, a comprehension over an `IntEnum`, `%G %V` with a week the year lacks | wrong answers | `AnswersAsCPython`, `OldHighRowsAnswerAsCPython`, `AnEnumInTheRunKeepsFusionForPlainInts` |
| a second limit in a `finally` after an abort | replaced the first abort's cause | `ASecondLimitInFinallyKeepsTheFirstCause` |
| `f'{n:,}'`, `'%d' % n` of an int past about 750,000 bits | the charge alone passed MaxSteps before the 4300-digit `ValueError` | `Runtime/Review6Stage2Tests` `AHugeIntFormatsToTheValueError` |
| `repr(Counter(...))` of a million keys, a million `super().__init__()` calls | a tuple and an int per key, a super object and a bound method per call: past the default MaxAllocBytes that 1.1.0 fitted | `ACounterReprCostsAboutWhatADictReprDoes`, `ASuperCallCostsWhatAClassCallDoes` |
| `b'abc' % b'x'` | bytes taken as a mapping: no `TypeError` | `Review6Stage2Tests` `AnswersAsCPython` |
| `uuid1().node` | one per process: a run could tell another had run there | `TheUuid1NodeIsOwnedByTheRun` |
| `ScriptRunner.GetErrorLimit()` | null for a refusal, so a host could not tell it from a failing host function | `ScriptRunnerNamesARefusal` |
| an unknown keyword to `deque`, `Decimal`, `Element`, `OrderedDict`, `ChainMap` methods; a parameter by position and by name; an extra positional; a keyword to an exception class | dropped silently, the body ran | `ADroppedArgumentIsATypeError`, `TheKeywordsCPythonTakesStillWork` |
| `traceback.format_exception(e, e, None, None)`, `print_exc(file=f)`, `UUID(None, b)`, `quote_plus(string=)`, `ET.ElementTree.write(file_or_filename=)` | a positional `chain` read at the `limit` position, `file=` ignored, later `UUID` positions dropped, keyword forms refused | `KeywordsTheSignaturesNameAreRead` |
| `repeat(times=2)`, `move_to_end(last=False)` | a required argument left out became `None` | `ARequiredArgumentLeftOutIsMissing` |
| `copy` of a generator, an itertools object, a hash object, a `csv` reader or writer, a sequence iterator, a `decimal.Context` | the same object back: a copy that moved with the original | `Runtime/Review6Part4Tests` `CopyAsCPythonDoes` |
| `{}.fromkeys`, `(5).from_bytes`, `date(...).min` through an instance | `AttributeError` | `AClassMethodIsReachableThroughAnInstance` |
| `repr` of a deque or ChainMap that contains itself; the markers of a tuple, OrderedDict, defaultdict, namedtuple | `RecursionError`; `{...}` everywhere | `SelfContainingContainersPrintCPythonsMarkers` |
| `len(range(10**30))`, `date(10**5000, 1, 1)`, `struct.unpack_from` past ssize_t, `isinstance(1, (int, 'a'))`, `format(nan, '010')`, `'a'.encode(errors=1)`, `replace(old=)`, UTF-16 decode reasons, `gzip` zero padding, `json.loads('1\x00')`, `struct` `'e'` | answered, refused or worded unlike CPython | `RangesAndStructAsCPythonDo`, `TextsAndArgumentsAsCPythonHasThem`, `CodecsGzipAndJsonAsCPythonDo`, `DateFieldsOverflowAsCPythonDoes` |
| `IOError` and the other builtin exception names, `ImportError.name`, `re.error.pos`, `e.args = ...`, `ET.ParseError` texts and `code`, `TypeVar(zzz=)`, `functools.cache(f, zzz=)`, `dir(csv.writer())`, `json.loads(s=)`, `calendar` keywords, `format_exception(t, value=None, tb=None)` | absent, refused or worded unlike CPython | `BuiltinNamesAndExceptionAttributes`, `XmlParseErrorsUseExpatsWordsAndCodes`, `KeywordsCPythonTakes` |
| `int(s, 16)` of 238,000 digits; `big & small`; a 2-argument method called 1M times | quadratic (3 s); O(len(big)); a bound method per call past the default MaxAllocBytes | `APowerOfTwoBaseParsesLongInputInLinearTime`, `BigAndSmallIntersectsInTheLeftOrder`, `ATwoArgumentMethodCallMakesNoBoundMethod` |
| an error inside `string.Template` or a host `PythonModules` module | `ScriptError.Line` and the frames showed the module's line numbers under `<script>` | `AModuleFrameNamesItsFileAndTheLineIsTheScripts` |

### Native calls that outlived their run

One .NET call that runs long cannot be interrupted: the run overshot its deadline, and past deadline + 5.25 s
the engine gave up on the runner thread, which kept computing.

| defect | what happened | held by |
|---|---|---|
| `format(x, '.1000000f')` of a float, `format(Decimal(1), '.2000000000f')` | built 10**precision and its digits; the Decimal one had no length check at all | `Hosting/NativeOvershootTests`; `Values/FloatReprTests` `FixedPastTheExactDigitsPadsZeros`; `Modules/DecimalMethodsTests` |
| `round`, `quantize`, `to_integral` of a zero `Decimal` written with an exponent like `-999999999`; `statistics` over one | raised 10 to that exponent: never ended | `Modules/DecimalMethodsTests`; `Modules/StatisticsTests` `ZeroDecimalWithAHugeExponent` |
| `csv.Sniffer().sniff` on a sample like `',"a' * 21000` | a quadratic search in a regex without a timeout | `Hosting/NativeOvershootTests` `csv-sniff-quadratic` |
| `textwrap.wrap(..., break_on_hyphens=False)` and `textwrap.dedent` on megabytes of text | one uninterruptible regex `Split`/`Replace` | `Hosting/NativeOvershootTests` `textwrap-split`; `Modules/TextwrapTests` |
| `string.capwords(s, sep)` and `urllib.parse.parse_qsl(qs, separator=sep)` with a long `sep` | the BCL's multi-char `Split`, O(n·m) | `Hosting/NativeOvershootTests` `capwords-long-sep`, `qsl-long-sep`; `Modules/StringModuleTests` |
| `hashlib.pbkdf2_hmac` with a long salt and a large `dklen` | hashed the salt once per output block with no budget check | `Hosting/NativeOvershootTests` `pbkdf2-long-salt` |
| An `IntEnum` member whose value is a long str | `BigInteger.TryParse`: quadratic, no `MaxIntBits` gate | `Hosting/NativeOvershootTests` `intenum-str-value` |
| `str`/`repr` of a `Fraction` and a `range`, `json.dumps` of ints, `timedelta` overflow messages with huge ints | the quadratic net472 decimal conversion, uncharged | `Hosting/NativeOvershootTests` `fraction-str` |
| `random.seed(s)` of a long str or int, `random.getrandbits(k)` | built the number a byte or a word at a time: quadratic, a 16M-char seed never ended | `Hosting/NativeOvershootTests` `random-seed-str`; `Modules/RandomTests` |
| `statistics.variance` of huge ints, a `range` with a step past 64 bits, `limit_denominator`, `randrange`, `math.comb`/`perm` | a direct `BigInteger` product or division beside BigOps | `Hosting/NativeOvershootTests` `statistics-big-square`, `range-big-step`; `Values/RangeValueTests` `StepPastAMachineWord` |
| A long big-int product, division, gcd, power, modular power or decimal conversion | ran on a BigOps helper thread that finished after its run was stopped (a flood of stopped runs loaded 7.9 of 8 cores at a limit of 2) | `Values/BigOpsTests` `LongProductEndsAtTheDeadline` and its siblings; `Values/BigKernelTests`; `Hosting/NativeOvershootTests` `statistics-big-square`, `range-big-step` |
| `re.compile('a{100000}')` and any pattern starting with a count that large | .NET built a Boyer-Moore table for the m-char prefix in O(m²), three times, before any timeout applies | `Hosting/NativeOvershootTests` `regex-long-repeat`; `Modules/RePatternSyntaxTests` `LongLowerBoundBuildsNoPrefixTable` |

### A process that died or stayed broken

Found by a review of the whole sandbox; each row ended or damaged the host process at default limits.

| defect | what happened | held by |
|---|---|---|
| `[*[*[*…]]]` 3,000 levels deep (9 KB of source), and its tuple, set and `for`-target forms | parsed below the expression-depth count: a stack overflow on the caller's thread while compiling | `Syntax/CompileStackSafetyTests` `NestedStarredElementsAreASyntaxErrorOnASmallThread` |
| `str()` of an exception wrapping an exception 200,000 times (also uncaught), `repr` of a 200,000-deep `itertools.repeat` chain, `<=` of two `Counter`s nested 80,000 deep | re-entered `repr`/`str`/comparison at depth 0, where no depth check or stack probe ran: a stack overflow | `Stress/ValueModelStressTests` `NestedLeafReentryIsARecursionError`, `DeepLeafReentryAbortsOnStack` |
| The first `import string` or `import email.utils` of the process, made by a run that had used up its stack | the module's compile failed and the failure stayed cached: every later import in every engine was an engine fault until restart | `Hosting/CrossRunStateTests` `AnEmbeddedModuleCompilesFromAnExhaustedStack` |
| A method call on a namedtuple the run created, in a script the engine caches | the call site kept the run's namedtuple type: 205 MB per run of a 1M-field namedtuple, never released | `Hosting/CrossRunStateTests` `ACachedScriptKeepsNoTypeItsRunCreated` |
| Large distinct scripts | the compiled-script cache counted entries only: up to ~10 GB of syntax trees; it now holds 2M chars of source, a script weighing its characters or 8/3 of its tokens, whichever is more | `Hosting/CrossRunStateTests` `TheCompiledScriptCacheIsBoundedBySourceSize` |
| Long function names through deep frames and long exception chains; `raise e from e` with notes | the host's `ScriptTraceback` had no cap (136M chars, 541 MB per run); a cause cycle was rendered 17 times | `Hosting/CrossRunStateTests` `TheHostTracebackIsCapped`, `ACauseCycleIsRenderedOnce` |
| A run that named 1,100 classes (`make_dataclass` in a loop, or a long script of `class` statements) and asked `type()` of them or `reversed()` of their instances (second review) | `type()` and the iterator types were cached by name for the whole process, 1,024 names at most; past that `type(a) is type(b)` was False for two `re.Match` objects, in every engine, until the process restarted. A type object now lives with its type and a script-chosen name keys nothing shared; `type()` of a class is `type` | `Hosting/CrossRunStateTests` `ARunNamingManyClassesLeavesTypeIdentityAlone`; `Values/RecordClassTests` `TheTypeOfAClassIsType` |
| `repr` of a `defaultdict` that is its own factory, or of a 100,000-long chain of factories (second review) | the factory was rendered from C# at depth 0, outside the walk: a stack overflow. It is now a child of the walk, a cycle printing `...` | `Modules/CollectionsDictTests` `AFactoryCycleOrChainReprsWithoutRecursion`; Stress `e1`, `e2` |
| `except T` with `T` a tuple nested 700,000 deep (second review) | the handler match recursed: a stack overflow. CPython accepts only exception classes in the tuple, checked first, and so does the engine now | `Builtins/BuiltinsContainersTests` `AnExceptTupleHoldsExceptionClassesOnly`; Stress `e3` |

### Runs that leaked their thread

The same review: a call that did long work for the one step it charged, in a loop, ran until the runner
thread was leaked (deadline + 5.25 s, a core busy after `Run` returned). Every row is a case of
`Hosting/NativeOvershootTests` `ALoopOfChargedCallsEndsWithItsRun` and a probe of Stress group `k`.

| defect | what happened |
|---|---|
| A method or a constructor doing long work (`float(s)`, `Decimal(s)`, `uuid.UUID(s)`, `m.expand(t)`, `StringIO.readline`), also when it raises | only a free builtin had a deadline check after it returned |
| `in`, `==`, `<` and sorting of long strs and bytes; a tuple `==` of long strs | one native compare or search, charged one step |
| Set `<=` and the other set operators on large sets; `in` and truth of a `ChainMap` of millions of maps; `del d[0]` of a large deque | whole-table walks charged one step; the deque shifted the far side |
| `isalnum`/`isdigit`/`isnumeric` and case mappings of millions of astral characters | a string allocated per character, no deadline check inside the call |
| `x % 7` and the other short big-int operations in a loop at `MaxIntBits`; `Fraction` comparisons; `repr` of `itertools.repeat` with a huge count | linear native work outside the kernel, charged one step; the count's decimal text was the quadratic native conversion |
| `itertools.tee(it, 1500000)` | every `next` scanned every clone |
| A call with 300,000 keywords against 10,000 parameters; a dataclass with 40,000 fields (`__init__`, `astuple`, `repr`, `hash`) | a linear search per name |
| `traceback.format_exception_only` of an exception with 100,000 notes | `line += note`, quadratic |
| `[0 for x in r if c1 or c2 or ...]` with 70,000 terms | the compiled condition charged no step per term |
| XPath with a million `[1]` predicates or a 4M-step path; `iter()`, `findall('.//*')`, `tostring()` of a tree that contains itself | uncharged predicates and parsing; unbounded walks (`tostring` ended as an engine fault at 1.5 GB) |
| `fromstring` of a tag with 400,000 `xmlns` declarations | .NET's reader is quadratic in the attributes of one tag; tags past 10,000 attributes are now refused |
| `hmac` with a 16 MB key, `digest()` in a loop; 40 such objects | the key was hashed again per digest and copied per object; it is now hashed once (RFC 2104, the same MAC) |
| `csv.writer` with a 200,000-char `lineterminator`; `namedtuple` with 3M fields; `heapq.merge` over 4M empty inputs | a search of the terminator per field char; uncharged fields; dead inputs rescanned per item |
| `re.escape`, `unicodedata.normalize` around lone surrogates, `isinstance` with a huge type tuple, `bytes.split()`, codec error handlers (`namereplace`), `str.translate` | uncharged loops in one call; `translate` and the codec handlers' output are now capped and charged once |

The second review (also `Runtime/WorkChargedAsItScalesTests`, which checks that the steps grow with the work,
and Stress groups `f` and `y`):

| defect | what happened |
|---|---|
| A comprehension filtered by a 65,000-term `or`, a 200,000-leaf sum, or 99,000 `if` clauses | the compiled and fused forms of an expression skip the tree walk's NodeBatch per 64 nodes: one step per element. A compiled root now charges its size on every evaluation, a tree of 64 nodes or more does not fuse, and a clause level counts as a node |
| 100 calls of a function with 45,000 keyword-only defaults; a closure over 28,000 variables; `def`/`import` in a scope of many names | a search per parameter for its default and per captured name for its cell: quadratic. One pass over both now, and the scope's names are indexed; a frame charges its slots |
| A class statement in a loop, its `__init__` 50,000 assignments or its body 35,000 fields | the slots were recomputed by walking `__init__` on every execution, uncharged; the walk is the AST's and is made once, the merge is charged |
| `while L: del L[0]` on 5M items, `L.remove`, slice assignment, XML `del e[0]`/`insert`/`remove` | the tail shift was not charged (`pop` and `insert` were) |
| `s.startswith((p,) * 3000)` with 8M-char strings, its `endswith` and bytes twins; `==`, `<` and `sort` of 16 MB bytes | one compare per item uncharged; bytes compared in one go (the str twin was fixed in 1.1.0's review) |
| `slice.indices` and `range` slicing with million-bit bounds and steps; `a / b` and `a // b` of million-bit ints in a loop | a native division beside BigOps; and a division in BigOps' own kernel (~20 ms at that size) had no deadline check after it, so a loop of them leaked |
| `case [a, *rest]` on `range(2**40)` | copied the rest with no cap and no step: 256 MB and 3.7 s on 3 steps; capped and charged first |
| `asdict`/`astuple` of a dataclass DAG 40 levels deep | 2**40 nodes along its paths (CPython walks them too) with no step |
| `random.randbytes(250000000)` | the buffer was drawn before the str cap refused it; checked first, drawn a block at a time |
| `html.unescape` of `'&' + 'q' * 31` repeated, `attrgetter` of a 3M-part path, `deque *= 10**18` under a maxlen, `string.capwords` of 8M words | 30 lookups per `&` (no legacy name is longer than 6), a read per part, a full round of appends, a split into 8M strings, each on one step |
| `re.sub('', '', s)`, `re.split('', s)` over 4M chars, and every other loop of matches | one step per call, not per match: 4M matches on 94 steps. The one place every match runs through now takes a step |
| `json.JSONDecodeError(msg, doc, pos)` of a 16M-char document, 300 times; `base64` and `binascii` encoders and decoders over 9 MB, 40 times | the scan for the line number and the coding loops were not charged (8.5 s on 155 steps); the input now pays its length |
| `math.factorial(10**7)` | multiplied until the deadline and was refused by `MaxIntBits` only at the end; Stirling's estimate now refuses it before the first product, and each product is charged by the size of the result so far |

### Memory taken past what the budget charged

The same review: the process took hundreds of MB to GB past what `MaxAllocBytes` counted. Each case is in
`Runtime/BuiltBeforeCheckedTests` and Stress group `o`.

| defect | what happened |
|---|---|
| `ascii()`/`%a`/`!a`, `format(x, '.250000000g')`, `print` with a 16M-char `sep`, `json.dumps` escapes, `strftime('%Z' * 300)` with a long zone name, `html.escape`, `urlencode(doseq=True)` with a long key, `textwrap` indents and `tabsize`, a repeated `str.format` field, `calendar.weekheader`, XML char references | the output was built in full (up to 6x the text) before its size was checked; `format(.., '.2000000000g')`, `weekheader(2**31-1)` and `tabsize=2000000000` were engine faults (OutOfMemory) |
| `f"{s}{s}"` and every charged string builder | never checked `MaxStrChars`: a 32M-char str survived |
| `f(*itertools.repeat(x))`, `math.dist` and `random.choices(weights=)` of an endless iterator, `itertools.batched`, `namedtuple._make`, XML `extend` | a list grew with no charge and no item cap; doubling jumped the cap (a 7M `*args` tuple) |
| Kept regex matches: with an `endpos` (a copy of the subject), with 2,000 groups, with a repeated group's captures; a pattern with a repeated group over a long subject; the `sub` template cache; the `struct` format cache | kept by the run, charged 64 bytes or nothing |
| `json.dumps` and `repr` of a 5M-item list | two work-stack entries per item: 850 MB and 1 GB for 66 and 76 MB charged; they now take one entry per open container |
| `operator.attrgetter` of 15M dots, `urljoin` with 5M `../`, a quoted csv field spanning 400 lines of 1M chars | uncharged splits; the csv field limit was checked only when the field ended |

The second review (in `Runtime/UnchargedObjectsTests`, `Runtime/BoundedOutputsTests`, `Runtime/BudgetStringBuilder`
users, Stress groups `e` and `y`):

| defect | what happened |
|---|---|
| Chains of objects a module call returns: `g = itemgetter(g)`, `lru_cache` wrappers, `cmp_to_key` keys, `methodcaller`, `wraps`, `dataclasses.field`, `namedtuple._replace`, the `dataclass(...)` and `lru_cache(...)` decorators, `super()`, `ElementTree()`, `QName`, `NewType`, an engine-raised exception | made outside the value factory with nothing charged: 2.9 GB for 38 MB charged. A function made during a run now goes through `ValueFactory.Closure`, any other such object through `ChargeObject`; `str.lower` and the like are one object per type and slot |
| `format(s, '.N')` of a long str kept 40 times, `finditer(s, 0, endpos)` iterators kept, `json.dumps` with a long separator or indent at every nesting level | the truncated copy, the `endpos` copy and the per-level texts were made uncharged (647, 487 and 410 MB for 3-15 MB charged) |
| 150 classes of 35,000 fields | the slot and default tables of a class were never charged (968 MB for 0) |
| `namedtuple` defaults and field names, csv `DictReader`/`DictWriter` fieldnames, `random.sample`'s pool, `urlencode` pairs, `''.join`/`b''.join` of empty parts, all from an endless iterator | drained into a plain list with no charge and no cap; `urlunsplit` of one now stops past its arity, and a `__len__` that disagrees with the iteration is a `TypeError` in `sample` instead of an engine fault |
| `csv.writer().writerow(repeat(None))`; the repr builder | the row was built in a plain `StringBuilder` past `MaxStrChars`; the charged builder charged only what overshot its capacity (2 bytes a growth), so a 15M-char row was charged nothing |
| `urllib.parse.quote`, `re.escape`, XML text and attribute escaping, `unicodedata.normalize`, `bytes.hex(sep)`, `encode('utf-16')` and `encode('utf-8')`, a `replace` that grows, the `base64` and `binascii` encoders | the output was built in full (up to 18x the input for `normalize`) before its size was checked; each now counts its length first and is refused before anything is built |
| A quoted csv field across 120 lines of 1M chars after `csv.field_size_limit(2**31 - 1)` | the field grew to 120M chars: the field limit was the script's, and nothing else checked it; `MaxStrChars` now holds whatever the field limit is |
| `rsplit()`, `bytes.split()`, `parse_qs('a&' * 8000000)`, `string.capwords(s, sep)`, `textwrap.shorten` and `wrap` of 8M words, the match positions of a `replace` | the parts were collected in a plain list, charged when the result was made or never (623 MB for 30 MB in `shorten`, which also kept every regex `Match` its `MatchCollection` had handed out); each part is now charged as it is cut, with the item cap, and `parse_qs` and `capwords` no longer cut them at all: they stream into the result |
| ElementTree `find`, `iterfind`, `findtext` and `itertext` over 6,000 shared copies of a 6,000-child element; `SubElement` and the parser past the item cap | every match was built before the first was returned (589 MB for 0, and `find('*/*')` ran into its deadline); `SubElement` and the parser skipped the cap that `append` has. The path is now evaluated lazily, as CPython's chain of generators is, so `find` costs a few steps there; walks keep one frame per level and read children live, so 600 copies of a 600-child element no longer meet a 1,000-item cap that CPython's walk never meets |
| `Counter.elements()` kept 12 times over a million keys | a copy of the keys and counts per call: 565 MB for 158 charged. It is lazy over `items()` now, as CPython's is |

### Parallel runs that interfered

Found by the second review, which ran ~70,000 runs in parallel on one engine and on several against a
sequential baseline.

| defect | what happened | held by |
|---|---|---|
| Unseeded `random` (and `uuid.uuid4`, which drew from it) in runs started together | `new Random()` is seeded from `Environment.TickCount` on .NET Framework: runs within one 15.6 ms tick drew the same numbers (157 of 160 parallel runs matched another). An unseeded generator now starts from the OS generator | `Hosting/ScriptEngineGateTests` `ParallelRunsDrawDifferentRandomNumbers` |
| `uuid.uuid4()` after `random.seed(x)` | drawn from the run's `random` state, so every run that seeded repeated its ids; CPython's comes from `os.urandom`, and now so does ours | `Modules/UuidTests` `Uuid4IgnoresRandomSeed` |
| A runner that finished just after the host's wait gave up | counted as leaked with nothing left to uncount it: phantoms piled up until the breaker refused every run of the engine (96 racing runs left 10). Only a running thread is counted now, and a finished one returns its own result | `Hosting/LeakBreakerTests` `AThreadThatAlreadyFinishedIsNotCountedAsLeaked` |
| `Thread.Abort` of the host thread (an ASP.NET timeout) between taking a slot and starting the runner | the slot was lost for good (two of four after 400 aborts); both steps now run where an abort cannot land. The run gate and its slots were later removed; the abort case stays covered by `Hosting/ScriptEngineGateTests` `HostThreadAbortsLeaveTheEngineWhole` |
| Parallel runs of one cached script calling a method of a namedtuple each creates | a run that found another run's type in the call-site cache did not register its own, and the other run's end could clear the site just before it wrote: the type (21 MB for 100,000 fields) stayed with the cached script. Each run now clears its own last entry, if it is still there | `Hosting/CrossRunStateTests` `ParallelRunsOfACachedScriptLeaveNoTypeInItsCallSites` |
| A host changing the `PreludeModules` list, a `ModuleOverrides` table or a `RunRequest`'s dictionaries and lists while runs execute | documented as snapshots, they were the host's own objects, read live: a later run saw the change, and 1,104 of 1,200 runs faulted with overrides changed meanwhile. The engine copies them when it is built and the request when a run starts (a request changed during that copy is a `HostError`) | `Hosting/OptionsSnapshotTests` |

### Engine faults and sizes past the item cap

The same review: a value the script set or passed that the engine then cast or indexed unchecked (an
`EngineFault` no `except` can catch), and collections sized by one argument with no item check. The faults
are in `Modules/ArgumentFaultTests` `AValueTheEngineCastUncheckedIsAScriptError`, the caps in
`Runtime/BuiltBeforeCheckedTests`, both in Stress group `s`.

| defect | what happened |
|---|---|
| XML `text`, `tail`, `tag` or an attribute name set to an int or bytes, then `tostring`/`write` (every method) or a text predicate; a child set to a non-Element with `e[i] = 5` | hard casts in the serializer and XPath; now CPython's `TypeError` ("cannot serialize 5 (type int)"); an attribute predicate over a non-str value compares unequal, and a comment's text is written with `%s` as CPython does |
| `Decimal(1).compare()`, `compare_signal()`, `copy_sign()`, `same_quantum()` with no argument | an index past the arguments; now `TypeError` |
| `statistics.quantiles` of 100,000 points into 30,000 parts | `i * m` overflowed an int: a wrong index |
| `re.fullmatch('(?x)a # comment', ...)`, `match` of a VERBOSE pattern ending in a comment | the anchored twin's closing parenthesis fell inside the comment |
| `p.search(s, pos, endpos)` with `pos > endpos` | a negative length; now `None`, and `[]` from `findall`/`finditer` |
| `unicodedata.normalize` of text with noncharacters (`chr(0xFFFF)`, `chr(0xFDD0)`) | .NET's normalizer throws on them; they are now kept as they are and the text around them normalized, as lone surrogates already were |
| A zip entry whose `header_offset` is set to `2**63 - 16`, or forged in a ZIP64 extra field | `offset + 30` overflowed; now `BadZipFile` |
| `struct.unpack('0s' * 6000000, b'')`, `combinations_with_replacement([1], 20000000)`, `product([1], repeat=12000000)`, `l[0:0] = r` and `l.insert(0, x)` on a list at the cap | 6M- to 20M-item tuples and lists past `MaxCollectionItems`; each now ends on it |

The second review: an `__eq__`, `__lt__`, key function or iterator - or the `__bool__` of what a comparison
returned - that cleared or rebuilt the container being walked. Every case answers as CPython 3.14 does, in
`Runtime/MutationDuringCallbackTests`.

| defect | what happened |
|---|---|
| list `<`/`<=`, `list.remove`, slice assignment from an iterator that shrinks the list, `match` sequence patterns | an index or a count from before the callback: engine faults. Now the live sizes (`list_richcompare`), nothing removed, the value taken before the bounds, the subject unpacked first |
| deque `in`, `count`, `index`, `remove`, `==`, `<` | engine faults (a slot the clear had emptied); now CPython's `deque mutated during iteration` |
| `heapq` heapify, heappush, heappop, heapreplace, heappushpop; `bisect`/`insort` with `key=` or `__lt__`; `ChainMap` length | engine faults; now `RuntimeError: list changed size during iteration`, `IndexError`, and the maps taken first |
| A dict or set whose key's `__eq__` clears or rebuilds it during a lookup, insert or delete | the probe went on through the old index: an engine fault for a set, and a delete that marked another key's slot or never ended. The probe now starts over, as CPython's `lookdict` does |

### Wrong answers

| defect | what happened | held by |
|---|---|---|
| `string.capwords(s, '')` | split on whitespace; CPython raises `ValueError: empty separator` | `Modules/StringModuleTests` `CapwordsRefusesAnEmptySeparator` |
| An `IntEnum` member valued `'1_000'` or written in non-ASCII digits | `ValueError`; CPython converts it as `int()` does | `Modules/EnumTests` `IntEnumStrValueConvertsAsInt` |
| `lru_cache()` and `lru_cache(typed=True)` with no `maxsize` | cached up to the 4,096-entry cap, as `maxsize=None` does; CPython's default is 128 | `Modules/FunctoolsTests` `LruCacheWrapperSurface` |
| `isinstance(1, (int,) + twenty_sibling_tuples)` | counted every tuple it visited against a limit of 16: a `RecursionError`; the limit is now on the nesting (`MaxDataDepth`) | `Builtins/BuiltinsContainersTests` `IsinstanceTakesAnyNumberOfSiblingTuples` |
| `except (KeyError, (ValueError,))`, `except (KeyError, 1)` | matched the nested tuple, or matched before reaching the non-class; CPython checks every item first: `TypeError` | `Builtins/BuiltinsContainersTests` `AnExceptTupleHoldsExceptionClassesOnly` |
| `float('1.1283791670955e-310')`, float literals, `json.loads`, `round()` and host inputs with 16+ digits or near the subnormal range | .NET Framework's `double.Parse` missed the nearest double by an ulp on 8.6% of a hard 760,000-text battery; text now goes through `NearestDouble` (Clinger, Eisel-Lemire, exact integers), which matches CPython on all of it | `Runtime/NearestDoubleTests` |
| `repr` of floats: `268243531196398.62` printed as `...98.63`, `9.37631e-113` as `9.376309999999999e-113`, `9.9125e-315` as `9.9125000005e-315`, `5.0288368436448605e-224` as `5.02883684364486e-224` (which reads back as another double) | wrong on 2.3% of everyday values and 9.7% of random bit patterns: the 15- and 16-digit candidates were .NET's roundings of its own 17-digit rendering (rounded twice), checked with the inexact parser, and a 17-digit tie went away from zero; CPython keeps the shortest nearest digits and, on a tie, the even one | `Runtime/NearestDoubleTests` `ReprIsCPythons` |
| `float(2**53 + 3)`, `float(2**64 + 2**11 + 1)`, `math`/`format`/`random` of big ints, `float(Decimal)` | .NET Framework's `(double)BigInteger` truncates (`9007199254740994.0`, CPython `...996.0`); a Decimal went through a double division | `Runtime/NearestDoubleTests` `FloatOfIntRoundsToEven` |
| `(2**1024 - 1) + 0.5` and every mix of float with an int that rounds past the largest double; `math.isfinite`/`isinf`/`isnan` of such an int; `statistics.fmean`, `median_grouped`, `random.choices` weights; `format(n, 'f')` | `inf` or `nan` (a bit-length test let 1024-bit ints through), `True` from `isfinite`, and four copies of the check said "long int too large"; one conversion now gives CPython's `OverflowError: int too large to convert to float` | `Runtime/IntToFloatTests` |
| `math.log`, `log2`, `log10` of ints of 1,000 bits and more | off by one in the last digit: scaled by `2**(e - 900)` where CPython's loghelper takes the 53-bit mantissa (`_PyLong_Frexp`) | `Runtime/IntToFloatTests` `Arithmetic` |
| `statistics.geometric_mean` | the 3.8-3.11 rules: floats first (a 400-digit int was `inf`), a zero an error; now 3.14's: the log of each value, `0.0` with a zero, `('No negative inputs allowed', x)` | `Runtime/IntToFloatTests` `Statistics` |
| `float('nan')`, `math.nan`, json's `NaN`, the nan statistics returns | the sign bit set (.NET's `double.NaN`): `copysign(1, math.nan)` was `-1.0`, `struct.pack` wrote `...\xff`, `-math.nan` was positive. CPython's `Py_NAN` is positive, and so is ours; a NaN from arithmetic stays the hardware's, as in CPython | `Runtime/IntToFloatTests` `NanIsPositive` |
| `struct.pack('f', 1e39)` in native mode, `'<f'` of a value just past the largest float32 that rounds to it, `struct.pack('d', 2**1024 - 1)`, `struct.pack('d', Fraction(1, 3))` | an `OverflowError` where CPython's C cast gives `inf`; refused though it rounds to `FLT_MAX`; packed `inf`; refused a value with `__float__`. Now as CPython; `math.isfinite(Fraction(1, 3))` converts too | `Runtime/IntToFloatTests` `StructAndTime` |
| `time.gmtime(10**20)`, `datetime.date.fromtimestamp(10**400)`, `time.sleep(10**400)` | "timestamp out of range", "cannot convert float infinity to integer"; CPython's texts now | `Runtime/IntToFloatTests` `StructAndTime` |
| `a·b = 1`, `x‿y`, a letter followed by a combining accent, `ℌ`, `ﬁx`; `x² = 1`, `x = 1 → 2`, a NO-BREAK SPACE in the source | valid PEP 3131 names were "invalid character in identifier" (`char.IsLetterOrDigit`), at the name's start, and names were not NFKC-normalized (`ℌ` was not `H`). Now CPython's tokenizer: any non-ASCII char may start a name, `verify_identifier`'s message (`invalid character '²' (U+00B2)`, `invalid non-printable character U+00A0`) at the char's own column, and NFKC | `Syntax/LexerNameTests` |
| `RunStatistics.CompileTime`, `RunStatistics.LeakedThreadsAtStart` | documented, always 0; `Run(source)` now reports its compile (0 on a cache hit and for `Run(CompiledScript)`), and every run the leaked runner threads when it was admitted | `Hosting/ScriptEngineTests` `CompileTimeIsReported`; `Hosting/EngineLifecycleTests` `ALeakedThreadHoldsItsSlotUntilItReturns` |
| `itertext()` of an element whose text or a tail is `''` | yielded the empty strings; CPython yields a text only when it is true | `Modules/XmlTreeTests` `IterText` |
| `iter()` and `iterfind()` with a child appended or removed meanwhile; `Counter.elements()` with a key added meanwhile | a snapshot taken at the call; CPython reads the children live and raises `RuntimeError: dictionary changed size during iteration` at the next key | `Modules/XmlTreeTests` `LazyWalksReadChildrenLive`; `Modules/CollectionsCounterTests` `Counters` |
| `textwrap.shorten` and `string.capwords` of text with a NO-BREAK SPACE, an IDEOGRAPHIC SPACE, `chr(0x1c)` or `chr(0x85)` | `shorten` collapsed only the ASCII whitespace and `capwords` split on .NET's; CPython's `str.split()` splits on all of Python's | `Modules/TextwrapTests` `Surface`; `Modules/StringModuleTests` `Surface` |

### One run reaching another, a host error a script dropped (review 3, P0-P2)

| defect | what happened | held by |
|---|---|---|
| A host function throwing one `ScriptExceptions.*` object from every run (a static field) | run B read and changed run A's notes, `__cause__` and traceback; the message's cached hash crossed engines (`k == 'acct-42'` but `k in d` False) | `Hosting/HostIsolationTests` `RunBSeesNoNoteOrCauseRunALeftOnTheHostException`, `TheHostExceptionsMessageHashesWithEachEnginesSeed` |
| Two sources that differ only in lone surrogates or U+FFFD, on one engine; `Compile(src, name)` | one compile-cache entry (SHA-256 of a lossy UTF-8): run B executed run A's program, and the cache answered with another script's name | `Hosting/HostIsolationTests` `SourcesThatDifferOnlyInSurrogatesAreCachedApart`, `TheCacheKeepsTheRequestedScriptName` |
| `random.seed(str)`, `ZipFile.writestr` and zip member names with a lone surrogate | encoded as U+FFFD; CPython raises `UnicodeEncodeError` | `Hosting/HostIsolationTests` `ALoneSurrogateRaisesWhereCPythonEncodesStrictly` |
| A host thread aborted (an ASP.NET timeout) while the first `ScriptRunner` or the builtins template was being built | the `Lazy` kept the exception: every later runner threw, every run of every new engine was an engine fault, until restart | the race harness of review 3 (`abortlazy`); the template is now built in the engine's constructor |
| `ET.Element('r').find('x/' * 4000000 + 'x')`, `find('x' + '[@a]' * 3900000)` | P4's lazy path built every step and walk source up front, uncharged: 1.7 GB, 7 s past the deadline | `Evaluator/EngineFaultClassesTests` `AHugeXPathIsChargedAsItIsParsed` |
| A host failure or an engine fault inside `try` with a `finally` that returns, breaks, continues or raises; a `with` whose `__exit__` suppresses; `contextlib.suppress` | the run went on and could end `OK`: the contract says a host error ends the run | `Hosting/HostIsolationTests` `AHostFailureEndsTheRunWhateverTheFinallyDoes` |
| `class str` then `type('').upper(str())`; `namedtuple('dict', ...)` then `type({}).keys(...)`; `isinstance(namedtuple('int', ...)(1, 2), int)` | a script class named like a builtin passed the builtin's checks: engine faults, and `True` where CPython says `False` | `Evaluator/EngineFaultClassesTests` |
| `[0 for 1 in x]`, `x = *a`, `*a`, `[*a for a in b]`, `f((b)=1)`, `Element.insert(0.0, e)`, `insert(-1, e)` | accepted by the parser and an engine fault in the evaluator; `insert` cast its index unchecked and clamped a negative one to 0. Now CPython's syntax errors and list-insert rules | `Evaluator/EngineFaultClassesTests` |
| A host changing `RunRequest.Builtins` while runs start | copied later on the runner thread: 2,245 engine faults in 2,400 runs; now copied on the caller's thread, a change during the copy is a `HostError` | `Hosting/HostIsolationTests` `ATableChangedWhileRunsStartIsAHostErrorNotAFault` |
| A loop of failing `Enum('E', 'a ' * 8000000)` calls; `for k in d` / `x in d.values()` / `d == e` / `repr` over a dict or set with millions of deleted entries; `hash`, `== float`, `>>` of 4M-bit ints and `hash` of huge `Fraction`s | seconds of work on a few steps: a leaked runner, or 5 s past the deadline | `Runtime/UnchargedWorkTests` |
| `[a.m for _ in range(4000000)]`, `csv.Sniffer().sniff('a\n' * 6000000)`, `isnumeric()` over the astral planes, 115 integer literals of 4,300 digits | memory charged to no run (bound methods, the sniffer's lines), a process-wide table that only grew, 0.86 GB allocated at compile time | `Runtime/UnchargedWorkTests` |
| The first runs after a cold start; `strptime` over a 15M-char whitespace run | the builtins template was built inside their deadlines (8 of 8 `DeadlineMs` at 150 ms); 3 s on ~150 steps | `Runtime/UnchargedWorkTests` `StrptimeChargesAWhitespaceRun` |
| A clock that throws or breaks its contract, a sleep quantum of -1 ms, `SetValue(name, null)`, a host exception whose `Message` throws, a request list whose enumerator throws, `Run(null)`, `TryCompile(null)`, `ScriptRunner.SetScalar(null, ...)` | engine faults, a `time.sleep` that never returned (two such runs opened the circuit breaker for good), and `Run` / `ScriptRunner.Run` throwing | `Hosting/HostContractTests` |
| `hashlib.md5`, `hmac` over md5 and `uuid.uuid3` on a FIPS-enforcing machine in an ASP.NET host (.NET 4.5 mode) | the framework's MD5 throws there: an engine fault; now a managed MD5 | `Hosting/HostContractTests` `Md5IsTheManagedOne` |
| A run started from a thread in a culture with another minus sign or language | the runner thread inherited it: U+2212 in messages, .NET's texts in Russian, the conformance XML case failing | `Hosting/HostContractTests` `TheCallersCultureNeverReachesARun` |
| `hash()` of a `ZoneInfo`, a bound method, a `typing` alias | `string.GetHashCode`: different in each process under randomized string hashing | `Hosting/HostContractTests` `AZoneInfoHashesAsItsKey` |
| An engine fault the run caught itself | no `EngineFault` event | `Hosting/HostContractTests` `AFaultTheRunCaughtStillRaisesTheEvent` |
| `ScalarsIn` of `"1\0"` and of 4,301 digits | an int; now a str (a sign and at most 4,300 ASCII digits make an int) | `Hosting/HostContractTests`, `Hosting/RunIOTests` |
| `json.loads('"\\ud800"')`, `JsonIn` with a lone surrogate escape | U+FFFD (Newtonsoft); CPython keeps the surrogate | `Hosting/HostContractTests` `JsonKeepsALoneSurrogate` |
| A ninth run on the default engine; a fifth one at the default 256 MB per run | refused at once with `MaxConcurrentRuns`, and the fixed 1 GB ceiling admitted 4 runs where 8 were documented. Now the engine does not limit how many runs execute at once: `Run` is synchronous, so the host's threads bound it; `MaxConcurrentRuns` and `GlobalMemoryCeilingBytes` are obsolete and ignored, and the leaked-thread breaker is the only refusal | `Hosting/UnlimitedRunsTests` |
| `EngineOptions.MemoryHygiene` changed after the engine was built | took effect; the options are now copied like the others | `Hosting/HostContractTests` `MemoryHygieneIsCopiedAtConstruction` |
| `tools/run-gates.ps1` on main | failed although every probe held: it cut probe names to 30 chars; and an engine fault counted as held | the gate itself: the runner's TSV rows, `bombs/expected.txt`, `OVERSHOOT`/`LEAK` flags |

### Wrong answers and missed sites (review 3, P3-P5)

| defect | what happened | held by |
|---|---|---|
| Every `Run` after the culture fix above | it set the runner's culture on the unstarted `Thread` object, which from .NET 4.6 on sets the CALLER's: the host's thread (a web request) came back in the invariant culture, with an AsyncLocal on its context where a `Thread.Abort` landing in the change callback fail-fasts the process (the test host died in 4 of 6 runs of the abort tests). Now set on the runner thread itself; the compile never touches the caller's culture | `Hosting/HostContractTests` `ARunLeavesTheCallersCultureAlone` |
| `sum([0.1] * 10)`, `sum([3.3, 1.1, 2.2])` | plain float adds: `0.9999999999999999`, `6.6000000000000005`; now CPython's phases, Neumaier-compensated over floats | `Spec/SilentWrongAnswersTests` |
| `format(Decimal('1E+2'), 'f')`, `'.2'`, the `e`/`g`/`n`/`%` codes, `#`, `,`/`_`; `round(Decimal('2.5'))`; `ROUND_HALF_UP` with `f'{d:.2f}'` | float rules, always half-even, `e`/`g`/`n` refused; now `_pydecimal`'s `__format__` in the context's mode, and `round(d)` half-even | `Spec/SilentWrongAnswersTests`, `Spec/OracleCorpusTests` (702 lines) |
| `'%.2f' % Decimal(...)`, `'%d' % Decimal(...)`, `format(Fraction(1, 3), '.2f')` | `TypeError`; now `__float__`/`__int__` as CPython, and `Fraction.__format__` (3.12) | `Spec/SilentWrongAnswersTests` |
| `any((found := r)['qty'] > 4 for r in rows)`, `[total := total + x for x in xs]` | the walrus bound inside the comprehension: `found` stayed `None`; now it binds in the enclosing function or module, with CPython's four syntax errors | `Spec/SilentWrongAnswersTests` |
| `type(datetime.date(2020, 1, 1)) is datetime.date` and 19 other module types; `isinstance(ET.Element('a'), ET.Element)` | `False`: `type()` answered a process-wide type object, the module made its class per run | `Spec/SilentWrongAnswersTests` |
| `findall('{*}a')`, `{}a`, `{ns}*`, `{*}*` | no match; now ElementPath's wildcards | `Spec/SilentWrongAnswersTests` |
| `ET.fromstring` and `ET.parse` of bytes with a BOM, in UTF-16 or with `encoding="windows-1252"`; `json.loads(bytes)` with a BOM or in UTF-16 | `ParseError`, `UnicodeDecodeError`, `TypeError`; now expat's and `json.detect_encoding`'s choice, and an undecodable byte is a `ParseError` at its position | `Spec/SilentWrongAnswersTests` |
| A quoted csv field that spans lines `1,"L1\r\nL2"` | the line ending became `\n`; now kept as written, and lines without one add none | `Spec/SilentWrongAnswersTests` |
| `ZoneInfo('Europe/Moscow')` in 2012, `America/Asuncion` in 2020 | `dst()` measured against today's base offset (`1:00`, `-1 day, 23:00`) and the name followed it; now against the standard offset in force | `Spec/SilentWrongAnswersTests` |
| `statistics.stdev([0, 10**200])`, `stdev([13, 43, 32, 48])` | `inf`, a last digit off on 269 of 3,000 datasets; now `_float_sqrt_of_frac` | `Spec/SilentWrongAnswersTests`, `Modules/StatisticsTests` (exact) |
| `[next(it) for _ in range(3)]` past the end | `RuntimeError` (PEP 479 applied to list comprehensions); now `StopIteration`, as 3.12+ | `Spec/SilentWrongAnswersTests` |
| `x = 'mod'; def f(): x = 5; class A: x = x`; `class B: y: int = 5; z = y`; decorators, base, constants and method defaults with side effects | the class body read the function's `x`, missed `y`, and ran its parts out of order; now decorators, base, then the body top to bottom | `Spec/SilentWrongAnswersTests` |
| A deque mutated by `d[0] = x`, `reverse()` or `rotate()` of one item during iteration | `RuntimeError`; CPython counts size and order changes only | `Spec/MissedSitesTests` |
| A million `f"{i}:{x}"`, a million caught `KeyError`s | 256 MB and 101 MB of budget (each small builder paid twice, each raised exception at once); the first fails the default limit. A builder's first 256 chars and an exception no handler keeps are not charged | `Spec/MissedSitesTests` |
| `𝐓𝐫𝐮𝐞 = 5`, `𝐦𝐚𝐭𝐜𝐡 x:`, a name of `ℌ` + 5,000 combining astral marks | accepted, a match statement, a `NameError` (the NFKC chunker cut a surrogate pair) | `Spec/MissedSitesTests` |
| `'a b'.split(None, 1, 5)`, `statistics.mean([1], [2])`, `itertools.count(0, 1, 5)`, `random.shuffle(x, f)` | accepted | `Spec/MissedSitesTests` |
| `namedtuple('P', 'a\x1cb')`, `uuid.UUID(' hex ')`, `float.fromhex('\xa00x1p0')`, `urllib.parse.unwrap`, `strptime` whitespace | .NET's whitespace where Python's (or C's) applies | `Spec/MissedSitesTests` |
| `struct.pack('<f', 2**200)` | `OverflowError`; CPython 3.14's `struct.error` for an int | `Spec/MissedSitesTests` |
| `dict(a)` of colliding keys, `a \| b` with a mutating `__eq__`, `a.keys() & b.keys()` | `__eq__` ran on every copied key, a `KeyError`, the bigger side walked; now `dict_merge`'s and `_PyDictView_Intersect`'s order | `Spec/MissedSitesTests` |
| `uuid.uuid1()` after `random.seed(7)` in parallel runs | 39 distinct of 160; now a process-wide node and last timestamp, as CPython's | `Spec/MissedSitesTests` |
| `hash()` of a class, an enum, an exception class, a builtin or a module function | a process-wide counter: a run read how many classes other runs had made | `Spec/MissedSitesTests` |
| The gates | no CPython oracle, and no check under another culture, in the 4.5 mode, for FIPS or of parallel runs; Stress saw neither peak memory nor CPU after a run; tests that could not fail (`Assert.True(true)`, a swallowed exception, uuid4 hiding the seeding), wall clocks in the everyday run | `Spec/OracleCorpusTests`, `Infra/FipsSafeCryptoTests`, the `PEAKMEM`/`BUSYAFTER` flags, `docs/CI-MATRIX.md` |

### CPython 3.14's texts and corners (review 3, P6)

| defect | what happened | held by |
|---|---|---|
| Syntax errors | 79 of 300 CPython 3.14 cases had its text, line and column; now 287 (the rest are dialect or listed in `deviations.md`): the lexer's errors win over the parser's as in CPython's tokenizer, an unclosed bracket is `'(' was never closed` at the bracket, `invalid syntax. Perhaps you forgot a comma?`, `expected ':'`, `expected an indented block after 'if' statement on line 1`, CPython's target, argument, parameter, import, `except` and pattern messages, tabs measured as CPython does | `Spec/SyntaxErrorOracleTests` (`Oracle/syntax-errors.tsv`) |
| `d[*a]`, `d[*a, 3]` (3.11), a hex, octal or binary literal of more than 4,300 digits | a `SyntaxError`; the power-of-two bases have no digit limit in CPython, and the octal and binary ones now parse in one linear pass | `Oracle/p6_modules.py`, `Syntax/LexerNumberTests` |
| `yield`/`await` at module level, `return` in a class body, names in a class body not mangled (`__x` in `class A` reached `A.__x`), an irrefutable pattern before another case, a repeated name or key in one pattern | accepted | `Spec/SyntaxErrorOracleTests` |
| `rb'a\'b'` | a syntax error: a raw literal's backslash did not protect the quote | `Syntax/LexerStringTests` |
| `1 / 0`, `0 ** -1`, the `math` domain errors, `math.factorial(2.0)`, `fsum([inf, -inf])`, `datetime.date(0, 1, 1)` and the other range errors, an unbound local | the texts of older versions (`integer division or modulo by zero`, `math domain error`, `year is out of range`, `local variable 'x' referenced before assignment`) or accepted (`factorial(2.0)`) | `Oracle/p6_runtime_texts.py`, `Values/NumericArithTests`, `Modules/MathModuleTests` |
| `(-1.0) ** inf`, `0.0 ** -inf`, `(-inf) ** 3`, `nan ** 0` and the other C99 `pow` cases | .NET's `Math.Pow` | `Oracle/p6_runtime_texts.py` |
| `re.compile('a(b')` and every other bad pattern | .NET's message; now `re.PatternError` (`re.error` an alias) with sre_parse's text, `pos`, `lineno` and `colno`; a variable-width look-behind is refused as in CPython; `\z` and `(?#...)` accepted; `\w` and `\s` take Python's classes | `Modules/RePatternSyntaxTests`, `Oracle/p6_runtime_texts.py` |
| A `StopIteration` raised by the function given to `map`, `filter`, `starmap`, `takewhile`, `dropwhile`, `accumulate`, `groupby` or `iter(f, s)` | escaped to the script; CPython's consumers read it as the end. `groupby` also read the next value right after handing one out, so a key raising there lost the value handed out last; now it reads when asked, as `itertoolsmodule.c` | `Oracle/p6_runtime_texts.py` |
| `datetime.fromisoformat('2024-01-02 ')`, `'2024-01-02T'` | a midnight datetime; CPython refuses a separator with no time after it | `Oracle/p6_modules.py` |
| `'\U0001F97A'.isprintable()`, `repr('₿')`, `'\U0001E030'.isalpha()`, `unicodedata.normalize` of the 21 decompositions newer than Windows' tables, a run of 20,000 combining marks | .NET Framework's Unicode 8 and the OS's normalization; now the Unicode 16 tables and UAX #15 | `Modules/UnicodedataTests`, `Builtins/BuiltinsStrMethodsTests` |
| `str(10**5000)`, `int('9' * 5000)` | answered; CPython 3.11+ refuses more than 4,300 decimal digits (`ValueError`, power-of-two bases exempt) | `Modules/FractionValueTests`, `Stress/BuiltinsStressTests` |
| `Decimal(0) / 0`, `Decimal(0) ** 0`, `-Decimal(0)`, `Decimal((0, (1,), -2))`, `float(Decimal('-0'))` | `DivisionByZero`, `1`, `Decimal('0')`, refused, `0.0` | `Oracle/p6_modules.py` |
| `int('12', 1)` | `int() base must be >= 2 and <= 36`, without CPython's `, or 0` | `Builtins/BuiltinsPhaseCTests`, `Evaluator/QuietCallTests` |
| `int(b'12')`, `float(b'1.5')`, `ord(b'a')`, `round(x, None)`, `format(True, '>5')`, `'%s %s' % ('a',)` with a mapping, `min([])`'s text | refused or a different answer or text | `Oracle/p6_modules.py`, `Builtins/BuiltinsIterTests` |
| `base64.b64decode(..., validate=True)`, `b16decode` of lower case, `hmac.new(key)` without `digestmod`, `compare_digest('a', b'a')`, `json.dumps(sort_keys=True)` of mixed keys, `csv` of one empty field, `strptime('%G-%V-%u')`, `isocalendar()`, `ET.find('b[0]')`, `'/a'`, `.//b[2]`, a frozen dataclass's `FrozenInstanceError`, `int \| str`, `case deque(...)` against a sequence pattern | binascii's, hmac's, csv's, `_strptime`'s and ElementPath's rules not followed | `Oracle/p6_modules.py`, `Modules/XmlXPathModernTests`, `Modules/CsvTests`, `Modules/GapClosureTests` |

### Found by review 4, before the release

| defect | what happened | held by |
|---|---|---|
| `del [a]`, `del (a,)`, `del ()` | an engine fault; CPython runs them | `Oracle/r4_wanted.py` |
| `S = type(''); class str(Exception): pass; S.upper(str('x'))`, also `dict`, `int`, `list` | an exception class named like a builtin passed the builtin's name check: `isinstance` said `True`, then an engine fault. Now `False` and `TypeError` | `Oracle/r4_wanted.py` |
| `zoneinfo` `dst()` in a year the zone changed its standard offset (Pyongyang 2015, Volgograd 2018, Asuncion 2024, Minsk 2011) | Windows keeps the other part of that year as daylight of a whole-year rule, and it read as daylight. What remains is in `deviations.md` | `Oracle/r4_wanted.py` |
| A one-line `def f(x): return <expr>` with a wide body | charged one statement per call whatever the body's size: 6.2 s for 3,089 steps, where the same lambda stops at `MaxSteps` | `Evaluator/OneLineDefTests` |
| A host call failing inside the `__str__` of an uncaught exception's message | an engine fault. Now `HostError` | `Hosting/HostIsolationTests` |
| A host function calling `Thread.CurrentThread.Abort()` (`Response.End`), a host throwing `new ScriptException(null)`, a provider with a null member | an engine fault or a null reference. Now `HostError`, and the abort does not reach the runner | `Hosting/HostIsolationTests` |
| `csv.DictReader` over a blank line | an extra row of `None`. Blank lines are skipped | `Oracle/r4_wanted.py` |
| `except ZeroDivisionError` around `Decimal(1) / 0` | missed it: `decimal.DivisionByZero` had one parent. It has both now | `Oracle/r4_wanted.py` |
| `next(map(f, ...))` when `f` raises `StopIteration('val')` (also `zip`, `filter`, `itertools`) | arrived as an empty `StopIteration()` | `Oracle/r4_wanted.py` |
| `[y := y + 1 for _ in range(2)]`, `[y for ...]` before `y` is assigned, in a function | "cannot access free variable". CPython runs a list, set or dict comprehension inside its function (PEP 709): `UnboundLocalError` | `Oracle/r4_wanted.py` |
| `format(Decimal('1.5'), '.15900000f')` | about five uncharged copies of the result: a peak of 205 MB is 143 MB now | `Oracle/r4_wanted.py` (the answers) |
| Syntax hints fired before CPython's parser would fail: `(a[0] := )`, `f(a[0] := 1)`, a missing comma after a comprehension's `in` or `if`, `lambda x=: 0`, `yield` where an expression is needed; `except E as (a):` was accepted | a wrong position or text, or no error | `Oracle/syntax-errors.tsv` |
| `RunCompleted` for a syntax error, a refusal, a leaked thread | not raised, though documented for any end of a run | `Hosting/HostContractTests` |
| Invalid `JsonIn` | failed after the prelude's and provider's code had run. Inputs are parsed first | `Hosting/HostContractTests` |
| A scalar output whose `str()` raises | a `RuntimeError` at the script's last line, documented as `HostError` | `Hosting/HostContractTests` |
| `ScriptError.Message` cut inside a surrogate pair; `PrintOutput` null or `""` by which refusal it was | a lone surrogate in a host's log; now `""` for any run that never started, null only with a `PrintSink` or a leak | `Hosting/HostContractTests` |

### Regressions of those fixes (review 4)

| defect | what happened | held by |
|---|---|---|
| `int \| str \| None`, `(int \| str).__args__`, `type(int \| str)` | an uncatchable engine fault: the union's type could not be built. Now they answer as CPython 3.14, the type is `typing.Union`, `int \| int` is `int`, and building a union is linear and charged | `Oracle/r4_required.py` |
| A hung host call with 1 to 3 run slots | later runs waited in the queue until it returned, forever if it never did. The queue and the slots are gone | `Hosting/UnlimitedRunsTests` |
| `match` patterns of thousands of keys, captures or keyword names, a mapping key of a long hex literal | quadratic at compile time, on the host's thread (464 KB of source took 53 s, was 0.6 s) | `Oracle/syntax-errors.tsv` (the answers), `Syntax/CompileScalingTests` (the time) |
| `datetime(fold=<a huge int>)`, `Decimal((0, (1,), <a huge int>))`, `format(Fraction(...), ...)` of a huge value or precision | .NET's quadratic `BigInteger.ToString`, uncharged and past the last deadline read (5.9 s at a limit of 3). Now CPython's `OverflowError`, and its `ValueError` past 4,300 digits for the Fraction formats | `Oracle/r4_required.py` |
| `unicodedata.normalize` of a long run of combining marks, `json.dumps(sort_keys=True)` over long keys | one native sort, no deadline read and uncharged (6.8 s at a limit of 3; 1.3 s now). A counting sort and a charged merge | `Oracle/r4_required.py` |
| `global x` or `nonlocal x` after `[(x := ...) for ...]` in a function | compiled, then an uncatchable engine fault. Now CPython's `SyntaxError` | `Oracle/syntax-errors.tsv` |
| A method with defaults under a long name | each default's hidden name repeated the method's: 67 KB of source charged 191 MB | `Syntax/CompileScalingTests` |
| A class with a long name and private names (`x.__a` in its methods) | every occurrence was mangled into its own `_Class__a` string at compile time, on the host's thread and outside every limit: 160 KB of source took 980 MB; now each distinct name is mangled once per class and all of them together are capped (`deviations.md`) | `Syntax/PrivateNameMangleTests` |
| A host thread aborted while it built one of the Unicode 16 class tables (compiling a source with an invalid character or an NFKC identifier) | the table's `Lazy<T>` kept the abort, and every later run or compile that needed it was an engine fault in every engine until restart; now a build cut short publishes nothing | `Infra/NoFailureCachingLazyTests` (no failure-caching `Lazy` in the engine) |
| A function, type or iterator the host makes with `ctx.Values` and hands over other than as a module's top-level member | ran unfenced: a host failure was an engine fault and a static `ScriptExceptions` object carried one run's notes into another | `Hosting/HostIsolationTests` |

## In 1.1.0

### Resource limits that did not hold

| defect | what happened | held by |
|---|---|---|
| `RegexPerOpTimeoutMs` set for a run | ignored: every pattern had a fixed 250 ms timeout | `Modules/ReWrapperTests` `ThePatternTimeoutIsTheRunsLimit` |
| `SleepTotalMs` | not enforced: each sleep was cut at 5 s without a word | `Modules/TimeSleepStrftimeTests` `SleepPastTotalFailsBeforeWaiting`; `Hosting/ScriptEngineTests` `SleepIsCountedInStats` |
| `dict` and `set` past `MaxCollectionItems` | grew without the cap that stops a list | `Values/TablePresizeTests` `ATableStopsOnMaxCollectionItems` |
| Outputs built before their size was checked: `str.replace('')`, `%`-formatting, `json.dumps`, `re.sub` with a function, `ET.tostring`, `textwrap.indent`, `bytes.join`, `itertools.tee`, `calendar.month` | the whole result was allocated first | `Stress/ModuleCatalogStressTests` `OutputBombsStopAtTheCap` |
| Digit grouping of a huge int (`format(2**200000, '_b')`) | quadratic, past the deadline | `Builtins/BuiltinsFormatTests` `GroupingAHugeIntIsLinear` |
| Draining a `set` or `dict` with `pop()`/`popitem()` | quadratic | `Values/OrderedTableTests` `DrainingABigSetOrDictIsLinear` |
| `del a[::k]` | quadratic and uncharged | `Values/ListTupleTests` `ExtendedSliceDeleteMatchesTheIndices` |
| `str.expandtabs` | charged the whole length again per tab and hit the memory limit spuriously | `Builtins/BuiltinsStrMethodsTests` `ExpandTabsChargesTheResultOnce` |
| `statistics` over a long iterable | materialized it uncharged and uncapped | `Modules/StatisticsModernTests` `MaterializingIsCapped` |
| `format(x, '.20000f')` | grew a process-wide cache that every later run kept | `Values/FloatReprTests` `APrecisionPastTheCacheIsNotKept` |
| `type()` of a namedtuple | went through a process-wide cache keyed by the script's name | `Modules/CollectionsNamedTupleTests` `TypeIsTheReturnedClass` |
| Nested `functools.partial` | copied arguments uncharged: 100,000 levels ran 34 s and took 1.3 GB | `Modules/FunctoolsTests` `PartialChainChargesItsCopies` |
| A host raising `MaxDataDepth` very high | `repr` of ~200,000 nested deques overflowed the process stack | `Stress/ValueModelStressTests` `DeepLeafReentryAbortsOnStack`; `Runtime/ResourceLimitsTests` `DataDepthClampedToEngineHardMaximum` |
| `EngineOptions.DefaultLimits` above the hard caps | not clamped for a run without limits of its own | `Hosting/ScriptEngineTests` `CeilingAboveHardCapIsClamped` |
| `MaxIntBits` | had no hard cap | `Runtime/ResourceLimitsTests` `IntBitsClampedToEngineHardMaximum` |
| `int()` of more digits than `MaxIntBits` holds | ended on `MaxSteps` after a long parse | `Stress/BuiltinsStressTests` `OverlongIntParseAbortsOnIntBits` |
| `int.bit_length()`/`bit_count()` of a huge int, `Decimal` and `Fraction` literals of a million digits | ran past the deadline | `Modules/GapClosureTests`; `Modules/DecimalValueTests`; `Modules/FractionValueTests` `LongLiteralAbortsOnIntBits` |
| `pow(b, e, m)`, `math.isqrt`, `Fraction` arithmetic, `int.from_bytes`/`to_bytes` on huge numbers | ran past the deadline in one native call | `Values/BigOpsTests` `HugeModPowEndsOnBudget`; `Modules/MathModernTests` `IsqrtExactAcrossSizes`; `Stress/BuiltinsStressTests` `HugeFromBytesAbortsOnIntBits` |
| One long big-int operation (a million-bit gcd, division, power or `str()`) in a stopped run | delayed the end of the run by its own length | `Values/BigOpsTests` `LongGcdEndsAtTheDeadline`, `LongDecimalStringEndsAtTheDeadline`, `LongIntParseEndsAtTheDeadline` |
| `asdict`/`astuple` of a dataclass that contains itself | ended the run uncatchably | `Modules/DataclassTests` `ACycleIsACatchableRecursionError` |

### Engine faults from script content

| defect | what happened | held by |
|---|---|---|
| A wrong argument: `random.choice()`, `ET.fromstring(b'\xff')`, `uuid.UUID(version='4')`, `b'..'.replace(a, b, None)` and others | an uncatchable engine error instead of the script's exception | `Modules/ArgumentFaultTests` `AWrongArgumentIsAScriptError` |
| `isinstance`/`issubclass` of a class against a builtin type | an engine fault | `Modules/EnumTests` `AClassCheckedAgainstABuiltinTypeIsNotASubclass` |

### Wrong answers

| defect | what happened | held by |
|---|---|---|
| A comparison or `key` that raised during a sort | left the list empty | `Modules/SortTests` `ARaisingComparisonLeavesTheListWhole` |
| `def`/`class`/`import`/`except as`/`del` of a `nonlocal` or captured name | acted on a same-named global | `grammar/scoping.py` |
| Arguments a builtin does not take (`math.sqrt(4, 5)`, `'x'.strip(chars='x')`) and keywords it does (`pow(mod=)`, `replace(count=)`, `b64decode(validate=)`) | dropped silently | `Modules/ArgumentFaultTests` `NoArgumentIsDroppedSilently`; `Modules/Base64Tests` `KeywordsBindAsInCPython` |
| `hmac.new(key, msg=...)` | ignored `msg` | `Modules/HashlibTests` `HmacArgumentsBindByName` |
| `statistics.harmonic_mean` and `linear_regression` of iterators | read each iterator twice | `Modules/StatisticsModernTests` `IteratorsAreReadOnce` |
| `1 // 0.1`, `-6.0 % 3`, `float('-0.0')` | differed from CPython (`10.0`, `-0.0`, lost sign) | `Values/NumericArithTests` `FloatFloorDivModMatchCPython` |
| `bytes` find/count/replace with an empty needle or a window | differed from `str` | `Values/BytesTests` `EmptyNeedlesAndWindowsMatchStr` |
| `json.dumps` of data nested past `MaxDataDepth` | `ValueError` instead of CPython's `RecursionError` | `Modules/JsonTests` `DumpsTooDeepIsRecursionError` |
| A callback from a Python prelude module into the main script | ran on the prelude's evaluator | `Hosting/ScriptEngineTests` `PreludeCallbacksRunTheMainScript` |
| `calendar.IllegalMonthError(13)` and its siblings | not classes | `Modules/CalendarTests` `TheErrorsAreClasses` |
| A `Decimal` sum that is exactly zero | took the backend's zero sign: `Decimal('1.30') - Decimal('1.3')` was `Decimal('-0.00')` | `Modules/DecimalValueTests` `ZeroSumSign`, `ZeroSumIsNegativeUnderRoundFloor` |
| `csv.writer` with `doublequote=False`, an escape character, or a line-terminator character in a field | quoted a field that CPython only escapes, left the escape character bare inside quotes | `Modules/CsvTests` `WriterQuotesAsCPython`; `csv/csv_write_escape.py` (CPython's `test_write_escape` vectors) |
| `functools.lru_cache(typed=True)`, `maxsize=0` | `typed` was ignored (`f(1)` answered `f(1.0)`), `maxsize=0` cached as much as the cap allows | `Modules/FunctoolsTests` `LruCacheTypedAndMaxsizeZero` |
