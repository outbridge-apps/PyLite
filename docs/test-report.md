# Test report

Written by `tools/run-gates.ps1`; run it to repeat every number below (see `TESTING.md`).

| | |
|---|---|
| PyLite | 1.1.0, commit `87c9624` |
| run | 2026-10-29 19:01 to 19:08 UTC |
| OS | Microsoft Windows Server 2022 Datacenter Evaluation (10.0.20348) |
| CPU | Intel(R) Xeon(R) CPU E5-2650 v2 @ 2.60GHz, 8 logical processors |
| .NET | Framework 4.8 (release 528449), SDK 9.0.308 |
| reference | IronPython 3.4.2 (NuGet) |
| xUnit | 2.9.2 |

## Gates

| gate | result | outcome | time |
|---|---|---|---:|
| build (PyLite.sln, Release) | pass | warnings as errors in the engine | 44 s |
| xUnit, everyday (Category!=MachineSensitive) | pass | 6832 passed, 0 failed of 6832 | 125 s |
| xUnit, MachineSensitive | pass | 175 passed, 0 failed of 175 | 77 s |
| conformance (CPython Lib/test cases) | pass | 174 passed, 0 failed, 0 skipped | 13 s |
| differential (vs IronPython 3.4.2) | pass | 87 scripts: 69 match, 18 known difference(s), 0 bug(s) | 36 s |
| adversarial (Stress corpus) | pass | 130 probes, the sandbox held on every one | 165 s |

## Differential: PyLite and IronPython 3.4.2 on the same scripts

Each script sets `result`; the two are compared by `repr` (a `dict` order-free, floats by their bits), or by
the exception class when the script raises. A known difference is listed with its reason in
`tools/PyLite.DiffHarness/known-differences.txt`.

| script | class | detail |
|---|---|---|
| float_floordiv_iron.py | Known | value differs (known): (9.0, -1.0, 9.0, -1.0) vs (10.0, -0.0, 9.0, -1.0) |
| iron34/aware_naive_eq.py | Known | outcome differs (known) |
| iron34/decimal_bad_rounding.py | Known | value differs (known): TypeError vs 'KeyError' |
| iron34/float_text.py | Known | value differs (known): (2.67, '1e+16', '1e-05') vs (2.6800000000000002, '10000000000000000.0', '1.0000000000000001e-05') |
| iron34/json_decode_error.py | Known | value differs (known): ('JSONDecodeError', True) vs ('ValueError', True) |
| iron34/keyerror_str.py | Known | value differs (known): ("'k'", '1', '') vs ('k', '1', '') |
| iron34/math_log10.py | Known | value differs (known): [3.0, 6.0] vs [2.9999999999999996, 5.9999999999999991] |
| iron34/re_empty_matches.py | Known | value differs (known): ('-b--c-', ['', 'a', '', 'b', 'c', '']) vs ('-b-c-', ['a', 'bc']) |
| iron34/re_escape.py | Known | value differs (known): ['1\\+1=2', 'a\\-b\\ c', 'x_y'] vs ['1\\+1\\=2', 'a\\-b\\ c', 'x_y'] |
| iron34/re_lastgroup.py | Known | value differs (known): (None, 'x') vs ('1', 'x') |
| iron34/re_sub_bad_group.py | Known | value differs (known): error vs '\x02' |
| iron34/statistics_mode_tie.py | Known | value differs (known): 1 vs 'StatisticsError' |
| iron34/str_isnumeric.py | Known | value differs (known): [True, False] vs [False, False] |
| iron34/struct_float32.py | Known | value differs (known): 0.10000000149011612 vs 0.1 |
| iron34/textwrap_hyphens.py | Known | value differs (known): ['a-b-', 'c-', 'd-e'] vs ['a-b-', 'c-d-', 'e'] |
| iron34/timedelta_repr.py | Known | value differs (known): datetime.timedelta(days=-1, seconds=82800) vs 'datetime.timedelta(-1, 82800)' |
| iron34/unicode_case.py | Known | value differs (known): ['Ç…', True] vs ['Ç„', False] |
| iron34/url_quote_tilde.py | Known | value differs (known): ~user/a%20b vs '%7Euser/a%20b' |
| arith.py | Match |  |
| bytesio.py | Match |  |
| class_consts.py | Match |  |
| collections/counter.py | Match |  |
| collections/defaultdict_ops.py | Match |  |
| collections/deque_ops.py | Match |  |
| collections/namedtuple_ops.py | Match |  |
| collections/ordered_chain.py | Match |  |
| comprehension.py | Match |  |
| contextlib_decimal.py | Match |  |
| core/builtins_more.py | Match |  |
| core/classes_closures.py | Match |  |
| core/containers_slicing.py | Match |  |
| core/errors_builtin.py | Match |  |
| core/exceptions_flow.py | Match |  |
| csv/dict_rw.py | Match |  |
| csv/reader_dialects.py | Match |  |
| csv/sniffer.py | Match |  |
| csv/writer_quoting.py | Match |  |
| datetime/arith.py | Match |  |
| datetime/calendar_math.py | Match |  |
| datetime/errors.py | Match |  |
| datetime/format.py | Match |  |
| datetime/timezones.py | Match |  |
| decimal/money.py | Match |  |
| dict_order.py | Match |  |
| email_dates.py | Match |  |
| encoding/codecs_hashes.py | Match |  |
| encoding/struct_ops.py | Match |  |
| enum_basics.py | Match |  |
| error_div.py | Match |  |
| float_divmod.py | Match |  |
| float_repr.py | Match |  |
| fractions/ops.py | Match |  |
| functools/reduce_partial_cache.py | Match |  |
| int_big.py | Match |  |
| itertools/combinatorics.py | Match |  |
| itertools/groupby_errors.py | Match |  |
| itertools/slicing_filters.py | Match |  |
| json/errors.py | Match |  |
| json/options.py | Match |  |
| json_rt.py | Match |  |
| math/functions.py | Match |  |
| math_fns.py | Match |  |
| re/errors.py | Match |  |
| re/findall_sub.py | Match |  |
| re/lookaround_escape.py | Match |  |
| re/search_groups.py | Match |  |
| re/split_flags.py | Match |  |
| re/unicode_classes.py | Match |  |
| sorted_key.py | Match |  |
| statistics/central_spread.py | Match |  |
| statistics/errors.py | Match |  |
| stdlib/small_modules.py | Match |  |
| str_format.py | Match |  |
| string_template.py | Match |  |
| strings.py | Match |  |
| super_chain.py | Match |  |
| text/str_methods.py | Match |  |
| text/textwrap_ops.py | Match |  |
| try_else_raise.py | Match |  |
| try_match_error_finally.py | Match |  |
| urllib/quote_query.py | Match |  |
| urllib/split_join.py | Match |  |
| with_stmt.py | Match |  |
| xml/build_errors.py | Match |  |
| xml/parse_find.py | Match |  |
| zipfile_rt.py | Match |  |
| zlib_gzip.py | Match |  |

## Adversarial: which limit stopped each probe

`ok` and `raise` mean the script finished or raised a Python exception it was meant to raise; `budget X`
names the limit that stopped it. A probe that hangs past its deadline or crashes the host would fail the
gate. The probes and their limits are in `tools/PyLite.Stress/bombs`; the groups in its README.

| probe | outcome | ms | steps | MB charged |
|---|---|---:|---:|---:|
| `b1_bytes_upper` | ok | 587 | 1967 | 15 |
| `b2_bytes_split` | ok | 1135 | 1967 | 74 |
| `b3_bytes_replace` | ok | 254 | 2002457 | 17 |
| `b4_bytes_title` | ok | 137 | 1967 | 15 |
| `b5_bytes_isalpha` | ok | 120 | 1958 | 7 |
| `c1_encode_cp866` | ok | 402 | 3923 | 22 |
| `c2_decode_cp866` | ok | 274 | 3926 | 22 |
| `c3_encode_utf8` | ok | 78 | 3924 | 30 |
| `c4_decode_utf16` | ok | 134 | 3928 | 15 |
| `c5_encode_replace` | ok | 517 | 3923 | 22 |
| `d1_deep_list_repr` | ok | 230 | 504704 | 10 |
| `d2_deep_list_eq` | ok | 218 | 109514 | 16 |
| `d3_deep_tuple_hash` | ok | 49 | 104827 | 7 |
| `d4_deep_deepcopy` | ok | 317 | 304715 | 16 |
| `d5_deep_json_dumps` | ok | 222 | 104974 | 10 |
| `d6_deep_dataclass_eq` | ok | 579 | 512657 | 9 |
| `d7_deep_dataclass_hash` | ok | 172 | 306411 | 6 |
| `d8_deep_record_repr` | ok | 424 | 810952 | 14 |
| `d9_deep_deque_eq` | ok | 430 | 915784 | 29 |
| `da_deep_deque_repr` | ok | 299 | 507970 | 16 |
| `db_deep_slice_repr` | ok | 255 | 306264 | 7 |
| `dc_deep_partial_repr` | ok | 221 | 407843 | 8 |
| `dd_deep_chainmap_eq` | ok | 779 | 519227 | 61 |
| `de_deep_exception_repr` | ok | 116 | 306264 | 10 |
| `g1_gzip_bomb` | budget MaxStrChars | 6 | 18 | 0 |
| `g2_zlib_bomb` | budget MaxStrChars | 3 | 18 | 0 |
| `h1_sha224` | ok | 561 | 3930 | 15 |
| `h2_pbkdf2_iters` | budget DeadlineMs | 20004 | 1052672 | 0 |
| `j1_invalid_tail` | ok | 1188 | 1505890 | 28 |
| `j2_valid_big` | ok | 955 | 1500032 | 28 |
| `j3_many_spaces` | ok | 305 | 400031 | 30 |
| `j4_many_strings` | ok | 509 | 400031 | 43 |
| `j5_dumps_big` | ok | 1622 | 3000031 | 20 |
| `j6_deep_nested` | ok | 11 | 546 | 0 |
| `j7_parse_float_hook` | ok | 3562 | 900033 | 38 |
| `l1_reentry_deque_repr` | ok | 751 | 1025655 | 32 |
| `l2_reentry_exc_repr` | ok | 542 | 612514 | 20 |
| `l3_reentry_slice_str` | ok | 463 | 612514 | 15 |
| `l4_reentry_asdict` | ok | 831 | 612532 | 14 |
| `l5_partial_chain` | budget MaxAllocBytes | 832 | 91352 | 1024 |
| `l6_partial_big_args_calls` | budget DeadlineMs | 3002 | 52055349 | 40 |
| `l7_partial_kw_merge` | ok | 3255 | 4287428 | 143 |
| `l8_sleep_past_total` | budget SleepTotalMs | 28 | 18 | 0 |
| `l9_int_overlong` | budget MaxIntBits | 37 | 247 | 1 |
| `la_int_base36_long` | budget DeadlineMs | 3036 | 35255733 | 0 |
| `lb_bit_length_1m` | ok | 25 | 20 | 0 |
| `lc_decimal_overlong` | ok | 69 | 273 | 1 |
| `ld_decimal_long_fraction` | ok | 49 | 266 | 3 |
| `le_fraction_overlong` | budget MaxIntBits | 79 | 264 | 1 |
| `lf_fraction_power_of_ten` | budget MaxIntBits | 6 | 20 | 0 |
| `lg_fraction_pow` | budget MaxIntBits | 63 | 20 | 0 |
| `lh_fraction_big_parts` | budget DeadlineMs | 3016 | 950 | 0 |
| `li_modpow_huge` | budget DeadlineMs | 3006 | 3206401 | 0 |
| `lj_modinverse_huge` | budget DeadlineMs | 3008 | 3 | 0 |
| `lk_isqrt_1m` | ok | 1432 | 40 | 0 |
| `ll_gcd_huge` | budget DeadlineMs | 3010 | 25600 | 0 |
| `lm_str_huge` | budget DeadlineMs | 3009 | 320412175 | 0 |
| `ln_from_bytes_huge` | budget MaxIntBits | 341 | 2449 | 9 |
| `lo_to_bytes_big` | ok | 684 | 14 | 0 |
| `lp_to_bytes_len_cap` | budget MaxStrChars | 15 | 2 | 0 |
| `lq_round_fraction_far` | ok | 5 | 25 | 0 |
| `lr_pow_exact_cap` | ok | 1 | 11 | 0 |
| `m1_stdev_decimal` | ok | 772 | 1814193 | 28 |
| `m2_median_decimal` | ok | 327 | 1814175 | 29 |
| `m3_decimal_ln_loop` | ok | 17211 | 20136658 | 168 |
| `m4_decimal_extremes` | ok | 6 | 188 | 0 |
| `p1_eq_recurses` | budget MaxCallDepth | 6 | 608 | 0 |
| `p2_hash_recurses` | budget MaxCallDepth | 8 | 408 | 0 |
| `p3_str_recurses` | budget MaxCallDepth | 4 | 408 | 0 |
| `p4_len_recurses` | budget MaxCallDepth | 4 | 408 | 0 |
| `p5_bool_recurses` | budget MaxCallDepth | 6 | 408 | 0 |
| `p6_iter_never_ends` | budget MaxCollectionItems | 1411 | 15078134 | 381 |
| `p7_getitem_never_ends` | budget MaxCollectionItems | 4637 | 15078132 | 586 |
| `p8_contains_recurses` | budget MaxCallDepth | 6 | 405 | 0 |
| `p9_id_many` | ok | 2745 | 6062515 | 260 |
| `pa_call_recurses` | budget MaxCallDepth | 4 | 406 | 0 |
| `r1_compile_long` | raise error: regular expression t | 21 | 125086 | 1 |
| `r2_compile_named` | raise error: regular expression t | 1 | 12588 | 0 |
| `r3_counted_repeats` | raise error: regular expression t | 1 | 93836 | 1 |
| `s1_str_upper` | ok | 142 | 1969 | 30 |
| `s2_str_casefold` | ok | 222 | 1970 | 45 |
| `t1_struct_iter_unpack` | ok | 736 | 6000034 | 165 |
| `u1_quote` | ok | 191 | 3938 | 61 |
| `u2_unquote` | ok | 466 | 2001983 | 30 |
| `u3_parse_qs` | ok | 1442 | 3000022 | 95 |
| `u4_urlencode` | ok | 1048 | 4142543 | 107 |
| `u5_urlsplit_long` | ok | 86 | 1006 | 22 |
| `u6_urljoin_taught` | ok | 380 | 2166345 | 23 |
| `v1_reader_many_fields` | ok | 929 | 4000035 | 80 |
| `v2_reader_huge_field` | ok | 304 | 8000038 | 61 |
| `v3_writer_many_fields` | ok | 696 | 2002008 | 38 |
| `v4_sniff_big` | ok | 823 | 837538 | 4 |
| `v5_has_header_big` | ok | 881 | 837710 | 9 |
| `v6_dictreader_rows` | ok | 690 | 4000038 | 216 |
| `v7_duck_dialect_loop` | ok | 1077 | 3043777 | 85 |
| `w1_html_unescape` | ok | 397 | 1983 | 33 |
| `w2_strftime_long_fmt` | ok | 321 | 31282 | 24 |
| `w3_strptime_long` | ok | 55 | 26 | 7 |
| `w4_fromisoformat_long` | ok | 60 | 25 | 11 |
| `w5_percent_format_many` | ok | 422 | 3925704 | 39 |
| `w6_textwrap_big` | ok | 6711 | 2062527 | 21 |
| `w7_bisect_insort` | ok | 15275 | 12277597 | 14 |
| `w8_heapq_big` | ok | 851 | 72000070 | 200 |
| `w9_deque_rotate` | ok | 321 | 1000276 | 49 |
| `wa_calendar_year` | raise AttributeError: module 'cale | 9 | 20 | 0 |
| `wb_unicodedata_lookup_loop` | ok | 971 | 2160966 | 52 |
| `wc_operator_itemgetter` | ok | 136 | 400038 | 16 |
| `wd_counter_most_common` | ok | 802 | 4000043 | 140 |
| `we_bytes_fromhex` | ok | 101 | 1967 | 19 |
| `wf_int_str_huge` | budget MaxIntBits | 0 | 0 | 0 |
| `x1_fromstring_flat` | ok | 2440 | 1500030 | 120 |
| `x2_tostring_html_wide` | ok | 2626 | 2039098 | 92 |
| `x3_tostring_text_wide` | ok | 2063 | 2046909 | 89 |
| `x4_deep_built_tostring` | ok | 1074 | 1015660 | 38 |
| `x5_deep_built_iter` | ok | 927 | 1015662 | 41 |
| `x6_deep_built_itertext` | ok | 856 | 1225038 | 35 |
| `x7_deep_built_parentmap` | ok | 1194 | 1215661 | 38 |
| `x8_deep_built_indent` | budget MaxAllocBytes | 3168 | 627025 | 1024 |
| `x9_deep_built_xmlid` | ok | 1517 | 2963223 | 60 |
| `xa_deep_built_deepcopy` | ok | 583 | 615669 | 35 |
| `xb_xpath_text_quadratic` | ok | 317 | 390029 | 14 |
| `xc_xmlid_big` | ok | 2084 | 1802730 | 179 |
| `xd_iterparse_big` | ok | 2412 | 2501031 | 192 |
| `xe_find_wide` | ok | 1373 | 1000027 | 96 |
| `z1_zip_deflate_bomb` | budget MaxAllocBytes | 479 | 55021 | 71 |
| `z2_zip_nested` | budget MaxAllocBytes | 547 | 55404 | 71 |
| `z3_zip_many_entries` | budget MaxStrChars | 1711 | 1522600 | 118 |
| `z4_zip_overlap` | ok | 16 | 117 | 0 |
| `z5_zip_forged_size` | ok | 262 | 36749 | 43 |
| `z6_zip_not_an_archive` | ok | 318 | 3991 | 30 |
