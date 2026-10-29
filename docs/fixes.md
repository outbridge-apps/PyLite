# Fixed defects

Every defect fixed since version 1.0.0, with the test that keeps it fixed. Test classes are under
`tests/PyLite.Tests/`; conformance cases under `conformance/cases/`. A defect found by the differential
corpus, the adversarial probes or a review gets its row here in the commit that fixes it.

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
